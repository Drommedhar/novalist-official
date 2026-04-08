using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using Novalist.Core;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Localization;
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

    public void ActivateContentView(string viewKey)
    {
        System.Diagnostics.Debug.WriteLine($"[ExtCtxMenu] ActivateContentView called with '{viewKey}', handler null? {ContentViewActivated is null}");
        ContentViewActivated?.Invoke(viewKey);
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
        return key switch
        {
            "ai" => JsonSerializer.Serialize(_settingsService.Settings.Ai),
            _ => null
        };
    }

    public async Task WriteHostDataAsync(string key, string json)
    {
        if (key == "ai")
        {
            var settings = JsonSerializer.Deserialize<AiSettings>(json);
            if (settings != null)
            {
                _settingsService.Settings.Ai = settings;
                await _settingsService.SaveAsync();
            }
        }
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
    /// <summary>Internal event for content view activation requests.</summary>
    internal event Action<string>? ContentViewActivated;
    /// <summary>Internal event for right sidebar toggle requests.</summary>
    internal event Action<string>? RightSidebarToggled;

    // ── Event raising (called by ExtensionManager when host events occur) ──

    internal void RaiseProjectLoaded(string name, string rootPath)
    {
        ProjectLoaded?.Invoke(new Sdk.Services.ProjectInfo { Name = name, RootPath = rootPath });
    }

    internal void RaiseSceneOpened(string id, string title, string chapterGuid, string chapterTitle, int wordCount)
    {
        SceneOpened?.Invoke(new Sdk.Services.SceneInfo
        {
            Id = id, Title = title, ChapterGuid = chapterGuid,
            ChapterTitle = chapterTitle, WordCount = wordCount
        });
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

    List<string> IExtensionEntityService.GetProjectImages() => _entityService.GetProjectImages();
    string IExtensionEntityService.GetImageFullPath(string relativePath) => _entityService.GetImageFullPath(relativePath);
}
