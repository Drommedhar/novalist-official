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

    public Workspace(string? settingsDirectory = null)
    {
        SettingsDirectory = settingsDirectory;
        FileService = new FileService();
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

    public FileService FileService { get; }
    public ProjectService Projects { get; }
    public SettingsService Settings { get; }

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

    /// <summary>The headless extension host, created on first use.</summary>
    public Extensions.ExtensionManager ExtensionsHost
    {
        get
        {
            if (_extensions == null)
            {
                _uiPump = new Extensions.UiPump();
                _hostServices = new Extensions.HostServices(
                    FileService, Projects, new EntityService(Projects), Settings, _uiPump, Editing);
                _hostServices.NotificationRequested += UiBridge.ShowNotification;
                // An extension's write has to reach the screen, or it changed a
                // file and nothing the writer can see.
                _hostServices.EntityRefreshRequested += UiBridge.EntitiesChanged;
                _hostServices.ProjectStructureChanged += UiBridge.ProjectStructureChanged;
                _hostServices.BusyProgressFactory = UiBridge.CreateProgress;
                _hostServices.WizardLauncher = UiBridge.RunWizardAsync;
                _hostServices.Picker = UiBridge.PickAsync;
                _extensions = new Extensions.ExtensionManager(Settings, _hostServices, ExtensionsLoaderOverride);
                _hostServices.ExtensionManager = _extensions;
            }
            return _extensions;
        }
    }

    /// <summary>The host-services event raiser, or null when no extension host
    /// has been created yet (no extension has been touched this session).</summary>
    internal Extensions.HostServices? HostServices => _hostServices;

    /// <summary>The extension manager, or null when no extension host has been
    /// created yet. Lets callers consult contributions without force-creating a
    /// host (and its pump thread) for projects that never touched extensions.</summary>
    internal Extensions.ExtensionManager? ExtensionHostOrNull => _extensions;

    public void Dispose()
    {
        _extensions?.ShutdownAll();
        _hostServices?.Dispose();
        _uiPump?.Dispose();
    }

    public async Task<ProjectStateDto> OpenProjectAsync(string projectDirectory)
    {
        await Settings.LoadAsync();
        var metadata = await Projects.LoadProjectAsync(projectDirectory);
        await Projects.ReconcileActiveDraftAsync();
        Settings.SetActiveOverrides(Projects.ProjectSettings.Overrides);
        // Record the portrait cover's absolute path so the welcome screen can
        // render each recent project's cover without opening it.
        Settings.AddRecentProject(metadata.Name, projectDirectory, ActiveCoverAbsolutePath() ?? string.Empty);
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
    /// Ensures every extension-contributed <see cref="EntityTypeDescriptor"/> is
    /// present in the loaded project's custom entity types (as a
    /// <see cref="Source"/>="extension" definition). Idempotent and a no-op when
    /// no extension host exists or no project is loaded.
    /// </summary>
    internal async Task RegisterExtensionEntityTypesAsync()
    {
        if (_extensions == null || Projects.CurrentProject == null) return;
        var service = new EntityService(Projects);
        foreach (var descriptor in _extensions.EntityTypes)
        {
            if (service.GetCustomEntityTypes().Any(t =>
                    string.Equals(t.TypeKey, descriptor.TypeKey, StringComparison.Ordinal)))
                continue;
            await service.SaveCustomEntityTypeAsync(MapExtensionEntityType(descriptor));
        }
    }

    private static CustomEntityTypeDefinition MapExtensionEntityType(EntityTypeDescriptor d) => new()
    {
        TypeKey = d.TypeKey,
        DisplayName = d.DisplayName,
        DisplayNamePlural = string.IsNullOrWhiteSpace(d.DisplayNamePlural) ? d.DisplayName : d.DisplayNamePlural,
        Icon = d.Icon,
        FolderName = string.IsNullOrWhiteSpace(d.FolderName) ? d.TypeKey : d.FolderName,
        Source = "extension",
        DefaultFields = d.DefaultFields.Select(f => new CustomEntityFieldDefinition
        {
            Key = f.Key,
            DisplayName = f.DisplayName,
            Type = WellKnownPropertyTypes.TryToEnum(f.TypeKey, out var enumType) ? enumType : CustomPropertyType.String,
            TypeKey = WellKnownPropertyTypes.TryToEnum(f.TypeKey, out _) ? null : f.TypeKey,
            DefaultValue = f.DefaultValue,
            EnumOptions = f.EnumOptions,
            Required = f.Required,
        }).ToList(),
        Features = new CustomEntityFeatures
        {
            IncludeImages = d.Features.IncludeImages,
            IncludeRelationships = d.Features.IncludeRelationships,
            IncludeSections = d.Features.IncludeSections,
        },
    };

    // ── Extension host-event raisers ────────────────────────────────
    // No-ops until an extension host has been created (extensions/load). They
    // never force-create it, so projects without extensions pay nothing.

    internal void RaiseProjectLoaded()
    {
        if (_hostServices == null) return;
        var project = Projects.CurrentProject;
        if (project == null) return;
        _hostServices.RaiseProjectLoaded(project.Name, Projects.ProjectRoot ?? string.Empty);
    }

    internal void RaiseSceneOpened(ChapterData chapter, SceneData scene)
        => _hostServices?.RaiseSceneOpened(scene.Id, scene.Title, chapter.Guid, chapter.Title, scene.WordCount);

    internal void RaiseSceneSaved(ChapterData chapter, SceneData scene)
        => _hostServices?.RaiseSceneSaved(scene.Id, scene.Title, chapter.Guid, chapter.Title, scene.WordCount);

    internal void RaiseBookChanged()
    {
        if (_hostServices == null) return;
        var book = Projects.ActiveBook;
        if (book == null) return;
        _hostServices.RaiseBookChanged(book.Id, book.Name);
    }

    /// <summary>Syncs the extension-facing language and fires LanguageChanged.</summary>
    internal void RaiseLanguageChanged(string language)
    {
        Extensions.Loc.Instance.CurrentLanguage = language;
        _hostServices?.RaiseLanguageChanged(language);
    }

    /// <summary>Keeps the extension-facing current language in step with settings
    /// without firing the change event (used when the host is first created).</summary>
    internal void SyncExtensionLanguage()
        => Extensions.Loc.Instance.CurrentLanguage = Settings.Effective.Language;

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
        return BuildState();
    }

    public ProjectStateDto BuildState()
    {
        var project = Projects.CurrentProject;
        var book = Projects.ActiveBook;
        if (project == null || book == null)
        {
            return new ProjectStateDto(false, null, null, null, Array.Empty<BookDto>(), Array.Empty<ChapterDto>());
        }

        var chapters = book.Chapters
            .OrderBy(c => c.Order)
            .Select(c => new ChapterDto(
                c.Guid,
                c.Title,
                c.Order,
                c.Status.ToString(),
                c.Act,
                c.IsFavorite,
                c.Subtitle,
                c.HideHeading,
                c.Description,
                c.SectionTypeKey,
                ScenesOf(c.Guid)))
            .ToArray();

        return new ProjectStateDto(
            true,
            project.Name,
            Projects.ProjectRoot,
            book.Id,
            project.Books.Select(b => new BookDto(b.Id, b.Name)).ToArray(),
            chapters);
    }

    /// <summary>
    /// The colour a scene's label paints it, or the bare colour a project
    /// saved before labels had names still carries. A label key whose label is
    /// gone paints nothing, which reads as no label rather than as a mistake.
    /// </summary>
    private string? ResolveLabelColor(Core.Models.SceneData scene)
    {
        if (scene.LabelKey == null) return scene.LabelColor;
        return (Projects.ActiveBook?.SceneLabels ?? [])
            .FirstOrDefault(l => string.Equals(l.Key, scene.LabelKey, StringComparison.OrdinalIgnoreCase))
            ?.Color;
    }

    /// <summary>
    /// The colours of a scene's threads, in the book's plotline order.
    ///
    /// A plotline has carried a colour since the Plot Grid shipped and it never
    /// left that view, so the binder could not show that this scene and that one
    /// are the same thread.
    /// </summary>
    /// <summary>
    /// The threads this scene serves, by id, in the book's plotline order.
    /// Colours alone cannot drive a filter: two threads can share one, and a
    /// writer picks a thread by its name.
    /// </summary>
    private IReadOnlyList<string> ResolvePlotlineIds(SceneData scene)
    {
        var ids = scene.PlotlineIds;
        if (ids == null || ids.Count == 0) return [];

        return [.. (Projects.ActiveBook?.Plotlines ?? [])
            .OrderBy(p => p.Order)
            .Where(p => ids.Contains(p.Id, StringComparer.Ordinal))
            .Select(p => p.Id)];
    }

    private IReadOnlyList<string> ResolvePlotlineColors(SceneData scene)
    {
        var ids = scene.PlotlineIds;
        if (ids == null || ids.Count == 0) return [];

        return [.. (Projects.ActiveBook?.Plotlines ?? [])
            .OrderBy(p => p.Order)
            .Where(p => ids.Contains(p.Id, StringComparer.Ordinal))
            .Select(p => p.Color)];
    }

    private SceneDto[] ScenesOf(string chapterGuid)
    {
        var manifest = Projects.ScenesManifest;
        if (manifest == null || !manifest.Chapters.TryGetValue(chapterGuid, out var scenes))
        {
            return Array.Empty<SceneDto>();
        }
        return scenes
            .Where(s => s.ArchivedAt == null)
            .OrderBy(s => s.Order)
            .Select(s => new SceneDto(
                s.Id, s.Title, s.Order, s.WordCount, ResolveLabelColor(s), s.IsFavorite, s.Synopsis,
                s.Stage, s.ExcludeFromExport, s.Inactive, ResolvePlotlineColors(s),
                ResolvePlotlineIds(s)))
            .ToArray();
    }

    /// <summary>
    /// The projects worth offering to reopen.
    ///
    /// An entry whose folder is no longer a project is dropped from the list and
    /// from the stored settings as it is found, rather than being offered and
    /// failing when it is clicked. The list is a set of ways back into work, and
    /// a row that cannot be opened is not one of those - it is a dead end the
    /// writer has to learn to skip past.
    /// </summary>
    public async Task<RecentProjectDto[]> GetRecentProjectsAsync()
    {
        await Settings.LoadAsync();
        var results = new List<RecentProjectDto>();
        var dropped = false;

        // Over a copy: the loop removes from the list it is walking.
        foreach (var r in Settings.Settings.RecentProjects.ToList())
        {
            if (!await Projects.ProjectExistsAtAsync(r.Path))
            {
                Settings.RemoveRecentProject(r.Path);
                dropped = true;
                continue;
            }

            results.Add(new RecentProjectDto(r.Name, r.Path, await LoadCoverDataUriAsync(r.CoverImagePath)));
        }

        if (dropped) await Settings.SaveAsync();
        return results.ToArray();
    }

    /// <summary>Absolute filesystem path of the active project's portrait cover
    /// image (book cover, falling back to the project cover), or null when none
    /// is set or no project is open.</summary>
    internal string? ActiveCoverAbsolutePath()
    {
        var rel = Projects.ActiveBook?.CoverImage;
        if (string.IsNullOrEmpty(rel))
            rel = Projects.CurrentProject?.CoverImage;
        if (string.IsNullOrEmpty(rel) || Projects.ActiveBookRoot == null)
            return null;
        return Path.Combine(Projects.ActiveBookRoot, rel.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>Re-records the active project's cover path on its recent-projects
    /// entry (e.g. after the cover changes) so the welcome screen stays current.</summary>
    internal async Task RefreshRecentCoverAsync()
    {
        var root = Projects.ProjectRoot;
        if (root == null) return;
        var recent = Settings.Settings.RecentProjects.FirstOrDefault(r => r.Path == root);
        if (recent == null) return;
        recent.CoverImagePath = ActiveCoverAbsolutePath() ?? string.Empty;
        await Settings.SaveAsync();
    }

    /// <summary>Reads a recent project's cover file and returns it as a base64
    /// <c>data:</c> URI, or null when the path is empty or the file is absent.
    /// Recent projects are not the active project, so their cover cannot be
    /// served through <c>novalist-project://</c> (which resolves against the
    /// active root) — the bytes are inlined instead.</summary>
    internal static async Task<string?> LoadCoverDataUriAsync(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            return $"data:{MimeForExtension(Path.GetExtension(path))};base64,{Convert.ToBase64String(bytes)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The cover must never take down the recents list. Under the macOS App
            // Sandbox a recent project we don't currently hold access to — e.g. one
            // in iCloud Drive, whose cover file may be dataless — passes File.Exists
            // but throws on read. Degrade to no thumbnail instead of failing
            // GetRecentProjectsAsync (which would leave the start screen empty).
            return null;
        }
    }

    internal static string MimeForExtension(string extension)
    {
        return extension.TrimStart('.').ToLowerInvariant() switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            "gif" => "image/gif",
            "webp" => "image/webp",
            "bmp" => "image/bmp",
            _ => "application/octet-stream",
        };
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
    /// <summary>A second line under the chapter title in the finished book.</summary>
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

public sealed record RecentProjectDto(string Name, string Path, string? Cover);
