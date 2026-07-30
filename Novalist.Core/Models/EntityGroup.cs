using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// A faction, house, crew or family: a named set that spans entity types.
///
/// The group itself was a bare string on each entry, so it could say a house and
/// a ship belong to the Ravens and nothing more. It had no colour, no
/// description, no count and no way to be renamed - correcting "the Ravens" to
/// "House Raven" meant opening every entry that said the first thing.
///
/// A group has an id so renaming is a rename: the entries that belong to it keep
/// belonging to it.
/// </summary>
public sealed class EntityGroup
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Hex colour, drawn on the group's entries and on the scenes
    /// they appear in. Never empty: one uncoloured badge among coloured ones
    /// reads as a mistake rather than as a choice.</summary>
    [JsonPropertyName("color")]
    public string Color { get; set; } = "#8b8b8b";

    /// <summary>What the group is, in the writer's words. Never printed.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("order")]
    public int Order { get; set; }
}
