using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sdk.Models;

namespace Novalist.Backend;

/// <summary>
/// Owns the Core service graph for the running app: one project open at a time,
/// app settings (recents), and the word-history journal. RPC facades are thin
/// wrappers over this class so behavior stays testable without an RPC pair.
/// </summary>
public sealed partial class Workspace : IDisposable
{
    /// <summary>Where Novalist keeps its own files, as the host was started
    /// with. Null means the application-data folder, which the services resolve
    /// for themselves - it is kept so the ones that need a sibling folder, such
    /// as the narration clip cache, land beside everything else.</summary>
    public string? SettingsDirectory { get; }

    /// <summary>Platform help for paths that have stopped working - see
    /// <see cref="IStoredPathResolver"/>. Null on the desktop, where a stored
    /// path is simply an address.</summary>
    private readonly IStoredPathResolver? _storedPaths;

    public Workspace(string? settingsDirectory = null, IStoredPathResolver? storedPaths = null,
        IFileService? fileService = null)
    {
        SettingsDirectory = settingsDirectory;
        _storedPaths = storedPaths;
        FileService = fileService ?? new FileService();
        Projects = new ProjectService(FileService);
        Settings = new SettingsService(settingsDirectory);
        // Beside the settings rather than inside a project: a thought that
        // arrives before the right project is open still has somewhere to go.
        Scratchpad = new ScratchpadService(settingsDirectory);
        ArchiveService = new ArchiveService();
        WordHistory = new WordHistoryService(FileService, Projects);
        UserAssets = new Appearance.UserAssetsService(settingsDirectory);
        UserAssets.EnsureDirectories();
        // A dropped analysis.<tag>.json makes the Inspector's keyword analysis
        // work for a writing language Novalist does not ship, and overrides a
        // shipped one. Registered before any scene is analysed.
        SceneAnalysisLexicon.RegisterUserDirectory(UserAssets.AnalysisDirectory);
    }

    public IFileService FileService { get; }
    public ProjectService Projects { get; }
    public SettingsService Settings { get; }
    internal WorkspaceCoordinator? Coordinator { get; set; }

    internal async Task<BackupService> GetBackupServiceAsync()
    {
        // A saved iOS path does not carry permission across launches. Resolve
        // its bookmark before even checking existence, and persist a moved
        // folder because resolving it also re-keys the platform's bookmark.
        var configured = Settings.Settings.BackupFolder;
        if (_storedPaths != null && !string.IsNullOrWhiteSpace(configured))
        {
            var current = _storedPaths.Resolve(configured);
            if (!string.IsNullOrWhiteSpace(current) && current != configured)
            {
                Settings.Settings.BackupFolder = current;
                await Settings.SaveAsync();
            }
        }
        return new BackupService(Projects, FileService, ArchiveService, Settings);
    }

    /// <summary>
    /// What the editor has open, so an extension writing prose does not race
    /// the writer. Reported by the renderer, which is the only thing that knows.
    /// </summary>
    public Core.Services.SceneEditingState Editing { get; } = new();

    /// <summary>
    /// Loose notes that belong to the writer rather than to a project, so a
    /// thought that arrives before the right project is open has somewhere to go.
    /// </summary>
    public ScratchpadService Scratchpad { get; }

    /// <summary>ZIP creation and extraction, used by whole-project backups.</summary>
    public ArchiveService ArchiveService { get; }
    public WordHistoryService WordHistory { get; }

    /// <summary>User-supplied themes, interface locales, and analysis lexicons
    /// dropped into folders beside the extensions directory.</summary>
    public Appearance.UserAssetsService UserAssets { get; }

    private Extensions.ExtensionManager? _extensions;
    private Extensions.HostServices? _hostServices;
    private Extensions.UiPump? _uiPump;

    /// <summary>Bridges host-service UI capabilities (toasts, busy-progress,
    /// wizards) to the renderer. Created eagerly (no threads) so the backend host
    /// can attach its RPC notifier before extensions load.</summary>
    public Extensions.UiBridge UiBridge { get; } = new();

