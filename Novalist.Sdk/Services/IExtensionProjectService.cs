using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;

namespace Novalist.Sdk.Services;

/// <summary>
/// Read-only project information exposed to extensions.
/// </summary>
public interface IExtensionProjectService
{
    string? ProjectRoot { get; }
    string? ActiveBookRoot { get; }
    string? WorldBibleRoot { get; }
    bool IsProjectLoaded { get; }

    /// <summary>Read scene content.</summary>
    Task<string> ReadSceneContentAsync(string chapterGuid, string sceneId);

    /// <summary>Reads the stored scene synopsis (one- or two-line summary).</summary>
    Task<string> GetSceneSynopsisAsync(string chapterGuid, string sceneId);

    /// <summary>Updates the scene synopsis and persists the scenes manifest.</summary>
    Task SetSceneSynopsisAsync(string chapterGuid, string sceneId, string synopsis);

    /// <summary>
    /// Creates a chapter at the end of the active book and returns its guid.
    ///
    /// This and the two below are what a format importer needs: a .scriv or
    /// .fdx reader is ideal third-party territory, but until now an extension
    /// could read a project and not build one, so every importer had to be
    /// written into core.
    /// </summary>
    Task<string> CreateChapterAsync(string title);

    /// <summary>Creates a scene at the end of a chapter and returns its id.
    /// Empty when the chapter does not exist.</summary>
    Task<string> CreateSceneAsync(string chapterGuid, string title);

    /// <summary>
    /// Replaces a scene's content.
    ///
    /// The one call in this interface that overwrites prose the writer may have
    /// authored. It refuses, by throwing, when that scene is open in the editor
    /// with unsaved changes: without the refusal an extension pass and the
    /// editor's autosave write over each other, whichever lands second wins,
    /// and somebody's work is gone with no error anywhere.
    ///
    /// Call <see cref="IsSceneBusyAsync"/> first if you would rather skip a
    /// scene than fail a pass over the whole book.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The scene is open with unsaved changes.
    /// </exception>
    Task WriteSceneContentAsync(string chapterGuid, string sceneId, string html);

    /// <summary>
    /// True when a scene is open in the editor with unsaved changes, and so
    /// cannot be written to.
    ///
    /// A pass over the manuscript should ask before each scene and skip the
    /// ones that are busy, rather than stopping on the one the writer happens
    /// to be in.
    /// </summary>
    Task<bool> IsSceneBusyAsync(string chapterGuid, string sceneId);

    /// <summary>
    /// Renames a chapter. False when the guid is unknown.
    ///
    /// This and the calls below finish what CreateChapterAsync started: an
    /// importer that can add a chapter and not title it, or add scenes and not
    /// order them, produces a project the writer has to repair by hand.
    /// </summary>
    Task<bool> RenameChapterAsync(string chapterGuid, string title);

    /// <summary>Renames a scene. False when it does not exist.</summary>
    Task<bool> RenameSceneAsync(string chapterGuid, string sceneId, string title);

    /// <summary>
    /// Moves a scene to a position in a chapter, its own or another. An index
    /// past the end lands it at the end. False when either does not exist.
    /// </summary>
    Task<bool> MoveSceneAsync(string sceneId, string targetChapterGuid, int index);

    /// <summary>
    /// Moves a chapter to a one-based position in the book. False when the guid
    /// is unknown.
    /// </summary>
    Task<bool> MoveChapterAsync(string chapterGuid, int order);

    /// <summary>
    /// Sets a chapter's act label, which is how acts are made - there is no
    /// separate act to create. An empty label takes the chapter out of any act.
    /// </summary>
    Task<bool> SetChapterActAsync(string chapterGuid, string act);

    /// <summary>
    /// Sends a chapter and its scenes to the trash, where the writer can get
    /// them back. There is no call here that erases anything: an extension
    /// should not be able to destroy a chapter, only to put it aside.
    /// </summary>
    Task<bool> TrashChapterAsync(string chapterGuid);

