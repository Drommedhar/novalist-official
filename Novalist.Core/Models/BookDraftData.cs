using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// Lightweight metadata about a draft, stored on <see cref="BookData.Drafts"/>.
/// The draft's chapters / acts live in a per-draft <c>draft.json</c> file on disk
/// under <c>Books/&lt;book&gt;/Drafts/&lt;folderName&gt;/</c>.
/// </summary>
public sealed class BookDraftMetadata
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Draft 1";

    [JsonPropertyName("folderName")]
    public string FolderName { get; set; } = "default";

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("parentDraftId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentDraftId { get; set; }

    [JsonPropertyName("notes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Notes { get; set; }
}

/// <summary>
/// Per-draft on-disk content: chapter + act tree. Stored at
/// <c>Books/&lt;book&gt;/Drafts/&lt;folderName&gt;/draft.json</c>.
/// </summary>
public sealed class BookDraftData
{
    [JsonPropertyName("chapters")]
    public List<ChapterData> Chapters { get; set; } = new();

    [JsonPropertyName("acts")]
    public List<ActData> Acts { get; set; } = new();
}