    /// <summary>Test seam: overrides the extension discovery directory.</summary>
    public Extensions.ExtensionLoader? ExtensionsLoaderOverride { get; set; }
    internal event Action? Disposing;

    public void Dispose()
    {
        Disposing?.Invoke();
        Disposing = null;
        _extensions?.ShutdownAll();
        _hostServices?.Dispose();
        _uiPump?.Dispose();
    }

    public async Task<ProjectStateDto> OpenProjectAsync(string projectDirectory, string? bookId = null)
    {
        await Settings.LoadAsync();
        var metadata = await Projects.LoadProjectAsync(projectDirectory, bookId);
        await Projects.ReconcileActiveDraftAsync();
        Settings.SetActiveOverrides(Projects.ProjectSettings.Overrides);
        // Record the portrait cover's absolute path so the welcome screen can
        // render each recent project's cover without opening it.
        Settings.AddRecentProject(metadata.Name, projectDirectory, ActiveCoverAbsolutePath() ?? string.Empty);
        Settings.Settings.RecentProjects[0].ProjectId = metadata.Id;
        await Settings.SaveAsync();
        // Notify extensions (e.g. the AI Assistant's first-run setup + knowledge
        // cache) that a project is now available.
        RaiseProjectLoaded();
        // Merge any extension-contributed entity types into the project's custom
        // type registry so they surface in the Codex (mirrors the desktop's
        // EntityPanelViewModel.LoadCustomEntityTypesAsync).
        await RegisterExtensionEntityTypesAsync();
        return BuildState();
    }

    /// <summary>
    /// Closes the open project and answers with the empty state, which is the
    /// same state the app starts in.
    /// </summary>
    /// <remarks>
    /// The per-project settings overrides go with it, or the next project would
    /// open under the last one's preferences.
    /// </remarks>
    public ProjectStateDto CloseProject()
    {
        Projects.CloseProject();
        Settings.SetActiveOverrides(null);
        // The rendered reading goes with the project. Clips are kept between
        // readings now - that is what makes listening to a scene twice cost
        // once - so this is where they stop, rather than surviving into
        // whatever the writer opens next. Speech of somebody's manuscript
        // should not outlive the project being open.
        new NarrationClipCache(SettingsDirectory).Clear();
        return BuildState();
    }

    public ChapterData ResolveChapter(string chapterGuid)
    {
        var book = Projects.ActiveBook ?? throw new InvalidOperationException("No project open.");
        return book.Chapters.FirstOrDefault(c => c.Guid == chapterGuid)
            ?? throw new InvalidOperationException("Unknown chapter.");
    }

    public (ChapterData chapter, SceneData scene) ResolveScene(string chapterGuid, string sceneId)
    {
        var chapter = ResolveChapter(chapterGuid);
        var scene = Projects.ScenesManifest?.Chapters.GetValueOrDefault(chapterGuid)?.FirstOrDefault(s => s.Id == sceneId)
            ?? throw new InvalidOperationException("Unknown scene.");
        return (chapter, scene);
    }

    public async Task<int> WriteSceneAsync(string chapterGuid, string sceneId, string html, string plainText)
    {
        var (chapter, scene) = ResolveScene(chapterGuid, sceneId);
        await Projects.WriteSceneContentAsync(chapter, scene, html);
        await AfterSceneWriteAsync(chapter, scene, html, plainText);
        return scene.WordCount;
    }

    /// <summary>The guard that refuses a save when the file changed underneath
    /// it, wired to the snapshot service so resolving a conflict keeps both
    /// sides.</summary>
    public SceneConflictGuard SceneConflicts
        => new(Projects, new SnapshotService(Projects, FileService));

    /// <summary>
    /// Saves a scene unless it changed on disk since the editor read it. A
    /// conflicting save is refused rather than merged, and the outcome carries
    /// what is on disk so the writer can be shown both.
    /// </summary>
    public async Task<(SceneSaveOutcome Outcome, int WordCount)> WriteSceneCheckedAsync(
        string chapterGuid, string sceneId, string html, string plainText, string? expectedHash)
    {
        var (chapter, scene) = ResolveScene(chapterGuid, sceneId);
        var outcome = await SceneConflicts.SaveAsync(chapter, scene, html, expectedHash);
        // A refused save must not touch the word history or the manifest: nothing
        // was written, so recording it would report progress that did not happen.
        if (outcome.Conflicted) return (outcome, scene.WordCount);

        await AfterSceneWriteAsync(chapter, scene, html, plainText);
        return (outcome, scene.WordCount);
    }

