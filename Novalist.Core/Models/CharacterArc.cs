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

    /// <summary>
    /// True for the beat where the character stops chasing what they want and
    /// starts chasing what they need.
    ///
    /// One point among several, rather than a field of its own, because it is
    /// a beat that lands in a scene like any other - and the writer has to be
    /// able to move it when they find out it lands somewhere else.
    /// </summary>
    [JsonPropertyName("isTurn")]
    public bool IsTurn { get; set; }
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

    /// <summary>
    /// What they are chasing, which is not what the book is about.
    ///
    /// Start and end say who they are on either side. They do not say what
    /// pulls them across, and the want-to-need turn is the single most taught
    /// piece of arc craft there is - so a record of an arc that cannot hold it
    /// is a record of the shape without the engine.
    /// </summary>
    [JsonPropertyName("want")]
    public string Want { get; set; } = string.Empty;

    /// <summary>What they actually need, which they usually find out last.</summary>
    [JsonPropertyName("need")]
    public string Need { get; set; } = string.Empty;

    /// <summary>Who they are at the end.</summary>
    [JsonPropertyName("end")]
    public string End { get; set; } = string.Empty;

    /// <summary>The scenes that turn them, in the order the writer added them.</summary>
    [JsonPropertyName("points")]
    public List<ArcPoint> Points { get; set; } = [];
}
