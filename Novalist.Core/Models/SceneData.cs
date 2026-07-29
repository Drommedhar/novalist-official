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

    /// <summary>
    /// Key of the <see cref="SceneStage"/> this scene is at. Empty means the
    /// writer has not set one, which is not the same as being at the first
    /// stage - a scene nobody has triaged should not claim to be outlined.
    /// </summary>

    /// <summary>
    /// Word target for this scene, or null for none. A target set here is the
    /// writer's own intention, never derived - a chapter without one aggregates
    /// its scenes' targets instead, so setting a few scene targets is enough to
    /// see where the chapter stands.
    /// </summary>
    [JsonPropertyName("wordTarget")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? WordTarget { get; set; }

    /// <summary>
    /// Key of the story-structure beat this scene fulfils, or null when it
    /// fulfils none. A beat with no scene bound to it is a hole in the
    /// structure, which is the thing worth being told about.
    /// </summary>
    [JsonPropertyName("beat")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BeatKey { get; set; }

    [JsonPropertyName("stage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Stage { get; set; }

    [JsonPropertyName("labelColor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LabelColor { get; set; }

    /// <summary>Plotline ids this scene contributes to (Plot Grid).</summary>
    [JsonPropertyName("plotlineIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? PlotlineIds { get; set; }

    /// <summary>Optional short note per plotline this scene belongs to, keyed by
    /// plotline id — "what this scene does for that thread". Shown in the Plot
    /// Grid cell. Null when no cell on this scene carries a note.</summary>
    [JsonPropertyName("plotlineNotes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? PlotlineNotes { get; set; }

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

    /// <summary>Speakers the writer assigned by hand in the Dialogue view, keyed
    /// by the line key <c>DialogueScanner</c> derives from the spoken text. The
    /// value is a character id, or an empty string when the writer cleared a
    /// wrong guess without naming a replacement. Null when every line in the
    /// scene is left to automatic attribution.</summary>
    [JsonPropertyName("dialogueSpeakers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? DialogueSpeakers { get; set; }

    [JsonPropertyName("analysisOverrides")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SceneAnalysisOverrides? AnalysisOverrides { get; set; }

    /// <summary>
    /// Values for the book's scene-scoped <see cref="ManuscriptPropertyDefinition"/>s,
    /// keyed by property key. Null when the writer has filled none in.
    /// </summary>
    [JsonPropertyName("properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Properties { get; set; }

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
