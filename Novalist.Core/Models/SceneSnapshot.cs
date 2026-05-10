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
}
