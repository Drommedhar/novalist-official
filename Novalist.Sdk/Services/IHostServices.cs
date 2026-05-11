using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;

namespace Novalist.Sdk.Services;

/// <summary>
/// Read-only file system operations exposed to extensions.
/// </summary>
public interface IExtensionFileService
{
    Task<string> ReadTextAsync(string path);
    Task WriteTextAsync(string path, string content);
    Task<bool> ExistsAsync(string path);
    Task<bool> DirectoryExistsAsync(string path);
    Task CreateDirectoryAsync(string path);
    Task<IReadOnlyList<string>> GetFilesAsync(string directory, string pattern = "*", bool recursive = false);
    Task<IReadOnlyList<string>> GetDirectoriesAsync(string directory);
    string CombinePath(params string[] parts);
    string GetFileName(string path);
    string GetFileNameWithoutExtension(string path);
    string GetDirectoryName(string path);
}

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

    /// <summary>Get chapters in order.</summary>
    IReadOnlyList<ChapterInfo> GetChaptersOrdered();

    /// <summary>Get scenes for a chapter.</summary>
    IReadOnlyList<SceneInfo> GetScenesForChapter(string chapterGuid);

    /// <summary>The scene currently open in the editor, or null when none.
    /// Updated by the host before the <see cref="IHostServices.SceneOpened"/>
    /// event fires.</summary>
    SceneInfo? CurrentScene { get; }
}

/// <summary>
/// Read-only entity access exposed to extensions.
/// </summary>
public interface IExtensionEntityService
{
    Task<IReadOnlyList<CharacterInfo>> LoadCharactersAsync();
    Task<IReadOnlyList<LocationInfo>> LoadLocationsAsync();
    Task<IReadOnlyList<ItemInfo>> LoadItemsAsync();
    Task<IReadOnlyList<LoreInfo>> LoadLoreAsync();
    Task<IReadOnlyList<CustomEntityInfo>> LoadCustomEntitiesAsync(string typeKey);

    /// <summary>Returns all registered custom entity type keys and display names.</summary>
    IReadOnlyList<CustomEntityTypeInfo> GetCustomEntityTypes();

    /// <summary>Saves a custom entity to the active book. The entity type must be registered.</summary>
    Task SaveCustomEntityAsync(CustomEntityInfo entity);

    /// <summary>Notifies the host that entities have changed and the UI should refresh.</summary>
    void RequestEntityRefresh();

    List<string> GetProjectImages();
    string GetImageFullPath(string relativePath);

    /// <summary>
    /// Returns the absolute filesystem path of the character's image as it
    /// applies in the given chapter/scene context, walking per-chapter /
    /// per-scene overrides before falling back to the default. Returns null
    /// when the character has no image. The host owns the override
    /// resolution so extensions stay opaque to chapter/scene/act fall-back
    /// rules.
    /// </summary>
    Task<string?> GetCharacterImagePathAsync(string characterId, string? chapterGuid, string? sceneId);

    /// <summary>
    /// Returns the character's full profile resolved for the given chapter/scene
    /// context. Per-scene overrides take precedence over per-chapter, which take
    /// precedence over per-act, which fall back to the base profile. Returns
    /// null when the character does not exist.
    /// </summary>
    Task<CharacterDetailedInfo?> GetCharacterDetailedAsync(string characterId, string? chapterGuid, string? sceneId);
}

/// <summary>
/// Facade that exposes host application services to extensions.
/// Extensions receive this in Initialize() and use it throughout their lifetime.
/// </summary>
public interface IHostServices
{
    /// <summary>File I/O operations.</summary>
    IExtensionFileService FileService { get; }

    /// <summary>Project data access.</summary>
    IExtensionProjectService ProjectService { get; }

    /// <summary>Entity data access.</summary>
    IExtensionEntityService EntityService { get; }

    /// <summary>Current host version.</summary>
    string HostVersion { get; }

    /// <summary>
    /// Returns the path to this extension's data folder within the project
    /// (.novalist/extensions/{extensionId}/).
    /// Creates the folder if it doesn't exist.
    /// </summary>
    string GetExtensionDataPath(string extensionId);

    /// <summary>
    /// Returns the path to this extension's global settings folder
    /// (%APPDATA%/Novalist/extensions/{extensionId}/).
    /// Creates the folder if it doesn't exist.
    /// </summary>
    string GetExtensionSettingsPath(string extensionId);

    /// <summary>Post an action to the UI thread.</summary>
    void PostToUI(Action action);

    /// <summary>Current UI language code (e.g. "en", "de").</summary>
    string CurrentLanguage { get; }

    /// <summary>
    /// Returns the localization service for the given extension.
    /// The service loads JSON locale files from the extension's <c>Locales/</c> folder
    /// and resolves keys with English fallback.
    /// </summary>
    IExtensionLocalization GetLocalization(string extensionId);

