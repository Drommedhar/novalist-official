namespace Novalist.Sdk.Services;

public sealed class PlotlineInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Color { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

/// <summary>An act, and which chapters carry its label.</summary>
public sealed class ActInfo
{
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<string> ChapterGuids { get; init; } = [];
}

/// <summary>Manually entered timeline event, excluding events derived from dated scenes.</summary>
public sealed class TimelineEventInfo
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;

    /// <summary>The date as the writer wrote it. Free text - an in-world calendar
    /// is not a Gregorian one.</summary>
    public string Date { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    /// <summary>"plot", "character", "location", "world" or "other".</summary>
    public string CategoryId { get; init; } = "plot";

    /// <summary>The chapter this event belongs beside, or empty.</summary>
    public string LinkedChapterGuid { get; init; } = string.Empty;
}

/// <summary>
/// The parts of a scene beyond its prose, and the story structure around it.
///
/// An extension could read what a scene said and almost nothing about what it
/// was: no point of view, no intensity, no plot thread, no act. That made a
/// whole class of read-only analysis - pacing curves, continuity rules, thread
/// coverage - impossible to write outside core, which is the opposite of what
/// an extension surface is for.
/// </summary>
public interface IExtensionStoryService
{
    /// <summary>
    /// What the active book is about: the premise, the pitch and the
    /// publishing metadata. Null when no book is open.
    ///
    /// The one thing an extension building a prompt could not say was what the
    /// book *is*. It could read every scene, every Codex entry and every plot
    /// thread, and still had to open with "a novel" - so a model was asked to
    /// judge a cosy mystery by the standards of whatever it assumed. The genre,
    /// the audience, the blurb and the one-page synopsis are all things the
    /// writer has already written down; they were simply not reachable.
    ///
    /// Read-only on purpose. The premise is the writer's statement of intent,
    /// and a pass that could quietly rewrite what the book is meant to be would
    /// change the standard everything else is measured against.
    /// </summary>
    BookDetailInfo? GetBookDetail();

    /// <summary>
    /// A scene's metadata. Null when the scene does not exist.
    /// </summary>
    SceneDetailInfo? GetSceneDetail(string chapterGuid, string sceneId);

    /// <summary>
    /// A chapter's metadata. Null when the chapter does not exist.
    ///
    /// ChapterInfo carries a title and an order, which is enough to walk the
    /// book and nothing else: a report could not group by act or place a
    /// chapter in story time, so analysis that reads the
    /// shape of a draft had to live in core.
    /// </summary>
    ChapterDetailInfo? GetChapterDetail(string chapterGuid);

    /// <summary>
    /// Sets a chapter's legacy status: "Outline", "FirstDraft", "Revised", "Edited"
    /// or "Final", matched without regard to case. False when the chapter or
    /// the status is unknown.
    /// </summary>
    /// <remarks>Deprecated for new workflows. Retained with unchanged storage and
    /// read/write semantics for existing extensions and saved queries. The app
    /// tracks writing state on scenes; this method never changes scene stages or
    /// calculates a chapter state. Chapter status is no longer supported as an
    /// app workflow; only existing integrations and legacy queries consume it.</remarks>
    [Obsolete("Chapter status is deprecated and no longer supported as an app workflow. Use scene stages for writing state. Retained only for compatibility; legacy reads and writes still work.", false)]
    Task<bool> SetChapterStatusAsync(string chapterGuid, string status);

    /// <summary>
    /// Changes some of a scene's metadata, leaving the rest alone.
    ///
    /// Every field is nullable and null means "do not touch". A pass that sets
    /// the point of view must not blank the synopsis it said nothing about,
    /// which is what a whole-object save would do.
    /// </summary>
    /// <returns>False when the scene does not exist.</returns>
    Task<bool> SetSceneMetadataAsync(
        string chapterGuid, string sceneId, SceneMetadataPatch patch);

    /// <summary>Acts in reading order, with the chapters that carry each label.</summary>
    IReadOnlyList<ActInfo> GetActs();

    /// <summary>Plot threads defined on the active book.</summary>
    IReadOnlyList<PlotlineInfo> GetPlotlines();

    /// <summary>Creates a plot thread and returns its id.</summary>
    /// <param name="color">A CSS colour. Empty takes the host's default.</param>
    Task<string> CreatePlotlineAsync(string name, string color = "", string description = "");

    /// <summary>
    /// Sets which threads a scene belongs to, replacing what was there.
    /// False when the scene does not exist.
    /// </summary>
    Task<bool> SetScenePlotlinesAsync(
        string chapterGuid, string sceneId, IReadOnlyList<string> plotlineIds);

    /// <summary>
    /// The note on one plot-grid cell - the short "what this scene does for
    /// this thread" line. Empty when there is none.
    ///
    /// The membership tick says a thread is present; the note says what it is
    /// doing there, and it is the half a thread-coverage report actually needs
    /// to say anything useful.
    /// </summary>
    string GetCellNote(string chapterGuid, string sceneId, string plotlineId);

    /// <summary>
    /// Sets a plot-grid cell note, or clears it with empty text. False when the
    /// scene does not exist.
    /// </summary>
    Task<bool> SetCellNoteAsync(
        string chapterGuid, string sceneId, string plotlineId, string note);

    /// <summary>
    /// The writer's saved lists - the standing questions they ask of their own
    /// draft. An extension reporting on a book had no way to respect them, so
    /// it could only ever report on all of it.
    /// </summary>
    IReadOnlyList<SmartListInfo> GetSmartLists();

    /// <summary>
    /// Maps in the active book, with their pins. Read-only: a map is a drawing,
    /// and the drawing surface is the host's.
    /// </summary>
    Task<IReadOnlyList<MapInfo>> GetMapsAsync();

    /// <summary>Hand-entered timeline events, in the order they are stored.</summary>
    IReadOnlyList<TimelineEventInfo> GetTimelineEvents();

    /// <summary>
    /// Creates or updates a timeline event and returns its id. An empty
    /// <see cref="TimelineEventInfo.Id"/> creates a new one.
    /// </summary>
    Task<string> SaveTimelineEventAsync(TimelineEventInfo story);

    /// <summary>Deletes a timeline event. False when the id is unknown.</summary>
    Task<bool> DeleteTimelineEventAsync(string eventId);
}
