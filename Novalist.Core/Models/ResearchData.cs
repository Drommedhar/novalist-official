using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResearchItemType
{
    Note,
    Link,
    File,
    Image,
    Pdf
}

/// <summary>
/// One reference resource attached to a project: a free-form note, an external
/// URL, or a path to an imported file (PDF, image, audio, video, etc.).
/// </summary>
public sealed class ResearchItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = System.Guid.NewGuid().ToString();

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public ResearchItemType Type { get; set; } = ResearchItemType.Note;

    /// <summary>For Note: the prose. For Link: the URL. For files: project-relative path.</summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("createdAt")]
    public System.DateTime CreatedAt { get; set; } = System.DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public System.DateTime UpdatedAt { get; set; } = System.DateTime.UtcNow;

    [JsonPropertyName("order")]
    public int Order { get; set; }
}
