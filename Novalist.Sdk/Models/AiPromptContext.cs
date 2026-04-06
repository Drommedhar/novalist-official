namespace Novalist.Sdk.Models;

/// <summary>
/// Context passed to AI hooks when building the system prompt.
/// </summary>
public sealed class AiPromptContext
{
    public string CurrentChapterTitle { get; init; } = string.Empty;
    public string CurrentSceneTitle { get; init; } = string.Empty;
    public IReadOnlyList<string> CharacterNames { get; init; } = [];
    public IReadOnlyList<string> LocationNames { get; init; } = [];
    public string Language { get; init; } = "en";
}
