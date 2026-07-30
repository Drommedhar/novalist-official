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

    /// <summary>
    /// Where this item stands: <see cref="ResearchStatus.None"/> until the
    /// writer says otherwise.
    ///
    /// A research shelf without one is a pile: nothing separates a question
    /// still open from a question answered three months ago, and the note that
    /// says "check whether the bridge existed in 1755" reads the same after it
    /// has been checked as before.
    /// </summary>
    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ResearchStatus Status { get; set; } = ResearchStatus.None;

    /// <summary>
    /// The writer's own rating, 0 for none and 1-5 otherwise. A shelf of
    /// forty sources has three that matter, and nothing said which.
    /// </summary>
    [JsonPropertyName("rating")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Rating { get; set; }

    /// <summary>
    /// Ids of other research items this one refers to. A source that answers
    /// another's question, a note that expands on one - discoverable in both
    /// directions, because the item that got answered is the one being read.
    /// </summary>
    [JsonPropertyName("relatedIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? RelatedIds { get; set; }
}

/// <summary>
/// Where a research item stands.
///
/// Deliberately short. A four-state lifecycle is a project-management tool; what
/// a writer needs is to be able to see, at a glance, which questions are still
/// open and which sources they have actually read.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResearchStatus
{
    /// <summary>Nothing said. Every item starts here and most stay here.</summary>
    None,

    /// <summary>A question the book needs answered.</summary>
    Open,

    /// <summary>Being worked on.</summary>
    InProgress,

    /// <summary>Answered, read, done with.</summary>
    Resolved
}
