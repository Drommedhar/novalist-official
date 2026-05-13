using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

public class SceneData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = System.Guid.NewGuid().ToString();

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("chapterGuid")]
    public string ChapterGuid { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("isFavorite")]
    public bool IsFavorite { get; set; }

    /// <summary>Optional in-world date range. When present takes precedence
    /// over <see cref="Date"/>.</summary>
    [JsonPropertyName("dateRange")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StoryDateRange? DateRange { get; set; }

    [JsonPropertyName("wordCount")]
    public int WordCount { get; set; }

    [JsonPropertyName("notes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Notes { get; set; }

    [JsonPropertyName("synopsis")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Synopsis { get; set; }

    [JsonPropertyName("labelColor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LabelColor { get; set; }

    /// <summary>Plotline ids this scene contributes to (Plot Grid).</summary>
    [JsonPropertyName("plotlineIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? PlotlineIds { get; set; }

    /// <summary>Inline comments anchored to text ranges in the scene HTML.</summary>
    [JsonPropertyName("comments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SceneComment>? Comments { get; set; }

    /// <summary>Footnotes / endnotes in this scene, referenced by
    /// <c>&lt;sup class="nv-fn" data-fn-id="..."&gt;n&lt;/sup&gt;</c> anchors
    /// in the scene HTML.</summary>
    [JsonPropertyName("footnotes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SceneFootnote>? Footnotes { get; set; }

    [JsonPropertyName("analysisOverrides")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SceneAnalysisOverrides? AnalysisOverrides { get; set; }

    /// <summary>UTC timestamp when the scene was moved to the archive. Null = active.</summary>
    [JsonPropertyName("archivedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public System.DateTime? ArchivedAt { get; set; }

    /// <summary>The chapter this scene came from before archiving. Used as default
    /// restore target. Null on non-archived scenes.</summary>
    [JsonPropertyName("originChapterGuid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OriginChapterGuid { get; set; }
}

public class SceneFootnote
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = System.Guid.NewGuid().ToString();

    /// <summary>1-based ordinal number rendered in the superscript anchor.</summary>
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

public class SceneComment
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = System.Guid.NewGuid().ToString();

    /// <summary>The text snippet the comment was originally anchored to —
    /// shown in the comment list.</summary>
    [JsonPropertyName("anchorText")]
    public string AnchorText { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public System.DateTime CreatedAt { get; set; } = System.DateTime.UtcNow;

    [JsonPropertyName("resolved")]
    public bool Resolved { get; set; }
}

public class SceneAnalysisOverrides
{
    [JsonPropertyName("pov")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Pov { get; set; }

    [JsonPropertyName("emotion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Emotion { get; set; }

    [JsonPropertyName("intensity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Intensity { get; set; }

    [JsonPropertyName("conflict")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Conflict { get; set; }

    [JsonPropertyName("tags")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Tags { get; set; }

    [JsonIgnore]
    public bool HasValues
        => Pov != null
           || Emotion != null
           || Intensity.HasValue
           || Conflict != null
           || Tags != null;

    public SceneAnalysisOverrides Clone()
        => new()
        {
            Pov = Pov,
            Emotion = Emotion,
            Intensity = Intensity,
            Conflict = Conflict,
            Tags = Tags != null ? [.. Tags] : null
        };
}

/// <summary>
/// Maps chapter GUIDs to their ordered scene lists.
/// Stored in .novalist/scenes.json.
/// </summary>
public class ScenesManifest
{
    [JsonPropertyName("chapters")]
    public Dictionary<string, List<SceneData>> Chapters { get; set; } = new();

    /// <summary>Archived scenes — out of manuscript, restorable.</summary>
    [JsonPropertyName("archived")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<SceneData> Archived { get; set; } = [];
}
