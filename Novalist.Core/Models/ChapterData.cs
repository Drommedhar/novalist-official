using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

public class ChapterData
{
    [JsonPropertyName("guid")]
    public string Guid { get; set; } = System.Guid.NewGuid().ToString();

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("status")]
    public ChapterStatus Status { get; set; } = ChapterStatus.Outline;

    [JsonPropertyName("act")]
    public string Act { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("isFavorite")]
    public bool IsFavorite { get; set; }

    /// <summary>Optional in-world date range. When present takes precedence
    /// over <see cref="Date"/>.</summary>
    [JsonPropertyName("dateRange")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StoryDateRange? DateRange { get; set; }

    /// <summary>
    /// The folder name on disk for this chapter (e.g. "01 - First Chapter").
    /// </summary>

    /// <summary>
    /// Word target for this chapter, or null for none. A target set here is the
    /// writer's own intention, never derived - a chapter without one aggregates
    /// its scenes' targets instead, so setting a few scene targets is enough to
    /// see where the chapter stands.
    /// </summary>
    [JsonPropertyName("wordTarget")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? WordTarget { get; set; }

    [JsonPropertyName("folderName")]
    public string FolderName { get; set; } = string.Empty;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChapterStatus
{
    Outline,
    FirstDraft,
    Revised,
    Edited,
    Final
}
