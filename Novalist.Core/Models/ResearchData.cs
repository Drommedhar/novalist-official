using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResearchItemType
{
    Note,
    Link,
    File,
    Image,
    Pdf,

    /// <summary>
    /// An interview, a field recording, a piece of music the scene is written
    /// to. Novalist could hold the file but had no type that said what it was,
    /// so it could never be played.
    /// </summary>
    Audio,

    /// <summary>A clip, a reference performance, a location walk-through.</summary>
    Video
}

/// <summary>
/// One reference resource attached to a project: a free-form note, an external
/// URL, or a path to an imported file (PDF, image, audio, video, etc.).
/// </summary>
public sealed class ResearchItem
{
    /// <summary>Reserved tag marking a quick-captured note that has not been filed
    /// anywhere yet. The Research view groups these as the "Inbox".</summary>
    public const string InboxTag = "inbox";

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

    /// <summary>Ids of Codex entities this item is about. Research the writer has
    /// linked to a character or place surfaces on that entity's Wiki article, so
    /// it is visible at the moment of writing rather than only in the Research view.</summary>
    [JsonPropertyName("entityRefs")]
    public List<string> EntityRefs { get; set; } = [];

    [JsonPropertyName("createdAt")]
    public System.DateTime CreatedAt { get; set; } = System.DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public System.DateTime UpdatedAt { get; set; } = System.DateTime.UtcNow;

    [JsonPropertyName("order")]
    public int Order { get; set; }

    /// <summary>Values for the writer's own fields, keyed by property key.</summary>
    [JsonPropertyName("properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Properties { get; set; }
}
