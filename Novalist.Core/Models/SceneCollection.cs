using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// A hand-curated set of scenes, kept in an order the writer chose.
///
/// A saved list answers "which scenes match this query" and is recomputed every
/// time it is opened. A collection answers a question a query cannot: the eight
/// scenes I have to fix before Tuesday, the run I am reading to my group, the
/// ones a beta reader stumbled on. Nothing they have in common is expressible as
/// a filter - that is precisely why they had to be gathered by hand.
///
/// The scene ids are the collection. Membership does not touch the scene, so a
/// scene can be in five collections and none of them changes the manuscript.
/// </summary>
public sealed class SceneCollection
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Where this collection sits in the panel.</summary>
    [JsonPropertyName("order")]
    public int Order { get; set; }

    /// <summary>
    /// The scenes, in the order the writer put them. Not reading order: a
    /// revision run is often deliberately out of sequence, and re-sorting it
    /// would throw away the only thing the writer said about it.
    /// </summary>
    [JsonPropertyName("sceneIds")]
    public List<string> SceneIds { get; set; } = [];
}
