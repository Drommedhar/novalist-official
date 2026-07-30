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

    /// <summary>
    /// When this scene happens relative to the one before it, for a scene with
    /// no date of its own. Null means no relative statement.
    ///
    /// Papyrus lets an element be undefined, relative or absolute; Novalist had
    /// only absolute, so a writer who knows a scene is "the next morning" and
    /// not which morning left the date blank - and the scene fell out of the
    /// Calendar and the Timeline entirely.
    /// </summary>
    [JsonPropertyName("relativeTime")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RelativeStoryTime? RelativeTime { get; set; }

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
    /// What the viewpoint character is trying to get in this scene.
    ///
    /// The other two thirds of the classic diagnostic live elsewhere already:
    /// conflict is read out of the prose (and overridable), and the outcome is
    /// below. Goal and outcome are never inferred, because what somebody wanted
    /// and whether they got it are the two things a draft most often fails to
    /// answer and no keyword list can guess.
    /// </summary>
    [JsonPropertyName("goal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Goal { get; set; }

    /// <summary>
    /// How the scene leaves them: what changed, what it cost, what it opened.
    /// A scene whose outcome is the same as its goal is a scene where nothing
    /// happened, which is exactly what writing both down makes visible.
    /// </summary>
    [JsonPropertyName("outcome")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Outcome { get; set; }

    /// <summary>
    /// Out of the book, still in the plan.
    ///
    /// Archiving is the only thing Novalist had between keeping a scene and
    /// deleting it, and an archived scene disappears from every planning view -
    /// so a scene held back while the writer decides had to be either fully in
    /// the manuscript or invisible. An inactive scene stays in the binder, the
    /// corkboard, the outliner and the Plot Grid, and leaves the reading order,
    /// the word totals and every export.
    /// </summary>
    [JsonPropertyName("inactive")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Inactive { get; set; }

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

    /// <summary>
    /// Key of the <see cref="SceneLabel"/> this scene carries, or null for
    /// none. Replaces the bare colour below, which nothing ever read.
    /// </summary>
    [JsonPropertyName("label")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LabelKey { get; set; }

    /// <summary>
    /// Holds this scene back from every export. Stored inverted - true means
    /// excluded - so a project written before this existed carries no new keys
    /// and every scene in it is exported, which is what it already did.
    /// </summary>
    [JsonPropertyName("excludeFromExport")]
    public bool ExcludeFromExport { get; set; }

    /// <summary>
    /// A raw hex colour stored before labels had names. Read when present so a
    /// project saved by an older build keeps its colours; nothing writes it.
    /// </summary>
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
    /// How the scene sits in time relative to the story around it:
    /// <c>flashback</c>, <c>flashforward</c>, <c>parallel</c>, <c>frame</c>,
    /// <c>dream</c> or <c>timeskip</c>. Null for a scene that simply happens
    /// next, which is most of them.
    /// </summary>
    [JsonPropertyName("narrativeMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NarrativeMode { get; set; }

    /// <summary>
    /// The named thread a parallel scene belongs to, so two strands running at
    /// once can be told apart rather than being one undifferentiated pile.
    /// </summary>
    [JsonPropertyName("strand")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Strand { get; set; }

    /// <summary>
    /// Things this scene promises the reader, each with the scene that pays it
    /// off once one exists. Null when the scene promises nothing.
    /// </summary>
    [JsonPropertyName("promises")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ScenePromise>? Promises { get; set; }

    /// <summary>
    /// Entity ids the writer said are in this scene, whether or not the prose
    /// names them. Mentions in the text are author-confirmed but incomplete:
    /// a character who is present and silent leaves no span to find.
    /// </summary>
    [JsonPropertyName("cast")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Cast { get; set; }

    /// <summary>
    /// What this scene points at: another scene, a research item, a Codex
    /// entry. Null when it points at nothing.
    ///
    /// Scenes had no link model, so a scene that answers another scene could
    /// only say so as prose in its own notes - which nothing could follow, and
    /// which the scene at the other end never knew about.
    /// </summary>
    [JsonPropertyName("links")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SceneLink>? Links { get; set; }

    /// <summary>
    /// The entity this scene is really about, when that is not the same as who
    /// speaks or whose head it is in. Null when unremarkable.
    /// </summary>
    [JsonPropertyName("focusEntityId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FocusEntityId { get; set; }

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

    /// <summary>
    /// Which slot in that chapter the scene occupied, zero-based. Knowing the
    /// chapter and not the position put every restored scene at the end, so a
    /// scene archived from between two others came back somewhere else - which
    /// is the thing an archive is supposed not to do.
    /// </summary>
    [JsonPropertyName("originIndex")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? OriginIndex { get; set; }

    /// <summary>
    /// Where the writer put this card on the freeform corkboard, in pixels from
    /// the top left of the board. Null means it has never been placed, and the
    /// board falls back to laying it out in reading order - so switching to
    /// freeform shows the book as it stands rather than a pile at the origin.
    /// </summary>
    [JsonPropertyName("boardX")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BoardX { get; set; }

    [JsonPropertyName("boardY")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BoardY { get; set; }
}

/// <summary>
/// Something a scene sets up, and the scene that answers it.
///
/// Held on the setup rather than the payoff so that a promise nothing answers
/// still exists to be reported - which is the whole point of tracking them.
/// </summary>
public class ScenePromise
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = System.Guid.NewGuid().ToString();

    /// <summary>What was promised, in the writer's words: "the gun on the mantel".</summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>The scene that pays it off, or null while nothing does.</summary>
    [JsonPropertyName("payoffSceneId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PayoffSceneId { get; set; }
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

    /// <summary>
    /// Who left it. Stamped from the project's author when a comment arrives
    /// without one, so a file that came back from an editor keeps saying whose
    /// note is whose.
    /// </summary>
    [JsonPropertyName("author")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Author { get; set; }

    /// <summary>
    /// Whether this is a job rather than a remark - "check the timetable",
    /// "this paragraph needs cutting". Both live in the same inbox; only one
    /// of them is a task.
    /// </summary>
    [JsonPropertyName("isTodo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsTodo { get; set; }

    /// <summary>Answers to the comment, oldest first. Null when nobody replied.</summary>
    [JsonPropertyName("replies")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<CommentReply>? Replies { get; set; }
}

/// <summary>One answer in a comment thread.</summary>
public class CommentReply
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = System.Guid.NewGuid().ToString();

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public System.DateTime CreatedAt { get; set; } = System.DateTime.UtcNow;
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
