using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// A loose idea on the planning board. Deliberately not a scene: the point of a
/// canvas is somewhere to put a thought before it has earned a place in the
/// manuscript. A card can later be promoted into a real scene, which is the only
/// moment it touches the book's structure.
/// </summary>
public class CanvasCard
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; } = 200;

    [JsonPropertyName("height")]
    public double Height { get; set; } = 120;

    /// <summary>Label colour, as a token key ("accent", "warning", ...) rather
    /// than a hex value, so a card follows the active theme.</summary>
    [JsonPropertyName("color")]
    public string Color { get; set; } = string.Empty;

    /// <summary>
    /// Scene this card became, once promoted. Set means the idea has landed in
    /// the manuscript; the card stays on the board as a pointer to it.
    /// </summary>
    [JsonPropertyName("sceneId")]
    public string SceneId { get; set; } = string.Empty;

    [JsonPropertyName("chapterGuid")]
    public string ChapterGuid { get; set; } = string.Empty;

    /// <summary>Codex entry this card refers to, when it is about one.</summary>
    [JsonPropertyName("entityId")]
    public string EntityId { get; set; } = string.Empty;
}

/// <summary>
/// An author-drawn line between two cards. The label is the whole point - "because
/// of", "three weeks later", "but only if she lied" - so an unlabelled connector
/// is valid but carries no meaning the board can show.
/// </summary>
public class CanvasConnector
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("fromCardId")]
    public string FromCardId { get; set; } = string.Empty;

    [JsonPropertyName("toCardId")]
    public string ToCardId { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;
}

/// <summary>One planning board.</summary>
public class CanvasData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("cards")]
    public List<CanvasCard> Cards { get; set; } = [];

    [JsonPropertyName("connectors")]
    public List<CanvasConnector> Connectors { get; set; } = [];

    /// <summary>Viewport the board was last left at, so reopening it does not
    /// dump the writer at the origin of an infinite plane.</summary>
    [JsonPropertyName("panX")]
    public double PanX { get; set; }

    [JsonPropertyName("panY")]
    public double PanY { get; set; }

    [JsonPropertyName("zoom")]
    public double Zoom { get; set; } = 1;
}

/// <summary>Board listed in the book without loading its contents.</summary>
public class CanvasReference
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
