using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

public class ActivityItem
{
    [JsonPropertyName("type")]
    public ActivityType Type { get; set; } = ActivityType.Edit;

    [JsonPropertyName("chapterGuid")]
    public string ChapterGuid { get; set; } = string.Empty;

    [JsonPropertyName("chapterTitle")]
    public string ChapterTitle { get; set; } = string.Empty;

    [JsonPropertyName("sceneId")]
    public string SceneId { get; set; } = string.Empty;

    [JsonPropertyName("sceneTitle")]
    public string SceneTitle { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public enum ActivityType
{
    Edit,
    Create,
    Delete
}
