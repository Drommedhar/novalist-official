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
    public IReadOnlyList<string> ItemNames { get; init; } = [];
    public IReadOnlyList<string> LoreNames { get; init; } = [];

    /// <summary>
    /// Custom entity names grouped by type display name. Key = type display name, Value = entity names.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> CustomEntityNames { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    public string Language { get; init; } = "en";
}
