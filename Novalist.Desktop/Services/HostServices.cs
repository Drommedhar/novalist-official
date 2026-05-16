using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Novalist.Core;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Localization;
using Novalist.Desktop.Utilities;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;
using Novalist.Sdk.Services;

namespace Novalist.Desktop.Services;

/// <summary>
/// Concrete implementation of <see cref="IHostServices"/> that wraps the host's
/// static App services and exposes read-only facades to extensions.
/// </summary>
public sealed class HostServices : IHostServices, IExtensionFileService, IExtensionProjectService, IExtensionEntityService
{
    private readonly IFileService _fileService;
    private readonly IProjectService _projectService;
    private readonly IEntityService _entityService;
    private readonly ISettingsService _settingsService;
    private readonly Dictionary<string, ExtensionLocalizationService> _locServices = new(StringComparer.Ordinal);

    /// <summary>Reference to the extension manager (set after construction).</summary>
    internal ExtensionManager? ExtensionManager { get; set; }

    public HostServices(IFileService fileService, IProjectService projectService, IEntityService entityService, ISettingsService settingsService)
    {
        _fileService = fileService;
        _projectService = projectService;
        _entityService = entityService;
        _settingsService = settingsService;
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
        Dispatcher.UIThread.Post(action);
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

        if (Dispatcher.UIThread.CheckAccess())
            return factory(options);

        IBusyProgress? handle = null;
        var done = new System.Threading.ManualResetEventSlim();
        Dispatcher.UIThread.Post(() =>
        {
            try { handle = factory(options); }
            finally { done.Set(); }
        });
        done.Wait();
        return handle ?? new NoopBusyProgress();
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
        Log.Debug($"[ExtCtxMenu] ActivateContentView called with '{viewKey}', handler null? {ContentViewActivated is null}");
        var displayName = ExtensionManager?.ContentViews
            .FirstOrDefault(v => v.ViewKey == viewKey)?.DisplayName ?? viewKey;
        ContentViewActivated?.Invoke(viewKey, displayName);
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
        App.HotkeyService.Register(descriptor);
    }

    public void UnregisterHotkey(string actionId)
    {
        App.HotkeyService.Unregister(actionId);
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
        Log.Debug($"[HostServices] RaiseSceneOpened id={id} title={title} subscribers={SceneOpened?.GetInvocationList().Length ?? 0}");
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
