using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// A point-in-time copy of a scene's content. Stored under
/// &lt;bookRoot&gt;/&lt;SnapshotFolder&gt;/&lt;sceneId&gt;/&lt;timestamp&gt;.json.
/// </summary>
public class SceneSnapshot
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = System.Guid.NewGuid().ToString();

    [JsonPropertyName("sceneId")]
    public string SceneId { get; set; } = string.Empty;

    [JsonPropertyName("chapterGuid")]
    public string ChapterGuid { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("wordCount")]
    public int WordCount { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// The scene around the prose, as it stood. Restoring only the words put
    /// the writer back at a version whose synopsis, notes and dates described
    /// a different draft. Every field is nullable and every one is absent from
    /// a snapshot taken before this existed, which is what tells a restore to
    /// leave that field alone rather than blanking it.
    /// </summary>
    [JsonPropertyName("meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SceneSnapshotMeta? Meta { get; set; }
}

/// <summary>The scene's own fields at the moment a snapshot was taken.</summary>
public class SceneSnapshotMeta
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("synopsis")]
    public string? Synopsis { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("pov")]
    public string? Pov { get; set; }

    [JsonPropertyName("stage")]
    public string? Stage { get; set; }

    [JsonPropertyName("labelKey")]
    public string? LabelKey { get; set; }

    [JsonPropertyName("storyDate")]
    public string? StoryDate { get; set; }

    [JsonPropertyName("plotlineIds")]
    public List<string>? PlotlineIds { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }
}
