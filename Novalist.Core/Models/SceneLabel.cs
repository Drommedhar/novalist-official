using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// One label a scene can carry, as the writer defines it.
///
/// SceneData has held a LabelColor since long before anything read it: a bare
/// hex string with no name, no list to pick from, and no surface that showed
/// it. A label is what that colour was for - "needs a beta read", "cut but
/// keeping", "Mira's thread" - and a colour nobody named tells a reader
/// nothing at all.
/// </summary>
public sealed class SceneLabel
{
    /// <summary>Stable identifier stored on the scene. Never shown.</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>What the writer calls it.</summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>Hex colour for the corkboard card and the binder row.</summary>
    [JsonPropertyName("color")]
    public string Color { get; set; } = "#8b8b8b";
}