    /// <summary>
    /// Archives a scene, which is the recoverable half of deleting one.
    /// </summary>
    Task<bool> ArchiveSceneAsync(string chapterGuid, string sceneId);

    // ── Books and drafts ──

    /// <summary>
    /// Creates a project on disk and returns its folder. The writer's open
    /// project is not touched.
    ///
    /// It is deliberately not opened. An importer building a binder should not
    /// be able to move somebody out of the book they are in the middle of a
    /// sentence of - tell them where it is, or ask them to open it, and let
    /// them decide when.
    /// </summary>
    /// <returns>The new project's folder, or null when it could not be made.</returns>
    Task<string?> CreateProjectAsync(string parentDirectory, string projectName, string firstBookName);

    /// <summary>Every book in the project, in the order the binder shows them.</summary>
    IReadOnlyList<BookInfo> GetBooks();

    /// <summary>Implicit book scope for chapter and scene operations; null when no book is active.</summary>
    string? ActiveBookId { get; }

    /// <summary>
    /// Adds a book to the project and returns its id. The new book is not
    /// switched to: an extension that adds a volume should not move the writer
    /// out of the one they are in.
    /// </summary>
    Task<string> CreateBookAsync(string name);

    /// <summary>Renames a book. False when the id is unknown.</summary>
    Task<bool> RenameBookAsync(string bookId, string name);

    /// <summary>
    /// Makes a book the active one, so the chapter and scene calls above act on
    /// it. Refused while the editor holds unsaved changes - switching out from
    /// under an unsaved scene is how the writer's words go missing.
    /// </summary>
    /// <returns>False when the id is unknown or the editor is busy.</returns>
    Task<bool> SwitchBookAsync(string bookId);

    /// <summary>The active book's drafts, oldest first.</summary>
    IReadOnlyList<DraftInfo> GetDrafts();

    /// <summary>Implicit draft scope for manuscript operations; null when no draft is active.</summary>
    string? ActiveDraftId { get; }

    /// <summary>
    /// Adds a draft to the active book and returns its id. Give
    /// <paramref name="cloneFromDraftId"/> to start it as a copy of an existing
    /// draft rather than empty - which is what a revision pass wants, so the
    /// writer keeps the version it started from.
    /// </summary>
    Task<string> CreateDraftAsync(string name, string? cloneFromDraftId = null);

    /// <summary>Renames a draft of the active book. False when unknown.</summary>
    Task<bool> RenameDraftAsync(string draftId, string name);

    /// <summary>
    /// Makes a draft the active one. Refused while the editor holds unsaved
    /// changes, for the same reason as switching books.
    /// </summary>
    Task<bool> SwitchDraftAsync(string draftId);

    IReadOnlyList<ChapterInfo> GetChaptersOrdered();

    IReadOnlyList<SceneInfo> GetScenesForChapter(string chapterGuid);

    /// <summary>The scene currently open in the editor, or null when none.
    /// Updated by the host before the <see cref="IHostServices.SceneOpened"/>
    /// event fires.</summary>
    SceneInfo? CurrentScene { get; }
}

/// <summary>Lightweight project info for events.</summary>
public sealed class ProjectInfo
{
    public string Name { get; init; } = string.Empty;
    public string RootPath { get; init; } = string.Empty;
}

/// <summary>Lightweight book info for events.</summary>
public sealed class BookInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}

/// <summary>One draft of a book.</summary>
public sealed class DraftInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}

/// <summary>Lightweight chapter info for read-only access.</summary>
public sealed class ChapterInfo
{
    public string Guid { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public int Order { get; init; }
    public string Date { get; init; } = string.Empty;
}

/// <summary>Lightweight scene info for events and read-only access.</summary>
public sealed class SceneInfo
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string ChapterGuid { get; init; } = string.Empty;
    public string ChapterTitle { get; init; } = string.Empty;
    public int WordCount { get; init; }
}
