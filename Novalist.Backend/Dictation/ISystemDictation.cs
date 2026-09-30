namespace Novalist.Backend.Dictation;

public sealed record SystemDictationLanguage(string Language, bool Supported, bool Installed);
public sealed record SystemDictationStatus(string Engine, bool Available, bool Online,
    bool UsesSystemPanel, SystemDictationLanguage[] Languages);

/// <summary>Operating-system speech, independent of installed extensions.</summary>
public interface ISystemDictation
{
    Task<SystemDictationStatus> StatusAsync(CancellationToken token);
    Task PrepareAsync(string language, CancellationToken token);
    Task<string> TranscribeAsync(byte[] audio, string language, CancellationToken token);
    void OpenSystemPanel();
}
