using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Novalist.Core;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;
using Novalist.Sdk.Services;

namespace Novalist.Backend.Extensions;

/// <summary>
/// Concrete implementation of <see cref="IHostServices"/> that wraps the host's
/// static App services and exposes read-only facades to extensions.
/// </summary>
public sealed partial class HostServices :
    IHostServices,
    IExtensionFileService,
    IExtensionProjectService,
    IExtensionEntityService,
    IExtensionResearchService,
    IExtensionArchiveService,
    IExtensionReviewService,
    IExtensionStoryService,
    IDisposable
{
    private readonly IFileService _fileService;
    private readonly IProjectService _projectService;
    private readonly IEntityService _entityService;
    private readonly ISettingsService _settingsService;
    private readonly UiPump _uiPump;
    private readonly Core.Services.SceneEditingState _editing;
    private readonly bool _ownsPump;
    private readonly Dictionary<string, ExtensionLocalizationService> _locServices = new(StringComparer.Ordinal);

    /// <summary>Reference to the extension manager (set after construction).</summary>
    internal ExtensionManager? ExtensionManager { get; set; }
    internal Func<string, Func<Task>, Task>? CoordinateWorkspace { get; set; }

    private Task ChangeWorkspaceAsync(string reason, Func<Task> action)
        => CoordinateWorkspace?.Invoke(reason, action) ?? action();

    /// <param name="uiPump">Shared backend UI pump. When null, an owned pump is
    /// created and disposed with this instance (used by unit tests that construct
    /// HostServices directly).</param>
    /// <param name="editing">
    /// What the editor has open, so a prose write does not race the writer.
    /// Null means nothing is ever reported busy, which is the right answer for
    /// a headless host with no editor in it.
    /// </param>
    public HostServices(IFileService fileService, IProjectService projectService, IEntityService entityService, ISettingsService settingsService, UiPump? uiPump = null, Core.Services.SceneEditingState? editing = null)
    {
        _editing = editing ?? new Core.Services.SceneEditingState();
        _fileService = fileService;
        _projectService = projectService;
        _entityService = entityService;
        _settingsService = settingsService;
        _uiPump = uiPump ?? new UiPump();
        _ownsPump = uiPump == null;
    }

    public void Dispose()
    {
        if (_ownsPump)
            _uiPump.Dispose();
    }

    // ── IHostServices ──────────────────────────────────────────────

    public IExtensionFileService FileService => this;
    public IExtensionProjectService ProjectService => this;
    public IExtensionEntityService EntityService => this;
    public IExtensionResearchService ResearchService => this;
    public IExtensionReviewService ReviewService => this;
    public IExtensionStoryService StoryService => this;
    public IExtensionArchiveService ArchiveService => this;
    public string HostVersion => VersionInfo.Version;
    public string CurrentLanguage => Loc.Instance.CurrentLanguage;

    public string WritingLanguage =>
        ExportService.NormalizeLanguageTag(_settingsService.Effective.AutoReplacementLanguage);

    public string GetExtensionDataPath(string extensionId)
    {
        var projectRoot = _projectService.ProjectRoot;
        if (string.IsNullOrEmpty(projectRoot))
            throw new InvalidOperationException("No project is loaded.");

        var path = Path.Combine(projectRoot, ".novalist", "extensions", extensionId);
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetExtensionSettingsPath(string extensionId)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var path = Path.Combine(appData, "Novalist", "extensions", extensionId);
        Directory.CreateDirectory(path);
        return path;
    }

    public void PostToUI(Action action)
    {
        _uiPump.Post(action);
    }

    public IExtensionLocalization GetLocalization(string extensionId)
    {
        if (_locServices.TryGetValue(extensionId, out var svc))
            return svc;

        // Return a no-op service that just echoes keys back
        var empty = new ExtensionLocalizationService(string.Empty, CurrentLanguage);
        _locServices[extensionId] = empty;
        return empty;
    }

    /// <summary>
    /// Registers the locale folder for an extension. Must be called before
    /// <see cref="IExtension.Initialize"/> so that <see cref="GetLocalization"/>
    /// returns a properly loaded service.
    /// </summary>
    internal void RegisterExtensionLocales(string extensionId, string localesDir)
    {
        var svc = new ExtensionLocalizationService(localesDir, CurrentLanguage);
        _locServices[extensionId] = svc;
    }

    public void ShowNotification(string message)
    {
        NotificationRequested?.Invoke(message);
    }

    /// <summary>Set by MainWindow at startup. Creates the actual dialog overlay.</summary>
    internal Func<BusyProgressOptions, IBusyProgress>? BusyProgressFactory { get; set; }

    public IBusyProgress ShowBusyProgress(BusyProgressOptions options)
    {
        var factory = BusyProgressFactory;
        if (factory == null)
            return new NoopBusyProgress();

        return _uiPump.CheckAccess()
            ? factory(options)
            : _uiPump.Invoke(() => factory(options));
    }

    private sealed class NoopBusyProgress : IBusyProgress
    {
        public CancellationToken CancellationToken => CancellationToken.None;
        public bool IsClosed { get; private set; }
        public event Action? Cancelled { add { } remove { } }
        public void SetStatus(string status) { }
        public void SetProgress(double value) { }
        public void SetTitle(string title) { }
        public void SetIndeterminate(bool isIndeterminate) { }
        public void SetDetails(System.Collections.Generic.IReadOnlyList<string>? lines) { }
        public void Dispose() => IsClosed = true;
    }

    public void ActivateContentView(string viewKey)
    {
        Log.Debug($"[ExtCtxMenu] ActivateContentView handler null? {ContentViewActivated is null}");
        ContentViewActivated?.Invoke(viewKey, viewKey);
    }

    public void ToggleRightSidebar(string panelId)
    {
        RightSidebarToggled?.Invoke(panelId);
    }

    public void RegisterEditorExtension(IEditorExtension extension)
    {
        EditorExtensionRegistered?.Invoke(extension);
    }

    public void UnregisterEditorExtension(IEditorExtension extension)
    {
        EditorExtensionUnregistered?.Invoke(extension);
    }

    private readonly List<IInlineActionContributor> _inlineActionContributors = new();

    public void RegisterInlineActionContributor(IInlineActionContributor contributor)
    {
        lock (_inlineActionContributors)
        {
            if (!_inlineActionContributors.Contains(contributor))
                _inlineActionContributors.Add(contributor);
        }
        Log.Debug($"[InlineActions] HostServices register contributor {contributor.GetType().Name}. Total: {_inlineActionContributors.Count}. Listeners: {(InlineActionContributorsChanged?.GetInvocationList().Length ?? 0)}");
        InlineActionContributorsChanged?.Invoke();
    }

    public void UnregisterInlineActionContributor(IInlineActionContributor contributor)
    {
        bool removed;
        lock (_inlineActionContributors) { removed = _inlineActionContributors.Remove(contributor); }
        if (removed) InlineActionContributorsChanged?.Invoke();
    }

    public IReadOnlyList<IInlineActionContributor> GetInlineActionContributors()
    {
        lock (_inlineActionContributors) { return _inlineActionContributors.ToList(); }
    }

    /// <summary>Fired when contributors are added/removed so the host can refresh menus.</summary>
    internal event Action? InlineActionContributorsChanged;

    /// <summary>Host-side delegate that the MainWindow plugs in. Extensions
    /// reach the wizard dialog through <see cref="RunWizardAsync"/>.</summary>
    internal Func<Novalist.Sdk.Models.Wizards.WizardDefinition,
                  Novalist.Sdk.Models.Wizards.WizardResult?,
                  Task<Novalist.Sdk.Models.Wizards.WizardResult?>>? WizardLauncher
    { get; set; }

    public async Task<Novalist.Sdk.Models.Wizards.WizardResult?> RunWizardAsync(
        Novalist.Sdk.Models.Wizards.WizardDefinition definition,
        Novalist.Sdk.Models.Wizards.WizardResult? seed = null)
    {
        if (WizardLauncher == null) return null;

        var result = await WizardLauncher.Invoke(definition, seed);

        // Fired here as well as at the RPC entry point, so a wizard behaves the
        // same whether the extension ran it itself or the writer picked it out of
        // the command palette. A callback that only works down one of two paths
        // is worse than none, because it works in testing.
        if (result is { Completed: true } && definition.OnCompleted != null)
        {
            try
            {
                await definition.OnCompleted(result);
            }
            catch (Exception ex)
            {
                // An extension that throws while acting on its own wizard should
                // not take the caller down with it.
                Log.Warn(
                    $"[Extensions] wizard {definition.Id} completion threw {ex.GetType().Name}");
            }
        }

        return result;
    }

    /// <summary>Opens a folder or file dialog in the renderer. Set by the host.</summary>
    internal Func<string, string, bool, Task<string?>>? Picker { get; set; }

    public Task<string?> PickFolderAsync(string title)
        => Picker == null ? Task.FromResult<string?>(null) : Picker("folder", title ?? string.Empty, false);

    public Task<string?> PickFileAsync(string title, bool images = false)
        => Picker == null ? Task.FromResult<string?>(null) : Picker("file", title ?? string.Empty, images);

    public void RegisterHotkey(HotkeyDescriptor descriptor)
    {
        HotkeyRegistry.Register(descriptor);
    }

    public void UnregisterHotkey(string actionId)
    {
        HotkeyRegistry.Unregister(actionId);
    }

    public IReadOnlyList<IAiHook> GetAiHooks()
    {
        return ExtensionManager?.AiHooks ?? (IReadOnlyList<IAiHook>)[];
    }

    public string CurrentLanguageDisplayName => Loc.Instance.GetLanguageDisplayName(Loc.Instance.CurrentLanguage);

    public string? ReadHostData(string key)
    {
        return _settingsService.Settings.ExtensionData.TryGetValue(key, out var json) ? json : null;
    }

    public async Task WriteHostDataAsync(string key, string json)
    {
        _settingsService.Settings.ExtensionData[key] = json;
        await _settingsService.SaveAsync();
    }

    // ── Scene analysis records ──────────────────────────────────────

    private SceneAnalysisStore AnalysisStore => new(_projectService, _fileService);

    public Task<SceneAnalysisRecord?> GetSceneAnalysisAsync(string sceneId)
        => AnalysisStore.ReadAsync(sceneId);

    public Task SaveSceneAnalysisAsync(SceneAnalysisRecord record, string sceneText)
        => AnalysisStore.WriteAsync(record, sceneText);

    public Task<bool> IsSceneAnalysisStaleAsync(string sceneId, string sceneText)
        => AnalysisStore.IsStaleAsync(sceneId, sceneText);

    public Task<IReadOnlyList<string>> GetStaleSceneIdsAsync(IReadOnlyList<SceneTextPair> scenes)
        => AnalysisStore.GetStaleSceneIdsAsync(
            [.. scenes.Select(s => (s.SceneId, s.Text))]);

    public async Task<IReadOnlyList<string>> GetConfirmedMentionIdsAsync(
        string chapterGuid, string sceneId)
    {
        var chapter = _projectService.GetChaptersOrdered()
            .FirstOrDefault(c => string.Equals(c.Guid, chapterGuid, StringComparison.OrdinalIgnoreCase));
        if (chapter == null) return [];
        var scene = _projectService.GetScenesForChapter(chapter.Guid)
            .FirstOrDefault(s => string.Equals(s.Id, sceneId, StringComparison.OrdinalIgnoreCase));
        if (scene == null) return [];

        var html = await _projectService.ReadSceneContentAsync(chapter, scene);
        return AppearanceIndexService.ExtractMentionIds(html);
    }

    // ── Events ──────────────────────────────────────────────────────

    public event Action<Sdk.Services.ProjectInfo>? ProjectLoaded;
    public event Action<Sdk.Services.SceneInfo>? SceneOpened;
    public event Action<Sdk.Services.SceneInfo>? SceneSaved;
    public event Action<Sdk.Services.BookInfo>? BookChanged;
    public event Action<string>? LanguageChanged;

    /// <summary>Internal event for editor extension registration bridging.</summary>
    internal event Action<IEditorExtension>? EditorExtensionRegistered;
    /// <summary>Internal event for editor extension unregistration bridging.</summary>
    internal event Action<IEditorExtension>? EditorExtensionUnregistered;
    /// <summary>Internal event for notification requests from extensions.</summary>
    internal event Action<string>? NotificationRequested;
    /// <summary>Internal event for content view activation requests (viewKey, displayName).</summary>
    internal event Action<string, string>? ContentViewActivated;
    /// <summary>Internal event for right sidebar toggle requests.</summary>
    internal event Action<string>? RightSidebarToggled;

    // ── Event raising (called by ExtensionManager when host events occur) ──

    internal void RaiseProjectLoaded(string name, string rootPath)
    {
        ProjectLoaded?.Invoke(new Sdk.Services.ProjectInfo { Name = name, RootPath = rootPath });
    }

    private Sdk.Services.SceneInfo? _currentScene;

    internal void RaiseSceneOpened(string id, string title, string chapterGuid, string chapterTitle, int wordCount)
    {
        var info = new Sdk.Services.SceneInfo
        {
            Id = id,
            Title = title,
            ChapterGuid = chapterGuid,
            ChapterTitle = chapterTitle,
            WordCount = wordCount
        };
        _currentScene = info;
        // Do not log the scene title — it is story content. id is a non-content identifier.
        Log.Debug($"[HostServices] RaiseSceneOpened id={id} subscribers={SceneOpened?.GetInvocationList().Length ?? 0}");
        SceneOpened?.Invoke(info);
    }

    internal void RaiseSceneSaved(string id, string title, string chapterGuid, string chapterTitle, int wordCount)
    {
        SceneSaved?.Invoke(new Sdk.Services.SceneInfo
        {
            Id = id,
            Title = title,
            ChapterGuid = chapterGuid,
            ChapterTitle = chapterTitle,
            WordCount = wordCount
        });
    }

    internal void RaiseBookChanged(string id, string name)
    {
        BookChanged?.Invoke(new Sdk.Services.BookInfo { Id = id, Name = name });
    }

    internal void RaiseLanguageChanged(string language)
    {
        // Reload all extension locale services first so T() returns updated strings
        foreach (var svc in _locServices.Values)
            svc.Reload(language);

        LanguageChanged?.Invoke(language);
    }

    /// <summary>Internal event for entity refresh requests from extensions.</summary>
    internal event Action? EntityRefreshRequested;

    /// <summary>
    /// Raised when an extension changes the shape of the project - a book, a
    /// draft, a chapter, a scene. The renderer holds its own copy of that shape
    /// and has no other way to learn it moved.
    /// </summary>
    internal event Action? ProjectStructureChanged;
}
