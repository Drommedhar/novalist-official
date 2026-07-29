using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// One scene that turns a character, and what turns in it.
/// </summary>
public sealed class ArcPoint
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = System.Guid.NewGuid().ToString();

    /// <summary>The scene where it happens. Empty while the writer has only
    /// decided that it happens, not where.</summary>
    [JsonPropertyName("sceneId")]
    public string SceneId { get; set; } = string.Empty;

    /// <summary>What changes, in the writer's words: "stops lying to herself".</summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;
}

/// <summary>
/// A character's trajectory: where they start, where they end, and the scenes
/// that move them.
///
/// Novalist could already say what a character is like at a point in the story
/// through per-scope overrides. What it could not say is what the change is
/// for - so no view could show a character's arc against the book, and no
/// scene could be marked as the one where they turn.
/// </summary>
public sealed class CharacterArc
{
    /// <summary>Who they are at the start.</summary>
    [JsonPropertyName("start")]
    public string Start { get; set; } = string.Empty;

    /// <summary>Who they are at the end.</summary>
    [JsonPropertyName("end")]
    public string End { get; set; } = string.Empty;

    /// <summary>The scenes that turn them, in the order the writer added them.</summary>
    [JsonPropertyName("points")]
    public List<ArcPoint> Points { get; set; } = [];
}
