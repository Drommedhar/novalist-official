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
public sealed class HostServices : IHostServices, IExtensionFileService, IExtensionProjectService, IExtensionEntityService, IDisposable
{
    private readonly IFileService _fileService;
    private readonly IProjectService _projectService;
    private readonly IEntityService _entityService;
    private readonly ISettingsService _settingsService;
    private readonly UiPump _uiPump;
    private readonly bool _ownsPump;
    private readonly Dictionary<string, ExtensionLocalizationService> _locServices = new(StringComparer.Ordinal);

    /// <summary>Reference to the extension manager (set after construction).</summary>
    internal ExtensionManager? ExtensionManager { get; set; }

    /// <param name="uiPump">Shared backend UI pump. When null, an owned pump is
    /// created and disposed with this instance (used by unit tests that construct
    /// HostServices directly).</param>
    public HostServices(IFileService fileService, IProjectService projectService, IEntityService entityService, ISettingsService settingsService, UiPump? uiPump = null)
    {
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
    public string HostVersion => VersionInfo.Version;
    public string CurrentLanguage => Loc.Instance.CurrentLanguage;

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
                  Task<Novalist.Sdk.Models.Wizards.WizardResult?>>? WizardLauncher { get; set; }

    public Task<Novalist.Sdk.Models.Wizards.WizardResult?> RunWizardAsync(
        Novalist.Sdk.Models.Wizards.WizardDefinition definition,
        Novalist.Sdk.Models.Wizards.WizardResult? seed = null)
    {
        if (WizardLauncher == null)
            return Task.FromResult<Novalist.Sdk.Models.Wizards.WizardResult?>(null);
        return WizardLauncher.Invoke(definition, seed);
    }

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
            Id = id, Title = title, ChapterGuid = chapterGuid,
            ChapterTitle = chapterTitle, WordCount = wordCount
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
            Id = id, Title = title, ChapterGuid = chapterGuid,
            ChapterTitle = chapterTitle, WordCount = wordCount
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

    // ── IExtensionFileService ──────────────────────────────────────

    Task<string> IExtensionFileService.ReadTextAsync(string path) => _fileService.ReadTextAsync(path);
    Task IExtensionFileService.WriteTextAsync(string path, string content) => _fileService.WriteTextAsync(path, content);
    Task<bool> IExtensionFileService.ExistsAsync(string path) => _fileService.ExistsAsync(path);
    Task<bool> IExtensionFileService.DirectoryExistsAsync(string path) => _fileService.DirectoryExistsAsync(path);
    Task IExtensionFileService.CreateDirectoryAsync(string path) => _fileService.CreateDirectoryAsync(path);
    Task<IReadOnlyList<string>> IExtensionFileService.GetFilesAsync(string directory, string pattern, bool recursive) => _fileService.GetFilesAsync(directory, pattern, recursive);
    Task<IReadOnlyList<string>> IExtensionFileService.GetDirectoriesAsync(string directory) => _fileService.GetDirectoriesAsync(directory);
    string IExtensionFileService.CombinePath(params string[] parts) => _fileService.CombinePath(parts);
    string IExtensionFileService.GetFileName(string path) => _fileService.GetFileName(path);
    string IExtensionFileService.GetFileNameWithoutExtension(string path) => _fileService.GetFileNameWithoutExtension(path);
    string IExtensionFileService.GetDirectoryName(string path) => _fileService.GetDirectoryName(path);

    // ── IExtensionProjectService ───────────────────────────────────

    string? IExtensionProjectService.ProjectRoot => _projectService.ProjectRoot;
    string? IExtensionProjectService.ActiveBookRoot => _projectService.ActiveBookRoot;
    string? IExtensionProjectService.WorldBibleRoot => _projectService.WorldBibleRoot;
    bool IExtensionProjectService.IsProjectLoaded => _projectService.IsProjectLoaded;
    Sdk.Services.SceneInfo? IExtensionProjectService.CurrentScene => _currentScene;

    async Task<string> IExtensionProjectService.ReadSceneContentAsync(string chapterGuid, string sceneId)
    {
        var manifest = _projectService.ScenesManifest;
        if (manifest == null)
            return string.Empty;

        if (!manifest.Chapters.TryGetValue(chapterGuid, out var scenes))
            return string.Empty;

        var scene = scenes.FirstOrDefault(s => s.Id == sceneId);
        if (scene == null)
            return string.Empty;

        var chapter = _projectService.GetChaptersOrdered().FirstOrDefault(c => c.Guid == chapterGuid);
        if (chapter == null)
            return string.Empty;

        return await _projectService.ReadSceneContentAsync(chapter, scene);
    }

    Task<string> IExtensionProjectService.GetSceneSynopsisAsync(string chapterGuid, string sceneId)
    {
        var manifest = _projectService.ScenesManifest;
        if (manifest == null || !manifest.Chapters.TryGetValue(chapterGuid, out var scenes))
            return Task.FromResult(string.Empty);
        var scene = scenes.FirstOrDefault(s => s.Id == sceneId);
        return Task.FromResult(scene?.Synopsis ?? string.Empty);
    }

    async Task IExtensionProjectService.SetSceneSynopsisAsync(string chapterGuid, string sceneId, string synopsis)
    {
        var manifest = _projectService.ScenesManifest;
        if (manifest == null || !manifest.Chapters.TryGetValue(chapterGuid, out var scenes)) return;
        var scene = scenes.FirstOrDefault(s => s.Id == sceneId);
        if (scene == null) return;
        scene.Synopsis = string.IsNullOrWhiteSpace(synopsis) ? null : synopsis.Trim();
        await _projectService.SaveScenesAsync();
    }

    async Task<string> IExtensionProjectService.CreateChapterAsync(string title)
    {
        var chapter = await _projectService.CreateChapterAsync(title ?? string.Empty);
        return chapter.Guid;
    }

    async Task<string> IExtensionProjectService.CreateSceneAsync(string chapterGuid, string title)
    {
        // A scene under a chapter that does not exist would be unreachable, so
        // the caller gets an empty id rather than an orphan.
        if (!_projectService.GetChaptersOrdered().Any(c => c.Guid == chapterGuid))
            return string.Empty;

        var scene = await _projectService.CreateSceneAsync(chapterGuid, title ?? string.Empty);
        return scene.Id;
    }

    async Task IExtensionProjectService.WriteSceneContentAsync(
        string chapterGuid, string sceneId, string html)
    {
        var chapter = _projectService.GetChaptersOrdered().FirstOrDefault(c => c.Guid == chapterGuid);
        var scene = chapter == null
            ? null
            : _projectService.GetScenesForChapter(chapterGuid).FirstOrDefault(sc => sc.Id == sceneId);
        if (chapter == null || scene == null) return;

        await _projectService.WriteSceneContentAsync(chapter, scene, html ?? string.Empty);
        // The manifest carries the word count the binder shows, so it has to
        // catch up with what was just written.
        scene.WordCount = Workspace.CountWords(Core.Utilities.TextDiff.StripHtml(html ?? string.Empty));
        await _projectService.SaveScenesAsync();
    }

    IReadOnlyList<Sdk.Services.ChapterInfo> IExtensionProjectService.GetChaptersOrdered()
    {
        return _projectService.GetChaptersOrdered()
            .Select(c => new Sdk.Services.ChapterInfo
            {
                Guid = c.Guid,
                Title = c.Title,
                Order = c.Order,
                Date = c.Date ?? string.Empty
            })
            .ToList();
    }

    IReadOnlyList<Sdk.Services.SceneInfo> IExtensionProjectService.GetScenesForChapter(string chapterGuid)
    {
        var manifest = _projectService.ScenesManifest;
        if (manifest == null)
            return [];

        if (!manifest.Chapters.TryGetValue(chapterGuid, out var scenes))
            return [];

        var chapter = _projectService.GetChaptersOrdered().FirstOrDefault(c => c.Guid == chapterGuid);
        var chapterTitle = chapter?.Title ?? string.Empty;

        return scenes
            .Select(s => new Sdk.Services.SceneInfo
            {
                Id = s.Id,
                Title = s.Title,
                ChapterGuid = chapterGuid,
                ChapterTitle = chapterTitle,
                WordCount = s.WordCount
            })
            .ToList();
    }

    // ── IExtensionEntityService ────────────────────────────────────

    async Task<IReadOnlyList<Sdk.Services.CharacterInfo>> IExtensionEntityService.LoadCharactersAsync()
    {
        var characters = await _entityService.LoadCharactersAsync();
        return characters.Select(c => new Sdk.Services.CharacterInfo
        {
            Id = c.Id,
            DisplayName = c.DisplayName,
            Role = c.Role
        }).ToList();
    }

    async Task<IReadOnlyList<Sdk.Services.LocationInfo>> IExtensionEntityService.LoadLocationsAsync()
    {
        var locations = await _entityService.LoadLocationsAsync();
        return locations.Select(l => new Sdk.Services.LocationInfo
        {
            Id = l.Id,
            Name = l.Name,
            Type = l.Type
        }).ToList();
    }

    async Task<IReadOnlyList<Sdk.Services.ItemInfo>> IExtensionEntityService.LoadItemsAsync()
    {
        var items = await _entityService.LoadItemsAsync();
        return items.Select(i => new Sdk.Services.ItemInfo
        {
            Id = i.Id,
            Name = i.Name,
            Type = i.Type
        }).ToList();
    }

    async Task<IReadOnlyList<Sdk.Services.LoreInfo>> IExtensionEntityService.LoadLoreAsync()
    {
        var lore = await _entityService.LoadLoreAsync();
        return lore.Select(l => new Sdk.Services.LoreInfo
        {
            Id = l.Id,
            Name = l.Name,
            Category = l.Category
        }).ToList();
    }

    async Task<IReadOnlyList<Sdk.Services.CustomEntityInfo>> IExtensionEntityService.LoadCustomEntitiesAsync(string typeKey)
    {
        var entities = await _entityService.LoadCustomEntitiesAsync(typeKey);
        return entities.Select(e => new Sdk.Services.CustomEntityInfo
        {
            Id = e.Id,
            Name = e.Name,
            EntityTypeKey = e.EntityTypeKey,
            Fields = e.Fields
        }).ToList();
    }

    IReadOnlyList<Sdk.Services.CustomEntityTypeInfo> IExtensionEntityService.GetCustomEntityTypes()
    {
        return _entityService.GetCustomEntityTypes().Select(t => new Sdk.Services.CustomEntityTypeInfo
        {
            TypeKey = t.TypeKey,
            DisplayName = t.DisplayName,
            DisplayNamePlural = t.DisplayNamePlural,
            Icon = t.Icon
        }).ToList();
    }

    async Task IExtensionEntityService.SaveCustomEntityAsync(Sdk.Services.CustomEntityInfo entity)
    {
        var data = new CustomEntityData
        {
            Id = entity.Id,
            Name = entity.Name,
            EntityTypeKey = entity.EntityTypeKey,
            Fields = new Dictionary<string, string>(entity.Fields),
        };
        if (entity.Sections is { } sections)
        {
            data.Sections = sections.Select(s => new EntitySection
            {
                Title = s.Title,
                Content = s.Content,
            }).ToList();
        }
        await _entityService.SaveCustomEntityAsync(data);
    }

    async Task<string?> IExtensionEntityService.CreateEntityAsync(
        string typeKey, string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var trimmed = name.Trim();

        switch ((typeKey ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "character":
            {
                // A character has no description field, so the text becomes a
                // section — the same place the Codex editor would put it.
                var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                var data = new CharacterData
                {
                    Name = parts.Length > 0 ? parts[0] : trimmed,
                    Surname = parts.Length > 1 ? parts[1] : string.Empty,
                };
                if (!string.IsNullOrWhiteSpace(description))
                    data.Sections.Add(new EntitySection { Title = "Notes", Content = description });
                await _entityService.SaveCharacterAsync(data);
                return data.Id;
            }
            case "location":
            {
                var data = new LocationData { Name = trimmed, Description = description ?? string.Empty };
                await _entityService.SaveLocationAsync(data);
                return data.Id;
            }
            case "item":
            {
                var data = new ItemData { Name = trimmed, Description = description ?? string.Empty };
                await _entityService.SaveItemAsync(data);
                return data.Id;
            }
            case "lore":
            {
                var data = new LoreData { Name = trimmed, Description = description ?? string.Empty };
                await _entityService.SaveLoreAsync(data);
                return data.Id;
            }
            default:
            {
                // Fall through to a registered custom type; unknown keys return
                // null rather than silently creating the wrong kind of entry.
                var types = _entityService.GetCustomEntityTypes();
                var match = types.FirstOrDefault(t =>
                    string.Equals(t.TypeKey, typeKey, StringComparison.OrdinalIgnoreCase));
                if (match == null) return null;

                var data = new CustomEntityData { Name = trimmed, EntityTypeKey = match.TypeKey };
                if (!string.IsNullOrWhiteSpace(description))
                    data.Sections = [new EntitySection { Title = "Notes", Content = description }];
                await _entityService.SaveCustomEntityAsync(data);
                return data.Id;
            }
        }
    }

    /// <summary>
    /// The entries this scene may put in front of a model. The host resolves
    /// which entries the scene mentions and applies the writer's inclusion
    /// setting, so an extension cannot leak an entry marked "never" by
    /// assembling context from the raw lists instead.
    /// </summary>
    async Task<IReadOnlyList<Sdk.Services.AiContextEntryInfo>>
        IExtensionEntityService.GetAiContextAsync(string chapterGuid, string sceneId)
    {
        var chapter = _projectService.GetChaptersOrdered()
            .FirstOrDefault(c => c.Guid == chapterGuid);
        var scene = chapter == null
            ? null
            : _projectService.GetScenesForChapter(chapterGuid).FirstOrDefault(s => s.Id == sceneId);
        if (chapter == null || scene == null) return [];

        var html = await _projectService.ReadSceneContentAsync(chapter, scene);
        var prose = Core.Utilities.TextDiff.StripHtml(html);

        var characters = await _entityService.LoadCharactersAsync();
        var locations = await _entityService.LoadLocationsAsync();
        var items = await _entityService.LoadItemsAsync();
        var lore = await _entityService.LoadLoreAsync();

        var all = new List<Core.Services.AiContextEntry>();
        all.AddRange(characters.Select(c => new Core.Services.AiContextEntry(
            c.Id, "character", EntityResolveIndex.Compose(c.Name, c.Surname), c.Ai, c.Sections)));
        all.AddRange(locations.Select(l => new Core.Services.AiContextEntry(
            l.Id, "location", l.Name, l.Ai, l.Sections)));
        all.AddRange(items.Select(i => new Core.Services.AiContextEntry(
            i.Id, "item", i.Name, i.Ai, i.Sections)));
        all.AddRange(lore.Select(lo => new Core.Services.AiContextEntry(
            lo.Id, "lore", lo.Name, lo.Ai, lo.Sections)));
        foreach (var type in _entityService.GetCustomEntityTypes())
        {
            var entities = await _entityService.LoadCustomEntitiesAsync(type.TypeKey);
            all.AddRange(entities.Select(ce => new Core.Services.AiContextEntry(
                ce.Id, type.TypeKey, ce.Name, ce.Ai, ce.Sections)));
        }

        // "Mentioned" is a plain name match against the scene's prose. It is
        // deliberately generous: a near-miss that includes one extra entry is a
        // better failure than one that withholds context the model needed.
        var mentioned = all
            .Where(e => e.Name.Length > 0
                        && prose.Contains(e.Name, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Id)
            .ToHashSet(StringComparer.Ordinal);

        return [.. Core.Services.AiContextPolicy.Allowed(all, mentioned)
            .Select(e => new Sdk.Services.AiContextEntryInfo
            {
                Id = e.Id,
                TypeKey = e.TypeKey,
                Name = e.Name,
                Inclusion = e.Inclusion.ToString(),
                Sections = [.. e.Sections.Select(sec => new Sdk.Services.AiContextSectionInfo
                {
                    Title = sec.Title,
                    Content = sec.Content
                })]
            })];
    }

    void IExtensionEntityService.RequestEntityRefresh()
    {
        EntityRefreshRequested?.Invoke();
    }

    /// <summary>Internal event for entity refresh requests from extensions.</summary>
    internal event Action? EntityRefreshRequested;

    List<string> IExtensionEntityService.GetProjectImages() => _entityService.GetProjectImages();
    string IExtensionEntityService.GetImageFullPath(string relativePath) => _entityService.GetImageFullPath(relativePath);

    async Task<string?> IExtensionEntityService.GetCharacterImagePathAsync(string characterId, string? chapterGuid, string? sceneId)
    {
        var characters = await _entityService.LoadCharactersAsync();
        var character = characters.FirstOrDefault(c => string.Equals(c.Id, characterId, StringComparison.OrdinalIgnoreCase));
        if (character == null) return null;

        // Resolve chapter title + scene title for override matching (Scene
        // field on overrides is stored as title, not id).
        Novalist.Core.Models.ChapterData? chapter = null;
        Novalist.Core.Models.SceneData? scene = null;
        if (!string.IsNullOrEmpty(chapterGuid))
        {
            chapter = _projectService.GetChaptersOrdered()
                .FirstOrDefault(c => string.Equals(c.Guid, chapterGuid, StringComparison.OrdinalIgnoreCase));
            if (chapter != null && !string.IsNullOrEmpty(sceneId))
            {
                scene = _projectService.GetScenesForChapter(chapter.Guid)
                    .FirstOrDefault(s => string.Equals(s.Id, sceneId, StringComparison.OrdinalIgnoreCase));
            }
        }

        bool ChapterMatches(Novalist.Core.Models.CharacterOverride o)
            => chapter != null
               && (string.Equals(o.Chapter, chapter.Guid, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(o.Chapter, chapter.Title, StringComparison.OrdinalIgnoreCase));

        // Prefer override that matches both chapter AND scene; then chapter-only.
        Novalist.Core.Models.CharacterOverride? match = null;
        if (chapter != null && scene != null)
        {
            match = character.ChapterOverrides.FirstOrDefault(o =>
                ChapterMatches(o)
                && !string.IsNullOrWhiteSpace(o.Scene)
                && string.Equals(o.Scene, scene.Title, StringComparison.OrdinalIgnoreCase));
        }
        match ??= chapter == null
            ? null
            : character.ChapterOverrides.FirstOrDefault(o => ChapterMatches(o) && string.IsNullOrWhiteSpace(o.Scene));

        var images = match?.Images ?? character.Images;
        if (images == null || images.Count == 0)
            images = character.Images;

        var first = images.FirstOrDefault(img => !string.IsNullOrWhiteSpace(img.Path));
        if (first == null) return null;

        var abs = _entityService.GetImageFullPath(first.Path);
        return string.IsNullOrEmpty(abs) ? null : abs;
    }

    async Task<Sdk.Services.CharacterDetailedInfo?> IExtensionEntityService.GetCharacterDetailedAsync(string characterId, string? chapterGuid, string? sceneId)
    {
        var characters = await _entityService.LoadCharactersAsync();
        var character = characters.FirstOrDefault(c => string.Equals(c.Id, characterId, StringComparison.OrdinalIgnoreCase));
        if (character == null) return null;

        Novalist.Core.Models.ChapterData? chapter = null;
        Novalist.Core.Models.SceneData? scene = null;
        if (!string.IsNullOrEmpty(chapterGuid))
        {
            chapter = _projectService.GetChaptersOrdered()
                .FirstOrDefault(c => string.Equals(c.Guid, chapterGuid, StringComparison.OrdinalIgnoreCase));
            if (chapter != null && !string.IsNullOrEmpty(sceneId))
            {
                scene = _projectService.GetScenesForChapter(chapter.Guid)
                    .FirstOrDefault(s => string.Equals(s.Id, sceneId, StringComparison.OrdinalIgnoreCase));
            }
        }

        bool ChapterMatches(Novalist.Core.Models.CharacterOverride o)
            => chapter != null
               && (string.Equals(o.Chapter, chapter.Guid, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(o.Chapter, chapter.Title, StringComparison.OrdinalIgnoreCase));

        bool ActMatches(Novalist.Core.Models.CharacterOverride o)
            => chapter != null
               && !string.IsNullOrWhiteSpace(o.Act)
               && !string.IsNullOrWhiteSpace(chapter.Act)
               && string.Equals(o.Act, chapter.Act, StringComparison.OrdinalIgnoreCase);

        // Resolution order: scene-scoped override → chapter-scoped override → act-scoped override.
        Novalist.Core.Models.CharacterOverride? sceneOverride = null;
        Novalist.Core.Models.CharacterOverride? chapterOverride = null;
        Novalist.Core.Models.CharacterOverride? actOverride = null;

        if (scene != null)
        {
            sceneOverride = character.ChapterOverrides.FirstOrDefault(o =>
                ChapterMatches(o)
                && !string.IsNullOrWhiteSpace(o.Scene)
                && string.Equals(o.Scene, scene.Title, StringComparison.OrdinalIgnoreCase));
        }
        chapterOverride = character.ChapterOverrides.FirstOrDefault(o =>
            ChapterMatches(o) && string.IsNullOrWhiteSpace(o.Scene));
        actOverride = character.ChapterOverrides.FirstOrDefault(o =>
            ActMatches(o) && string.IsNullOrWhiteSpace(o.Scene) && string.IsNullOrWhiteSpace(o.Chapter));

        // Per-field resolution: scene > chapter > act > base.
        string Pick(Func<Novalist.Core.Models.CharacterOverride?, string?> selector, string baseValue)
        {
            var v = selector(sceneOverride);
            if (!string.IsNullOrWhiteSpace(v)) return v!;
            v = selector(chapterOverride);
            if (!string.IsNullOrWhiteSpace(v)) return v!;
            v = selector(actOverride);
            if (!string.IsNullOrWhiteSpace(v)) return v!;
            return baseValue;
        }

        var relationships = sceneOverride?.Relationships
                            ?? chapterOverride?.Relationships
                            ?? actOverride?.Relationships
                            ?? character.Relationships;
        var customProps = sceneOverride?.CustomProperties
                          ?? chapterOverride?.CustomProperties
                          ?? actOverride?.CustomProperties
                          ?? character.CustomProperties;
        var sections = sceneOverride?.Sections
                       ?? chapterOverride?.Sections
                       ?? actOverride?.Sections
                       ?? character.Sections;

        var resolvedFrom = sceneOverride != null ? sceneOverride.ScopeLabel
            : chapterOverride != null ? chapterOverride.ScopeLabel
            : actOverride != null ? actOverride.ScopeLabel
            : string.Empty;

        return new Sdk.Services.CharacterDetailedInfo
        {
            Id = character.Id,
            DisplayName = character.DisplayName,
            Name = Pick(o => o?.Name, character.Name),
            Surname = Pick(o => o?.Surname, character.Surname),
            Aliases = [],
            Age = Pick(o => o?.Age, character.Age),
            Gender = Pick(o => o?.Gender, character.Gender),
            Role = Pick(o => o?.Role, character.Role),
            Group = character.Group,
            EyeColor = Pick(o => o?.EyeColor, character.EyeColor),
            HairColor = Pick(o => o?.HairColor, character.HairColor),
            HairLength = Pick(o => o?.HairLength, character.HairLength),
            Height = Pick(o => o?.Height, character.Height),
            Build = Pick(o => o?.Build, character.Build),
            SkinTone = Pick(o => o?.SkinTone, character.SkinTone),
            DistinguishingFeatures = Pick(o => o?.DistinguishingFeatures, character.DistinguishingFeatures),
            CustomProperties = customProps?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? new Dictionary<string, string>(),
            Relationships = (relationships ?? []).Select(r => new Sdk.Services.CharacterRelationshipInfo
            {
                Role = r.Role ?? string.Empty,
                TargetName = r.Target ?? string.Empty,
                Note = string.Empty,
            }).ToList(),
            Sections = (sections ?? []).Select(s => new Sdk.Services.CharacterSectionInfo
            {
                Title = s.Title ?? string.Empty,
                Content = s.Content ?? string.Empty,
            }).ToList(),
            ResolvedFromScope = resolvedFrom ?? string.Empty,
        };
    }
}
