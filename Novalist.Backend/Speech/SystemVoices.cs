using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Novalist.Backend.Speech;

/// <summary>One voice the operating system can speak with.</summary>
public sealed record SystemVoice(string Id, string Name, string Language);

/// <summary>
/// Reads and drives the platform's own speech engine.
///
/// The renderer has always used the browser's <c>speechSynthesis</c>, and on
/// Windows that reads one voice store - Speech_OneCore - while everything a
/// writer installs to get more voices registers in the other one, SAPI5. A
/// machine with three hundred natural voices available to every other desktop
/// application offered Novalist three, and no setting could change it.
///
/// The interop lives here and is excluded from coverage, exactly as the
/// filesystem watchers are: constructing a COM object and pumping it cannot be
/// unit-tested. Everything that decides anything is in
/// <see cref="VoiceCatalog"/>, which is.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "SAPI COM interop; the decisions are in VoiceCatalog.")]
public sealed class SystemVoices : ISystemVoices
{
    /// <summary>
    /// Speak without blocking, so <see cref="Stop"/> can still be served while a
    /// sentence is in the air. The caller waits with <see cref="WaitUntilDone"/>
    /// rather than by blocking inside the engine.
    /// </summary>
    private const int SpeakAsync = 1;

    /// <summary>
    /// Throw away whatever is queued before speaking this. Only ever right for
    /// stopping: using it to speak makes every sentence cancel the one before
    /// it, which sounds like the reading skipping through the scene.
    /// </summary>
    private const int PurgeBeforeSpeak = 2;

    /// <summary>Longest a single sentence is given before the reading moves on.</summary>
    private const int SentenceTimeoutMs = 120_000;

    private object? _voice;

    /// <summary>
    /// True where a system engine can be reached at all. Everywhere else the
    /// renderer keeps using the browser's voices, which is what it always did.
    /// </summary>
    public bool Available => OperatingSystem.IsWindows();

    public IReadOnlyList<SystemVoice> List()
    {
        if (!Available) return [];
        try
        {
            dynamic voice = Voice();
            dynamic tokens = voice.GetVoices();
            var found = new List<SystemVoice>();
            for (var i = 0; i < tokens.Count; i++)
            {
                dynamic token = tokens.Item(i);
                string id = token.Id;
                string description = token.GetDescription();
                found.Add(new SystemVoice(id, description, LanguageOf(token)));
            }
            return found;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NotSupportedException)
        {
            // A machine with no speech engine, or one that refuses to start.
            // Silence is right: the browser voices are still there.
            return [];
        }
    }

    public void Speak(string text, string? voiceId, double rate)
    {
        if (!Available || string.IsNullOrWhiteSpace(text)) return;
        try
        {
            dynamic voice = Voice();
            if (!string.IsNullOrWhiteSpace(voiceId))
            {
                dynamic tokens = voice.GetVoices();
                for (var i = 0; i < tokens.Count; i++)
                {
                    dynamic token = tokens.Item(i);
                    if (!string.Equals((string)token.Id, voiceId, StringComparison.Ordinal)) continue;
                    voice.Voice = token;
                    break;
                }
            }
            // SAPI's rate is -10..10 around normal, not a multiplier.
            voice.Rate = VoiceCatalog.ToSapiRate(rate);
            // Async and emphatically not purging: purging here would cut off
            // the sentence still being spoken every single time.
            voice.Speak(text, SpeakAsync);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            // A voice that has been uninstalled since the list was read.
        }
    }

    /// <summary>
    /// Blocks until the passage has been spoken. True when it finished, false
    /// when it was stopped or the engine gave up.
    ///
    /// The waiting is here rather than in the engine call so that stopping
    /// still works: a synchronous Speak would hold the engine and there would
    /// be nothing left to tell it to stop.
    /// </summary>
    public bool WaitUntilDone()
    {
        if (!Available || _voice == null) return false;
        try
        {
            dynamic voice = _voice;
            // A sentence that outlives the timeout is a runaway; the reading
            // moves on rather than stopping dead on it.
            return (bool)voice.WaitUntilDone(SentenceTimeoutMs);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return false;
        }
    }

    public void Stop()
    {
        if (!Available || _voice == null) return;
        try
        {
            dynamic voice = _voice;
            voice.Speak(string.Empty, SpeakAsync | PurgeBeforeSpeak);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
        }
    }

    /// <summary>The voice's language as a BCP-47 tag, or empty when it says none.</summary>
    private static string LanguageOf(dynamic token)
    {
        try
        {
            dynamic attributes = token.GetAttribute("Language");
            return VoiceCatalog.LanguageFromLcidList((string)attributes);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private object Voice()
    {
        if (!OperatingSystem.IsWindows())
            throw new NotSupportedException("SAPI is available only on Windows.");

        // One instance for the session: creating a new SpVoice per sentence
        // makes stopping impossible, because Stop would purge a queue nobody
        // is speaking from.
        _voice ??= Activator.CreateInstance(
            Type.GetTypeFromProgID("SAPI.SpVoice")
            ?? throw new NotSupportedException("No SAPI on this machine."))!;
        return _voice;
    }
}

/// <summary>The platform speech engine, behind a seam so callers can be tested.</summary>
public interface ISystemVoices
{
    bool Available { get; }
    IReadOnlyList<SystemVoice> List();

    /// <summary>Starts speaking. Returns at once; the caller waits.</summary>
    void Speak(string text, string? voiceId, double rate);

    /// <summary>Blocks until the passage is spoken, or it is stopped.</summary>
    bool WaitUntilDone();

    void Stop();
}
