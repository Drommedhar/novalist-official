using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// One entry in the book's keyword vocabulary.
///
/// Scene tags were a free-text list per scene with nothing behind them: no
/// registry, no colours, and no way to rename one. So "flashback", "Flashback"
/// and "flash-back" were three tags, and correcting that meant opening every
/// scene that used the wrong one.
///
/// A keyword has an id of its own so that renaming it is a rename and not a
/// delete-and-recreate: the scenes that carry it keep carrying it.
/// </summary>
public sealed class Keyword
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Hex colour drawn on the chip. Never empty: an uncoloured chip
    /// among coloured ones reads as a mistake rather than as a choice.</summary>
    [JsonPropertyName("color")]
    public string Color { get; set; } = "#8b8b8b";

    /// <summary>
    /// The keyword this one sits under, or null at the top level. One level of
    /// grouping - "Themes" over "grief", "loss" - is what makes a vocabulary of
    /// forty legible; deeper nesting is a filing system nobody maintains.
    /// </summary>
    [JsonPropertyName("parentId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentId { get; set; }

    [JsonPropertyName("order")]
    public int Order { get; set; }
}
