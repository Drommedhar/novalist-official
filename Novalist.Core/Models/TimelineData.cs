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

    /// <summary>
    /// The named timelines of this project. Never empty: the first is the one
    /// everything unassigned belongs to.
    ///
    /// One list meant backstory and manuscript chronology shared a stream, so
    /// a war three hundred years before chapter one sat between two scenes of
    /// a Tuesday. Separating them is the whole point.
    /// </summary>
    [JsonPropertyName("timelines")]
    public List<TimelineTrack> Timelines { get; set; } =
    [
        new() { Id = "main", Name = "Main" },
    ];

    /// <summary>
    /// The timeline the view is showing, or empty for all of them at once.
    /// </summary>
    [JsonPropertyName("activeTimelineId")]
    public string ActiveTimelineId { get; set; } = string.Empty;

    [JsonPropertyName("viewMode")]
    public string ViewMode { get; set; } = "vertical";

    [JsonPropertyName("zoomLevel")]
    public string ZoomLevel { get; set; } = "month";
}

/// <summary>
/// One named timeline. A project has at least one and events say which they
/// are on - more than one, where an event belongs to both a character's life
/// and the world's history.
/// </summary>
public class TimelineTrack
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
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

    /// <summary>Optional end of the timeframe — empty for instantaneous events.</summary>
    [JsonPropertyName("endDate")]
    public string EndDate { get; set; } = string.Empty;

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

    /// <summary>
    /// The timelines this event is on. Empty means the first one, which is what
    /// every event written before there was more than one timeline means.
    ///
    /// A list rather than one id: an event can belong to a character's life and
    /// to the world's history at once, and duplicating it into two timelines
    /// would leave two copies to keep in step.
    /// </summary>
    [JsonPropertyName("timelineIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? TimelineIds { get; set; }

    /// <summary>Values for the writer's own fields, keyed by property key.</summary>
    [JsonPropertyName("properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Properties { get; set; }
}
