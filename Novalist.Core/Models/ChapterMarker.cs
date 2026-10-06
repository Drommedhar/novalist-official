using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// Hidden per-chapter marker file (<c>.nvchapter.json</c>) written into each chapter
/// folder. Holds the chapter's durable identity + metadata so the folder can be
/// renamed in a file manager without losing the chapter. The folder name becomes a
/// cosmetic display hint; this marker is the source of truth.
/// </summary>
public sealed class ChapterMarker
{
    [JsonPropertyName("guid")]
    public string Guid { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("act")]
    public string Act { get; set; } = string.Empty;

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("status")]
    public ChapterStatus Status { get; set; } = ChapterStatus.Outline;

    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("dateRange")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StoryDateRange? DateRange { get; set; }

    public const string FileName = ".nvchapter.json";

    public static ChapterMarker FromChapter(ChapterData chapter) => new()
    {
        Guid = chapter.Guid,
        Title = chapter.Title,
        Act = chapter.Act,
        Order = chapter.Order,
        Status = chapter.Status,
        Date = chapter.Date,
        DateRange = chapter.DateRange,
    };

    /// <summary>
    /// Builds a <see cref="ChapterData"/> from this marker. <paramref name="folderName"/>
    /// is the on-disk folder the marker was read from (the marker doesn't store it).
    /// </summary>
    public ChapterData ToChapter(string folderName) => new()
    {
        Guid = Guid,
        Title = Title,
        Act = Act,
        Order = Order,
        Status = Status,
        Date = Date,
        DateRange = DateRange,
        FolderName = folderName,
    };
}
