using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// What an entry is like at one point in the story, rather than in general.
///
/// Characters have had per-chapter and per-scene overrides for a long time.
/// Nothing else did, so a city razed in act two, an artefact that changes hands,
/// or a faction that falls could only be described as it is at the end - and a
/// reader of the Codex in chapter three would be told the ending.
///
/// Deliberately a loose patch rather than a typed mirror of each entity: the
/// interesting restatements are a description and a couple of fields, and five
/// parallel typed override classes would be five places to keep in step.
/// </summary>
public sealed class EntityStateOverride
{
    /// <summary>Act this applies from, or null when scoped by chapter instead.</summary>
    [JsonPropertyName("act")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Act { get; set; }

    /// <summary>Chapter guid or title this applies to. Empty for an act-wide
    /// override.</summary>
    [JsonPropertyName("chapter")]
    public string Chapter { get; set; } = string.Empty;

    /// <summary>Scene title this applies to, or null for the whole chapter.</summary>
    [JsonPropertyName("scene")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Scene { get; set; }

    /// <summary>What the entry is called at this point, if that changed.</summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    /// <summary>What the entry is like at this point.</summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    /// <summary>
    /// Any other field, by its key - a location's Type, an item's Owner, a
    /// custom property. Only the keys present are restated; everything else
    /// still reads from the entry itself.
    /// </summary>
    [JsonPropertyName("fields")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Fields { get; set; }

    /// <summary>The writer's note on why, shown beside the restated values.</summary>
    /// <summary>
    /// From this point the entry is out of the story: dead, departed, destroyed.
    ///
    /// Novalist tracked what an entry was like at a point in the story and never
    /// that it had stopped being in it, so nothing could notice a character
    /// standing in a scene two chapters after their funeral.
    /// </summary>
    [JsonPropertyName("gone")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Gone { get; set; }

    [JsonPropertyName("note")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; set; }

    /// <summary>Whether this override restates anything at all.</summary>
    [JsonIgnore]
    public bool HasValues =>
        // Gone counts: an override that says only "and then they were gone" is
        // the whole point of the marker and must not be pruned as empty.
        Name != null || Description != null || (Fields is { Count: > 0 }) || Gone;
}
