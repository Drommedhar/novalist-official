namespace Novalist.Core.Services;

/// <summary>Language rules used together when attributing, directing, and splitting a scene's prose.</summary>
public sealed record NarrationLanguageContext(
    DialogueLanguage Dialogue,
    DirectionLanguage Direction,
    UtteranceLanguage? Utterances = null);

/// <summary>Scene-level assignments retain their original dictionaries; a blank direction override still means explicitly neutral.</summary>
public sealed record SceneNarrationSettings
{
    public IReadOnlyDictionary<string, string>? SpeakerOverrides { get; init; }
    public IReadOnlyDictionary<string, string>? DirectionOverrides { get; init; }
    public string? Emotion { get; init; }
    public int? Intensity { get; init; }
}
