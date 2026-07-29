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

    /// <summary>Tags the writer put on the scene.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Threads the scene belongs to.</summary>
    public IReadOnlyList<string> PlotlineIds { get; init; } = [];

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