    /// <summary>Show a toast notification to the user.</summary>
    void ShowNotification(string message);

    /// <summary>Activate an extension content view by its ViewKey.</summary>
    void ActivateContentView(string viewKey);

    /// <summary>
    /// Toggle a right-side sidebar panel by its panel ID.
    /// If the panel is already visible it will be hidden; otherwise it becomes visible.
    /// </summary>
    void ToggleRightSidebar(string panelId);

    /// <summary>Register an editor extension hook.</summary>
    void RegisterEditorExtension(IEditorExtension extension);

    /// <summary>Unregister an editor extension hook.</summary>
    void UnregisterEditorExtension(IEditorExtension extension);

    /// <summary>Dynamically register a keyboard shortcut at runtime.</summary>
    void RegisterHotkey(HotkeyDescriptor descriptor);

    /// <summary>Remove a previously registered keyboard shortcut.</summary>
    void UnregisterHotkey(string actionId);

    /// <summary>Fired when a project is loaded.</summary>
    event Action<ProjectInfo>? ProjectLoaded;

    /// <summary>Fired when a scene is opened in the editor.</summary>
    event Action<SceneInfo>? SceneOpened;

    /// <summary>Fired when a scene is saved.</summary>
    event Action<SceneInfo>? SceneSaved;

    /// <summary>Fired when the active book changes.</summary>
    event Action<BookInfo>? BookChanged;

    /// <summary>Fired when the application language changes.</summary>
    event Action<string>? LanguageChanged;

    /// <summary>
    /// Returns all AI hooks registered by other extensions.
    /// Useful for extensions that implement an AI provider and need to
    /// invoke other extensions' prompt contributions and response filters.
    /// </summary>
    IReadOnlyList<IAiHook> GetAiHooks();

    /// <summary>
    /// Returns the display name of the current UI language (e.g. "English", "Deutsch").
    /// </summary>
    string CurrentLanguageDisplayName { get; }

    /// <summary>
    /// Reads a named JSON section from the host settings.
    /// Returns null if the key is not recognized.
    /// </summary>
    string? ReadHostData(string key);

    /// <summary>
    /// Writes a named JSON section to the host settings and persists the change.
    /// </summary>
    Task WriteHostDataAsync(string key, string json);
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

/// <summary>Lightweight character info for read-only access.</summary>
public sealed class CharacterInfo
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public List<string> Aliases { get; init; } = [];
}

/// <summary>
/// Rich character info with all profile data, resolved for a specific
/// chapter/scene context (per-scene → per-chapter → per-act override fallback
/// applied by the host before return). Extensions never see raw override
/// lists; they get the effective view for the requested context.
/// </summary>
public sealed class CharacterDetailedInfo
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Surname { get; init; } = string.Empty;
    public List<string> Aliases { get; init; } = [];
    public string Age { get; init; } = string.Empty;
    public string Gender { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
    public string EyeColor { get; init; } = string.Empty;
    public string HairColor { get; init; } = string.Empty;
    public string HairLength { get; init; } = string.Empty;
    public string Height { get; init; } = string.Empty;
    public string Build { get; init; } = string.Empty;
    public string SkinTone { get; init; } = string.Empty;
    public string DistinguishingFeatures { get; init; } = string.Empty;
    public Dictionary<string, string> CustomProperties { get; init; } = new();
    public List<CharacterRelationshipInfo> Relationships { get; init; } = [];
    public List<CharacterSectionInfo> Sections { get; init; } = [];

    /// <summary>Scope label of the override that produced the resolved view
    /// (e.g. "Scene: 04 - Bridge"). Empty when the base profile was used.</summary>
    public string ResolvedFromScope { get; init; } = string.Empty;
}

public sealed class CharacterRelationshipInfo
{
    public string Role { get; init; } = string.Empty;
    public string TargetName { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
}

public sealed class CharacterSectionInfo
{
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
}

/// <summary>Lightweight location info for read-only access.</summary>
public sealed class LocationInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
}

/// <summary>Lightweight item info for read-only access.</summary>
public sealed class ItemInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
}

/// <summary>Lightweight lore info for read-only access.</summary>
public sealed class LoreInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
}

/// <summary>Custom entity info for reading and writing.</summary>
public sealed class CustomEntityInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string EntityTypeKey { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<CustomEntitySectionInfo>? Sections { get; init; }
}

/// <summary>A section of rich-text content within a custom entity.</summary>
public sealed class CustomEntitySectionInfo
{
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
}

/// <summary>Describes a registered custom entity type.</summary>
public sealed class CustomEntityTypeInfo
{
    public string TypeKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string DisplayNamePlural { get; init; } = string.Empty;
    public string Icon { get; init; } = "📋";
}
