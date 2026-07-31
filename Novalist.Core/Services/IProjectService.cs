using Novalist.Core.Models;

namespace Novalist.Core.Services;

public interface IProjectService
{
    ProjectMetadata? CurrentProject { get; }
    ProjectSettings ProjectSettings { get; }
    BookData? ActiveBook { get; }
    ScenesManifest? ScenesManifest { get; }
    string? ProjectRoot { get; }
    string? ActiveBookRoot { get; }
    string? ActiveDraftRoot { get; }
    string? WorldBibleRoot { get; }
    bool IsProjectLoaded { get; }

    /// <summary>Optional sink for v2 to v3 filesystem-migration progress (set before load).</summary>
    IProgress<FilesystemMigrationProgress>? MigrationProgress { get; set; }

    /// <summary>Raised when reconciliation (load-time or live) detects external draft changes.</summary>
    event EventHandler<ReconciliationReport>? DraftReconciled;

    Task<ProjectMetadata> CreateProjectAsync(string parentDirectory, string projectName, string firstBookName);
    Task<ProjectMetadata> LoadProjectAsync(string projectDirectory);
    Task SaveProjectAsync();
    Task SaveProjectSettingsAsync();
    Task SaveScenesAsync();

    /// <summary>
    /// Reconciles the active draft with external filesystem edits. When <paramref name="apply"/>
    /// is true and changes are found, the in-memory model is updated and persisted. Returns the
    /// detected changes for surfacing to the user.
    /// </summary>
    Task<ReconciliationReport> ReconcileActiveDraftAsync(bool apply = true);

    // Project management
    Task RenameProjectAsync(string newName);

    // Book management
    Task<BookData> CreateBookAsync(string bookName);
    Task SwitchBookAsync(string bookId);
    Task RenameBookAsync(string bookId, string newName);
    Task DeleteBookAsync(string bookId);

    // Draft management
    Task<BookDraftMetadata> CreateDraftAsync(string draftName, string? cloneFromDraftId = null);
    Task SwitchDraftAsync(string draftId);
    Task RenameDraftAsync(string draftId, string newName);
    Task DeleteDraftAsync(string draftId);

    // World Bible
    Task InitializeWorldBibleAsync();

    /// <param name="insertAtOrder">Where the chapter goes, one-based. Null
    /// appends, which is what creating one has always done.</param>
    Task<ChapterData> CreateChapterAsync(string title, string date = "", int? insertAtOrder = null);
    Task<SceneData> CreateSceneAsync(
        string chapterGuid, string sceneTitle, string date = "", SceneTemplate? template = null);

    /// <summary>Captures an existing scene as a template for new ones.</summary>
    Task<SceneTemplate> SaveSceneAsTemplateAsync(string chapterGuid, string sceneId, string name);
    Task DeleteChapterAsync(string chapterGuid);
    Task DeleteSceneAsync(string chapterGuid, string sceneId);
    Task SetChapterDateAsync(string chapterGuid, string date);
    Task SetSceneDateAsync(string chapterGuid, string sceneId, string date);
    Task SetChapterFavoriteAsync(string chapterGuid, bool favorite);
    Task SetSceneFavoriteAsync(string chapterGuid, string sceneId, bool favorite);
    Task SetSceneLabelColorAsync(string chapterGuid, string sceneId, string? labelColor);
    Task SetChapterDateRangeAsync(string chapterGuid, StoryDateRange? dateRange);
    Task SetSceneDateRangeAsync(string chapterGuid, string sceneId, StoryDateRange? dateRange);
    Task SetSceneAnalysisOverridesAsync(string chapterGuid, string sceneId, SceneAnalysisOverrides? overrides);
    Task ReorderChapterAsync(string chapterGuid, int newOrder);
    Task ReorderSceneAsync(string chapterGuid, string sceneId, int newOrder);
    Task MoveChaptersAsync(IReadOnlyList<string> chapterGuids, int targetIndex);
    Task MoveScenesAsync(IReadOnlyList<string> sceneIds, string targetChapterGuid, int targetIndex);
    Task RenameChapterAsync(string chapterGuid, string newTitle);
    Task RenameSceneAsync(string chapterGuid, string sceneId, string newTitle);

    // Scene archive
    Task ArchiveSceneAsync(string chapterGuid, string sceneId);
    Task RestoreArchivedSceneAsync(string sceneId, string targetChapterGuid, int? targetIndex);
    Task DeleteArchivedSceneAsync(string sceneId);
    IReadOnlyList<SceneData> GetArchivedScenes();
    string GetArchivedSceneFilePath(SceneData scene);
    Task<string> ReadArchivedSceneContentAsync(SceneData scene);

    // Chapter trash. Deleting a chapter moves it and its scenes here rather
    // than erasing them; only PurgeChapterAsync destroys anything.
    IReadOnlyList<ChapterData> GetTrashedChapters();
    Task<bool> RestoreChapterAsync(string chapterGuid);
    Task<bool> PurgeChapterAsync(string chapterGuid);

    string GetChapterFolderPath(ChapterData chapter);
    string GetSceneFilePath(ChapterData chapter, SceneData scene);
    Task<string> ReadSceneContentAsync(ChapterData chapter, SceneData scene);
    Task WriteSceneContentAsync(ChapterData chapter, SceneData scene, string content);

    /// <summary>
    /// Rewrites the inner text of every `nv-entity-mention` span whose
    /// `data-entity-id` matches and whose `data-mention-source` is not "manual"
    /// across all scenes in the active book (including archived).
    /// Returns the number of scene files modified.
    /// </summary>
    Task<int> SyncMentionDisplayTextAsync(string entityId, string newDisplayText);

    // Reading a book that is not the open one. Everything else on this
    // interface resolves against the active book, which is right for editing
    // and wrong for anything that reads across a multi-book project.
    string? BookRootFor(BookData book);
    string? DraftRootFor(BookData book);
    string? ChapterFolderPathFor(BookData book, ChapterData chapter);
    Task<ScenesManifest?> LoadScenesManifestForAsync(BookData book);
    Task<string> ReadSceneContentForAsync(BookData book, ChapterData chapter, SceneData scene);

    List<ChapterData> GetChaptersOrdered();
    List<SceneData> GetScenesForChapter(string chapterGuid);
}
