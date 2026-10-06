using Novalist.Sdk.Models.Narration;

namespace Novalist.Core.Services;

/// <summary>Cast and engine resources shared across preview windows and audiobook chapters; supplied dictionaries are retained by reference.</summary>
public sealed record NarrationRenderContext(
    VoiceCastSheet Cast,
    IReadOnlyDictionary<string, byte[]> Voices,
    VoiceEngineFeatures Features,
    string Language)
{
    public IReadOnlyDictionary<string, byte[]>? Clips { get; init; }
    public IReadOnlyDictionary<string, string>? VoiceReferenceTexts { get; init; }
}
