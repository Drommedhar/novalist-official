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

    [JsonPropertyName("dateRange")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StoryDateRange? DateRange { get; set; }
}