    /// <summary>The bookkeeping every successful scene write does: word count,
    /// history, manifest, and the extension event.</summary>
    private async Task AfterSceneWriteAsync(
        ChapterData chapter, SceneData scene, string html, string plainText)
    {
        scene.WordCount = CountWords(plainText.Length > 0 ? plainText : StripHtml(html));
        await WordHistory.RecordSaveAsync(
            Projects.ActiveBook?.Id ?? string.Empty, scene.Id, scene.WordCount);
        await Projects.SaveScenesAsync();
        RaiseSceneSaved(chapter, scene);
    }

    // Same regex the Avalonia EditorViewModel uses, so persisted word counts stay identical.
    /// <summary>
    /// A scene's word count, in whatever script it is written in.
    ///
    /// Counting runs of letters made a Chinese scene of five hundred characters
    /// come out as a handful of words, which put the word count, the daily goal
    /// and every target wrong for a language the app ships an interface for.
    /// </summary>
    internal static int CountWords(string text)
        => Core.Utilities.ScriptAwareCounting.Count(text);

    internal static string StripHtml(string content)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;
        if (!content.TrimStart().StartsWith('<')) return content;
        var text = Regex.Replace(content, "<[^>]+>", string.Empty);
        return System.Net.WebUtility.HtmlDecode(text);
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+(?:['’-][\p{L}\p{N}]+)*", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}

public sealed record ProjectStateDto(
    bool IsLoaded,
    string? ProjectName,
    string? ProjectPath,
    string? ActiveBookId,
    IReadOnlyList<BookDto> Books,
    IReadOnlyList<ChapterDto> Chapters);

public sealed record BookDto(string Id, string Name);

public sealed record ChapterDto(
    string Guid,
    string Title,
    int Order,
    string Status,
    string Act,
    bool IsFavorite,
    /// <summary>Printed below the chapter title in exported books; null means no subtitle.</summary>
    string? Subtitle,
    /// <summary>True when the chapter opens straight into its prose.</summary>
    bool HideHeading,
    /// <summary>What the chapter is for, in the writer's words. Never printed -
    /// the subtitle is what a reader sees, this is the writer's own note.</summary>
    string? Description,
    /// <summary>What the chapter is - a chapter, a prologue, a part. Empty is
    /// an ordinary chapter, which is what every chapter was before types.</summary>
    string SectionTypeKey,
    IReadOnlyList<SceneDto> Scenes);

public sealed record SceneDto(
    string Id,
    string Title,
    int Order,
    int WordCount,
    string? LabelColor,
    bool IsFavorite,
    string? Synopsis,
    /// <summary>Key of the scene's stage, or null when the writer has not set
    /// one. Null is untriaged, not "at the first stage".</summary>
    string? Stage,
    /// <summary>True when the writer is holding this scene back from exports.</summary>
    bool ExcludeFromExport,
    /// <summary>True when the scene is out of the book but still in the plan:
    /// shown here, absent from word totals, targets and every export.</summary>
    bool Inactive,
    /// <summary>
    /// Colours of the threads this scene belongs to, in the book's plotline
    /// order. Plotlines had a colour that only ever appeared inside the Plot
    /// Grid, so which threads a scene serves was invisible everywhere the
    /// writer actually is.
    /// </summary>
    IReadOnlyList<string> PlotlineColors,
    /// <summary>The same threads by id, so the binder can filter by one.</summary>
    IReadOnlyList<string> PlotlineIds);

public sealed record RecentProjectDto(string Name, string Path, string? Cover,
    string? ProjectId = null, LibraryBookDto[]? Books = null, bool? HasWorldBible = null);
public sealed record LibraryBookDto(string Id, string Name, string? Cover);
