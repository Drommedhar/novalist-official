using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// One tag in the project's vocabulary, with the colour it is shown in.
///
/// Novalist had three unrelated tag notions - scene analysis tags, research
/// tags and the reserved inbox tag - none of them coloured and none of them
/// shared. This is the one list they all draw from, so a tag means the same
/// thing wherever it appears.
/// </summary>
public sealed class ProjectTag
{
    /// <summary>
    /// The tag as it is written. Compared case-insensitively everywhere, so
    /// "Flashback" and "flashback" are one tag rather than two.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>A CSS colour. Empty means the interface picks a neutral one.</summary>
    [JsonPropertyName("color")]
    public string Color { get; set; } = string.Empty;
}

/// <summary>A tag, its colour, and how many of each kind of thing carry it.</summary>
public sealed record TagUsage(
    string Name,
    string Color,
    int Scenes,
    int Entities,
    int Research)
{
    /// <summary>Everything carrying this tag, for sorting and for the manager.</summary>
    public int Total => Scenes + Entities + Research;
}
