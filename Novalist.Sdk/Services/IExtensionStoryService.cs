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

/// <summary>One condition in a saved list.</summary>
public sealed class SmartListRuleInfo
{
    /// <summary>The scene or chapter attribute the rule tests.</summary>
    public string Field { get; init; } = string.Empty;

    /// <summary>"Is", "Contains", "GreaterThan", "LessThan", "IsSet" or "IsNotSet".</summary>
    public string Op { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;
}

/// <summary>A saved scene query the writer built.</summary>
public sealed class SmartListInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    /// <summary>"All" when every rule must hold, "Any" when one is enough.</summary>
    public string Match { get; init; } = string.Empty;

    public IReadOnlyList<SmartListRuleInfo> Rules { get; init; } = [];
}

/// <summary>
/// The book in one line, then one paragraph, then the answers a submission
/// form asks for.
/// </summary>
public sealed class BookPremiseInfo
{
    /// <summary>One sentence: somebody wants something, and something stops them.</summary>
    public string Logline { get; init; } = string.Empty;

    /// <summary>The premise opened out: world, stakes, inciting incident, rough climax.</summary>
    public string Paragraph { get; init; } = string.Empty;

    /// <summary>
    /// A summary per act, keyed by the act name the chapters carry - so the
    /// ladder stays attached to the book's own structure rather than assuming
    /// three acts.
    /// </summary>
    public IReadOnlyDictionary<string, string> Acts { get; init; }
        = new Dictionary<string, string>();

    /// <summary>Genre as a shop would file it.</summary>
    public string Genre { get; init; } = string.Empty;

    /// <summary>Who it is for: age band, readership, the shelf it sits on.</summary>
    public string Audience { get; init; } = string.Empty;

    /// <summary>The two or three comparable titles an agent asks for.</summary>
    public string Comparables { get; init; } = string.Empty;

    /// <summary>Where and when it is set.</summary>
    public string Setting { get; init; } = string.Empty;

    /// <summary>
    /// Back-cover copy: what a reader is told to make them open it. Not the
    /// synopsis - a blurb withholds the ending on purpose, and a prompt that
    /// confuses the two asks for the wrong thing.
    /// </summary>
    public string Blurb { get; init; } = string.Empty;

    /// <summary>The one-page synopsis, ending included.</summary>
    public string Synopsis { get; init; } = string.Empty;
}

/// <summary>
/// What a shop, a library and a distributor are told about the book.
/// </summary>
public sealed class BookPublishingInfo
{
    /// <summary>ISBN as the writer typed it, hyphens and all.</summary>
    public string Isbn { get; init; } = string.Empty;

    public string Publisher { get; init; } = string.Empty;

    /// <summary>The description written for the metadata block.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Subject headings: genre words, or BISAC codes where the writer has them.</summary>
    public IReadOnlyList<string> Subjects { get; init; } = [];

    /// <summary>The copyright line.</summary>
    public string Rights { get; init; } = string.Empty;

    /// <summary>Publication date as the writer entered it, ideally yyyy-mm-dd.</summary>
    public string PublicationDate { get; init; } = string.Empty;

    /// <summary>The series this book belongs to, or empty.</summary>
    public string SeriesName { get; init; } = string.Empty;

    /// <summary>Position in the series - "2", or "2.5" for a novella between
    /// two books, which is why it is not a number.</summary>
    public string SeriesPosition { get; init; } = string.Empty;
}

/// <summary>What the active book is, beyond the chapters in it.</summary>
public sealed class BookDetailInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Who wrote this book when that is not who wrote the project - an
    /// anthology whose volumes have different authors. Empty means the
    /// project's own author.
    /// </summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>
    /// "first", "second", "third limited", "third omniscient", or empty where
    /// the writer has not said. Declared rather than derived: it is the
    /// writer's intention, and reading it off the majority of scenes would make
    /// the outlier normal.
    /// </summary>
    public string NarrativePerson { get; init; } = string.Empty;

    /// <summary>"past", "present", or empty. Same reasoning as
    /// <see cref="NarrativePerson"/>.</summary>
    public string Tense { get; init; } = string.Empty;

    /// <summary>The story structure the book is written against, or empty.</summary>
    public string StructureTemplateId { get; init; } = string.Empty;

    /// <summary>Never null: a book with nothing filled in has empty fields
    /// rather than a missing object, so a caller reads it without a null check
    /// on every line.</summary>
    public BookPremiseInfo Premise { get; init; } = new();

    /// <summary>Never null, for the same reason as <see cref="Premise"/>.</summary>
    public BookPublishingInfo Publishing { get; init; } = new();
}

/// <summary>A pin on a map.</summary>
public sealed class MapPinInfo
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public double X { get; init; }
    public double Y { get; init; }

    /// <summary>The Codex entry this pin stands for, or empty.</summary>
    public string EntityId { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;

    /// <summary>Another map this pin opens, or empty.</summary>
    public string TargetMapId { get; init; } = string.Empty;
}

/// <summary>A map of the world, with what is marked on it.</summary>
public sealed class MapInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<MapPinInfo> Pins { get; init; } = [];
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
