using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// An article that is about the world rather than about one entry in it.
///
/// Every Wiki article was generated from a Codex entity, so an essay on how the
/// economy works, or on the rules of the magic, had to be hung off whichever
/// entity it least badly belonged to - or kept in Research, outside the Wiki
/// entirely. Only Locations nested, so there was no way to file one under
/// another either.
/// </summary>
public sealed class WikiPage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The page this one sits under, or empty for a top-level page. Any depth:
    /// a world's rules nest as far as the world does.
    /// </summary>
    [JsonPropertyName("parentId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string ParentId { get; set; } = string.Empty;

    /// <summary>The article itself, in the same formatted text sections use.</summary>
    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    /// <summary>Where it sits among its siblings.</summary>
    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
