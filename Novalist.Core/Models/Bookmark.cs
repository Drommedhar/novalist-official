using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>What a bookmark points at.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BookmarkKind
{
    /// <summary>A scene, optionally a passage inside it.</summary>
    Scene,
    Chapter,

    /// <summary>A Codex entry, by type and id.</summary>
    Entity,
    Research,

    /// <summary>A point on the Timeline, by story date.</summary>
    StoryDate,

    /// <summary>A place on a map, by map id and pin id.</summary>
    MapPin
}

/// <summary>
/// One place in the project worth coming back to.
///
/// Novalist had a per-scene favourite flag and saved lists, which answer "which
/// scenes match this query" - a different question from "the paragraph where she
/// finds out, the entry I keep re-reading, the day the siege starts". Those had
/// nowhere to be recorded at all, so people kept them in a scene called Notes.
/// </summary>
public sealed class Bookmark
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("kind")]
    public BookmarkKind Kind { get; set; } = BookmarkKind.Scene;

    /// <summary>What the writer called it. Never derived - a bookmark's name is
    /// the reason it exists, and a title would only repeat what it points at.</summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// A named set this belongs to, or empty for none. One flat level: folders
    /// of bookmarks are a second binder, and the point of a bookmark is that
    /// making one costs nothing.
    /// </summary>
    [JsonPropertyName("group")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string Group { get; set; } = string.Empty;

    /// <summary>Chapter guid for a scene or chapter bookmark.</summary>
    [JsonPropertyName("chapterGuid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string ChapterGuid { get; set; } = string.Empty;

    /// <summary>Scene id, entity id, research id or map id, by kind.</summary>
    [JsonPropertyName("targetId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string TargetId { get; set; } = string.Empty;

    /// <summary>Entity type key for an entity bookmark; pin id for a map one.</summary>
    [JsonPropertyName("targetType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string TargetType { get; set; } = string.Empty;

    /// <summary>
    /// The passage this marks inside a scene, as the text itself.
    ///
    /// Stored as text rather than as an offset because prose is edited above
    /// the mark constantly, and an offset would silently drift to the middle of
    /// an unrelated sentence. Text that no longer appears simply opens the scene.
    /// </summary>
    [JsonPropertyName("anchorText")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string AnchorText { get; set; } = string.Empty;

    /// <summary>Story date for a timeline bookmark.</summary>
    [JsonPropertyName("storyDate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string StoryDate { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("order")]
    public int Order { get; set; }
}
