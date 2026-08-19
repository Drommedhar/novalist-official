using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Novalist.Backend.Speech;

/// <summary>
/// The platform's speech engine on macOS and Linux, driven as a command.
///
/// There was none. <see cref="SystemVoices"/> is SAPI and answers "no" to
/// <see cref="ISystemVoices.Available"/> everywhere but Windows, and the
/// renderer's reading loop takes the answer from <c>voices/speak</c> as its cue
/// to move on - so on a Mac every call returned false at once and the reading
/// swept through the whole book in a second, silent, with the highlight racing
/// ahead of prose nobody could hear. It looked like a broken feature because on
/// those machines it was one.
///
/// Both platforms ship an engine that is always there and needs nothing
/// installed: <c>say</c> on macOS, and espeak-ng on Linux where it is by far the
/// most commonly present. Driving them as processes rather than through a
/// binding keeps the dependency at zero, which is what makes it safe to do this
/// for platforms we cannot test on every build.
///
/// Excluded from coverage with the rest of the process interop. Everything that
/// decides anything - what the voice lists mean, what a rate is in words per
/// minute - is in <see cref="VoiceCatalog"/>, which is tested.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Process interop; the decisions are in VoiceCatalog.")]
public sealed class CommandVoices : ISystemVoices
{
    /// <summary>Longest a single sentence is given before the reading moves on.
    /// The same ceiling SAPI is held to, for the same reason.</summary>
    private const int SentenceTimeoutMs = 120_000;

    private readonly string _executable;
    private readonly bool _isSay;
    private Process? _speaking;
    private IReadOnlyList<SystemVoice>? _listed;

    private CommandVoices(string executable, bool isSay)
    {
        _executable = executable;
        _isSay = isSay;
    }

    /// <summary>
    /// The engine on this machine, or null where there is none.
    ///
    /// Asked of the machine rather than assumed from the platform: a Linux box
    /// without espeak installed has no speech, and reporting that honestly is
    /// what keeps the browser's own voices in play as the fallback they have
    /// always been.
    /// </summary>
    public static CommandVoices? ForThisMachine()
    {
        if (OperatingSystem.IsMacOS())
            return Answers("say", ["-v", "?"]) ? new CommandVoices("say", isSay: true) : null;

        if (!OperatingSystem.IsLinux())
            return null;

        // espeak-ng first: espeak is its predecessor and on most distributions
        // the name is a compatibility shim for it anyway.
        foreach (var candidate in new[] { "espeak-ng", "espeak" })
        {
            if (Answers(candidate, ["--voices"]))
                return new CommandVoices(candidate, isSay: false);
        }
        return null;
    }

    public bool Available => true;

    public IReadOnlyList<SystemVoice> List()
    {
        // Read once. Listing shells out, the picker asks on every render, and
        // the set of installed voices does not change while the app is open.
        return _listed ??= Read();
    }

    private IReadOnlyList<SystemVoice> Read()
    {
        var output = Capture(_executable, _isSay ? ["-v", "?"] : ["--voices"]);
        return output == null
            ? []
            : _isSay ? VoiceCatalog.ParseSayVoices(output) : VoiceCatalog.ParseEspeakVoices(output);
    }

    public void Speak(string text, string? voiceId, double rate)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        // Whatever was in the air is finished with. The reading speaks one
        // sentence at a time and waits for each, so anything still running here
        // is a sentence somebody stopped.
        Stop();

        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(voiceId))
        {
            args.Add("-v");
            args.Add(voiceId);
        }
        args.Add(_isSay ? "-r" : "-s");
        args.Add(VoiceCatalog.ToWordsPerMinute(rate, _isSay).ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        // Last, and as one argument. Prose is passed as an argument rather than
        // through a shell, so a line containing a quote or a semicolon is a line
        // rather than a command.
        args.Add(text);

        _speaking = Start(_executable, args);
    }

    public bool WaitUntilDone()
    {
        var process = _speaking;
        if (process == null)
            return false;

        try
        {
            if (!process.WaitForExit(SentenceTimeoutMs))
            {
                // A sentence that outlives the timeout is a runaway; the reading
                // moves on rather than stopping dead on it.
                Stop();
                return false;
            }
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or SystemException)
        {
            return false;
        }
        finally
        {
            if (ReferenceEquals(_speaking, process))
                _speaking = null;
            process.Dispose();
        }
    }

    public void Stop()
    {
        var process = _speaking;
        _speaking = null;
        if (process == null)
            return;

        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or SystemException)
        {
            // Already gone, which is what was wanted.
        }
    }

    /// <summary>Whether a command exists and runs at all.</summary>
    private static bool Answers(string executable, string[] args) => Capture(executable, args) != null;

    /// <summary>What a command printed, or null where it could not be run.</summary>
    private static string? Capture(string executable, IEnumerable<string> args)
    {
        try
        {
            using var process = Start(executable, args, capture: true);
            if (process == null)
                return null;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10_000);
            return output;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException
                                       or InvalidOperationException)
        {
            return null;
        }
    }

    private static Process? Start(string executable, IEnumerable<string> args, bool capture = false)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = capture,
            RedirectStandardError = capture
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);

        try
        {
            return Process.Start(info);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            return null;
        }
    }
}
