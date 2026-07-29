using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// Optional metadata for an act. Acts are referenced by name from
/// <see cref="ChapterData.Act"/>; this entry stores act-level metadata (date
/// range, etc.).
/// </summary>
public class ActData
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;


    /// <summary>
    /// Word target for this act, or null for none. A target set here is the
    /// writer's own intention, never derived - a chapter without one aggregates
    /// its scenes' targets instead, so setting a few scene targets is enough to
    /// see where the chapter stands.
    /// </summary>
    [JsonPropertyName("wordTarget")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? WordTarget { get; set; }

    [JsonPropertyName("dateRange")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StoryDateRange? DateRange { get; set; }
}
