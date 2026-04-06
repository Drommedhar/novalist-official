using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

public class TimelineData
{
    [JsonPropertyName("manualEvents")]
    public List<TimelineManualEvent> ManualEvents { get; set; } = [];

    [JsonPropertyName("categories")]
    public List<TimelineCategory> Categories { get; set; } =
    [
        new() { Id = "plot", Name = "Plot Point", Color = "#e74c3c" },
        new() { Id = "character", Name = "Character Event", Color = "#3498db" },
        new() { Id = "world", Name = "World Event", Color = "#2ecc71" },
    ];

    [JsonPropertyName("viewMode")]
    public string ViewMode { get; set; } = "vertical";

    [JsonPropertyName("zoomLevel")]
    public string ZoomLevel { get; set; } = "month";
}

public class TimelineCategory
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("color")]
    public string Color { get; set; } = string.Empty;
}

public class TimelineManualEvent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("categoryId")]
    public string CategoryId { get; set; } = "plot";

    [JsonPropertyName("linkedChapterGuid")]
    public string LinkedChapterGuid { get; set; } = string.Empty;

    [JsonPropertyName("linkedSceneId")]
    public string LinkedSceneId { get; set; } = string.Empty;

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("characters")]
    public List<string> Characters { get; set; } = [];

    [JsonPropertyName("locations")]
    public List<string> Locations { get; set; } = [];
}
