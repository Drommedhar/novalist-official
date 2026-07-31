namespace Novalist.Sdk.Services;

/// <summary>A plot thread.</summary>
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

/// <summary>A dated event on the story timeline that the writer entered by hand.</summary>
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
    /// A scene's metadata. Null when the scene does not exist.
    /// </summary>
    SceneDetailInfo? GetSceneDetail(string chapterGuid, string sceneId);

    /// <summary>
    /// A chapter's metadata. Null when the chapter does not exist.
    ///
    /// ChapterInfo carries a title and an order, which is enough to walk the
    /// book and nothing else: a report could not group by act, colour by
    /// status, or place a chapter in story time, so analysis that reads the
    /// shape of a draft had to live in core.
    /// </summary>
    ChapterDetailInfo? GetChapterDetail(string chapterGuid);

    /// <summary>
    /// Sets a chapter's status: "Outline", "FirstDraft", "Revised", "Edited"
    /// or "Final", matched without regard to case. False when the chapter or
    /// the status is unknown - a status nothing can display would leave the
    /// chapter in a state the writer cannot see or change back.
    /// </summary>
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

/// <summary>What a chapter is, beyond the scenes in it.</summary>
public sealed class ChapterDetailInfo
{
    public string Guid { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public int Order { get; init; }

    /// <summary>"Outline", "FirstDraft", "Revised", "Edited" or "Final".</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>The act label, or empty for a chapter in no act.</summary>
    public string Act { get; init; } = string.Empty;

    /// <summary>The in-world date as the writer wrote it. Free text.</summary>
    public string Date { get; init; } = string.Empty;

    /// <summary>Story date range, where the writer set one.</summary>
    public string DateStart { get; init; } = string.Empty;
    public string DateEnd { get; init; } = string.Empty;

    /// <summary>The writer's description of the chapter.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The chapter's own word target, where one is set.</summary>
    public int? WordTarget { get; init; }

    /// <summary>Words across the chapter's scenes.</summary>
    public int WordCount { get; init; }

    /// <summary>Scenes in the chapter, in order.</summary>
    public IReadOnlyList<string> SceneIds { get; init; } = [];

    /// <summary>The writer's own typed fields on this chapter.</summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; }
        = new Dictionary<string, string>();
}

/// <summary>
/// The parts of a scene a caller wants changed. Null leaves a field as it is.
/// </summary>
public sealed class SceneMetadataPatch
{
    public string? Synopsis { get; init; }
    public string? Notes { get; init; }
    public string? Pov { get; init; }
    public string? Emotion { get; init; }
    public string? Conflict { get; init; }
    public int? Intensity { get; init; }
    public string? Stage { get; init; }
    public string? NarrativeMode { get; init; }
    public string? DateStart { get; init; }
    public string? DateEnd { get; init; }
    public bool? Inactive { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>
    /// The writer's own fields. Only the keys given are written; a key with a
    /// null value is removed.
    /// </summary>
    public IReadOnlyDictionary<string, string?>? Properties { get; init; }
}

/// <summary>What a scene is, beyond the words in it.</summary>
public sealed class SceneDetailInfo
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string ChapterGuid { get; init; } = string.Empty;
    public int Order { get; init; }
    public int WordCount { get; init; }

    /// <summary>Whose head the scene is in, as the writer set it or as the host detected it.</summary>
    public string Pov { get; init; } = string.Empty;

    /// <summary>The writer's synopsis.</summary>
    public string Synopsis { get; init; } = string.Empty;

    /// <summary>Free-text notes on the scene.</summary>
    public string Notes { get; init; } = string.Empty;

    /// <summary>Dramatic intensity 0-10 where the host or the writer set one, else null.</summary>
    public int? Intensity { get; init; }

    /// <summary>The dominant emotion, where one is recorded.</summary>
    public string Emotion { get; init; } = string.Empty;

    /// <summary>The central conflict, where one is recorded.</summary>
    public string Conflict { get; init; } = string.Empty;

    /// <summary>The stage in whatever structure the book uses.</summary>
    public string Stage { get; init; } = string.Empty;

    /// <summary>
    /// True when the scene is out of the book but still in the plan.
    ///
    /// An extension could not tell a parked scene from a live one, so every
    /// report counted words the manuscript does not contain and named scenes
    /// the reader will never reach.
    /// </summary>
    public bool Inactive { get; init; }

    /// <summary>Tags the writer put on the scene.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Threads the scene belongs to.</summary>
    public IReadOnlyList<string> PlotlineIds { get; init; } = [];

    /// <summary>
    /// Ids of the Codex entries the writer said are in this scene.
    ///
    /// Novalist has known this since assigned casts shipped and never handed it
    /// to an extension, so a report on who drops out of the book could only read
    /// the point of view - one name per scene, whoever else was standing there.
    /// </summary>
    public IReadOnlyList<string> Cast { get; init; } = [];

    /// <summary>Id of the entry the scene is about, or empty.</summary>
    public string FocusEntityId { get; init; } = string.Empty;

    /// <summary>Story date as written, or empty.</summary>
    public string DateStart { get; init; } = string.Empty;
    public string DateEnd { get; init; } = string.Empty;

    /// <summary>"Flashback", "FlashForward", "Parallel", "Frame", "Dream",
    /// "TimeSkip", or empty for a scene that simply happens next.</summary>
    public string NarrativeMode { get; init; } = string.Empty;

    /// <summary>The act label of the chapter this scene is in.</summary>
    public string Act { get; init; } = string.Empty;

    /// <summary>The writer's own typed fields on this scene.</summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; }
        = new Dictionary<string, string>();
}
