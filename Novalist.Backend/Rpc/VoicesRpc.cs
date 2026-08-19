using Novalist.Backend.Speech;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// The voices the operating system can speak with, and speaking through them.
///
/// The renderer used the browser's <c>speechSynthesis</c>, which on Windows
/// reads one voice store while everything a writer installs to get more voices
/// registers in the other. A machine offering every other desktop application
/// three hundred voices offered Novalist three, and no setting could change it.
/// </summary>
public sealed class VoicesRpc
{
    private readonly Workspace _workspace;
    private readonly ISystemVoices _voices;

    public VoicesRpc(Workspace workspace, ISystemVoices? voices = null)
    {
        _workspace = workspace;
        // SAPI where there is SAPI, the platform's own command elsewhere, and
        // the browser's voices where there is neither. Only the first of those
        // existed, so a reading on a Mac was silent - and worse than silent,
        // because the loop takes a refused passage as its cue to move on and
        // swept the highlight through the whole book in a second.
        _voices = voices ?? (ISystemVoices?)CommandVoices.ForThisMachine() ?? new SystemVoices();
    }

    /// <summary>
    /// The system voices, the ones for the writing language first. Empty where
    /// there is no system engine, which is the signal to keep using the
    /// browser's own list.
    /// </summary>
    [JsonRpcMethod("voices/list")]
    public SystemVoiceDto[] List()
    {
        if (!_voices.Available) return [];
        var language = _workspace.Settings.Effective.AutoReplacementLanguage;
        return [.. VoiceCatalog.ForPicker(_voices.List(), language)
            .Select(v => new SystemVoiceDto(v.Id, v.Name, v.Language))];
    }

    /// <summary>
    /// Speaks one passage. The renderer sends a sentence at a time so it can
    /// keep highlighting the one being read - moving that here would mean
    /// re-inventing the editor's own idea of where a sentence starts.
    /// </summary>
    [JsonRpcMethod("voices/speak")]
    public async Task<bool> SpeakAsync(string text, string? voiceId = null, double rate = 1.0)
    {
        if (!_voices.Available) return false;
        var chosen = VoiceCatalog.Choose(
            _voices.List(), voiceId, _workspace.Settings.Effective.AutoReplacementLanguage);
        _voices.Speak(text, chosen?.Id, rate);

        // The answer is what tells the editor to move to the next sentence, so
        // it has to wait for this one. Off the dispatcher thread, or stopping
        // could not be served while a sentence was in the air.
        return await Task.Run(_voices.WaitUntilDone);
    }

    [JsonRpcMethod("voices/stop")]
    public void Stop() => _voices.Stop();
}

/// <summary>One system voice as the renderer lists it.</summary>
public sealed record SystemVoiceDto(string Id, string Name, string Language);
