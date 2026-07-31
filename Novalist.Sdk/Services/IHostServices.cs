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

    /// <summary>
    /// Creates a Codex entry and returns its new id, or null when the type is
    /// unknown or no book is open.
    ///
    /// <paramref name="typeKey"/> is one of the built-in kinds — "character",
    /// "location", "item", "lore" — or a registered custom entity type key.
    /// This is what lets an extension act on something it found in the prose
    /// (a name the Codex does not have yet) instead of only reporting it.
    /// </summary>
    Task<string?> CreateEntityAsync(string typeKey, string name, string description = "");

    /// <summary>
    /// The Codex entries this scene is allowed to put in front of an AI model,
    /// with withheld sections already removed.
    ///
    /// Use this rather than the Load* methods when assembling model context.
    /// Those return everything, because the Codex has to show everything; this
    /// applies the writer's per-entry inclusion setting and their per-section
    /// withholding, which the extension has no way to reconstruct on its own.
    /// A writer who marks an entry "never" means it, and an extension that
    /// assembles context from the raw lists breaks that promise.
    /// </summary>
    /// <param name="chapterGuid">Chapter of the scene the context is for.</param>
    /// <param name="sceneId">Scene the context is for. Entries the scene does
    /// not mention are included only when set to always.</param>
    Task<IReadOnlyList<AiContextEntryInfo>> GetAiContextAsync(string chapterGuid, string sceneId);

    /// <summary>
    /// Writes name, description and sections onto an existing Codex entry of
    /// any kind, built-in or custom. False when the id is unknown.
    ///
    /// CreateEntityAsync could make an entry and nothing could fill one in, so
    /// a questionnaire extension could ask a writer twenty questions about a
    /// character and then had nowhere to put the answers.
    /// </summary>
    /// <param name="sections">
    /// Sections to write. A section whose title already exists is replaced;
    /// anything else is appended. Sections the caller does not mention are left
    /// alone, so filling in one part of an entry does not wipe the rest.
    /// </param>
    Task<bool> SaveEntityAsync(
        string typeKey,
        string entityId,
        string? name = null,
        string? description = null,
        IReadOnlyList<CustomEntitySectionInfo>? sections = null);

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

    /// <summary>Research items, readable and writable.</summary>
    IExtensionResearchService ResearchService { get; }

    /// <summary>Comments and suggested edits on scenes.</summary>
    IExtensionReviewService ReviewService { get; }

    /// <summary>Scene metadata, acts, plot threads and timeline events.</summary>
    IExtensionStoryService StoryService { get; }

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
    /// The language the book is written in, as a BCP-47 tag ("de", "pt-BR").
    ///
    /// Not the same thing as <see cref="CurrentLanguage"/>: someone can read the
    /// menus in English and write in German, and anything that ends up in a file
    /// a reader opens - a language attribute, a document property, a metadata
    /// field - belongs to the book rather than to the interface.
    /// </summary>
    string WritingLanguage { get; }

    /// <summary>
    /// Returns the localization service for the given extension.
    /// The service loads JSON locale files from the extension's <c>Locales/</c> folder
    /// and resolves keys with English fallback.
    /// </summary>
    IExtensionLocalization GetLocalization(string extensionId);

    /// <summary>Show a toast notification to the user.</summary>
    void ShowNotification(string message);

    /// <summary>
    /// Asks the writer to choose a folder, and returns its absolute path, or null
    /// when they cancelled.
    ///
    /// Without this an extension that needs somewhere to write has to ask for a
    /// path as text, which means the writer typing one by hand into a form - and
    /// then finding out it was wrong only once the work has run.
    /// </summary>
    /// <param name="title">What the dialog is for, shown in its title bar.</param>
    Task<string?> PickFolderAsync(string title);

    /// <summary>
    /// Asks the writer to choose a file, and returns its absolute path, or null
    /// when they cancelled. What an importer needs for the same reason.
    /// </summary>
    /// <param name="images">
    /// True to offer only image files. Everything else is offered otherwise -
    /// the host does not take an arbitrary filter list, because a format an
    /// extension invented is not one the dialog knows how to describe.
    /// </param>
    Task<string?> PickFileAsync(string title, bool images = false);

    /// <summary>
    /// Show a busy-progress dialog. Returns a handle that lets the extension
    /// update the status text, progress value, or close the dialog.
    /// Dispose the returned handle to close.
    /// Safe to call from any thread.
    /// </summary>
    IBusyProgress ShowBusyProgress(BusyProgressOptions options);

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

    /// <summary>Register an inline-action contributor (editor context-menu AI / text actions).</summary>
    void RegisterInlineActionContributor(IInlineActionContributor contributor);

    /// <summary>
    /// Runs the supplied <see cref="Novalist.Sdk.Models.Wizards.WizardDefinition"/>
    /// interactively in the host's wizard dialog. Optionally accepts a seed
    /// result whose answers are pre-populated. Returns the completed result,
    /// or <c>null</c> when the user cancelled.
    /// </summary>
    Task<Novalist.Sdk.Models.Wizards.WizardResult?> RunWizardAsync(
        Novalist.Sdk.Models.Wizards.WizardDefinition definition,
        Novalist.Sdk.Models.Wizards.WizardResult? seed = null);

    /// <summary>Unregister a previously registered inline-action contributor.</summary>
    void UnregisterInlineActionContributor(IInlineActionContributor contributor);

    /// <summary>Returns all currently registered inline-action contributors.</summary>
    IReadOnlyList<IInlineActionContributor> GetInlineActionContributors();

    /// <summary>Dynamically register a keyboard shortcut at runtime.</summary>
    void RegisterHotkey(HotkeyDescriptor descriptor);

    /// <summary>Remove a previously registered keyboard shortcut.</summary>
    void UnregisterHotkey(string actionId);

    /// <summary>
    /// Per-scene analysis records: what a pass over a scene found (entities and
    /// their presence, per-character knowledge, findings). The host owns storage,
    /// staleness and the schema; an extension supplies the analysis.
    ///
    /// Read a record to reuse previous work, ask <see cref="IsSceneAnalysisStaleAsync"/>
    /// (or <see cref="GetStaleSceneIdsAsync"/>) to find what still needs doing, and
    /// save one record per scene. Anything cumulative — what a character knows by a
    /// given point — is a roll-up over these records and needs no further model calls.
    /// </summary>
    Task<SceneAnalysisRecord?> GetSceneAnalysisAsync(string sceneId);

    /// <summary>Stores the analysis for one scene, stamped with the hash of the
    /// text it came from so it can be skipped next time.</summary>
    Task SaveSceneAnalysisAsync(SceneAnalysisRecord record, string sceneText);

    /// <summary>Whether a scene still needs analysing — never analysed, text
    /// changed since, or stored under an older schema.</summary>
    Task<bool> IsSceneAnalysisStaleAsync(string sceneId, string sceneText);

    /// <summary>Of the given scenes, the ids still needing analysis.</summary>
    Task<IReadOnlyList<string>> GetStaleSceneIdsAsync(
        IReadOnlyList<SceneTextPair> scenes);

    /// <summary>
    /// The entity ids the writer explicitly `@`-mentioned in a scene, taken from the
    /// mention markers stored in the scene HTML. These are author-confirmed rather
    /// than inferred, so they are the strongest signal available about who a scene
    /// involves — worth handing to a model as known-good context.
    /// </summary>
    Task<IReadOnlyList<string>> GetConfirmedMentionIdsAsync(string chapterGuid, string sceneId);

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
    /// Every command the host or another extension has registered, by id.
    ///
    /// This plus <see cref="InvokeCommandAsync"/> is what a scripting extension
    /// needs to be worth having: a macro that can only call the one extension
    /// hosting it is not automation.
    /// </summary>
    IReadOnlyList<HostCommandInfo> GetCommands();

    /// <summary>
    /// Runs a registered command by id. Returns false when no such command
    /// exists, so a script can check rather than guess.
    /// </summary>
    /// <param name="argumentsJson">
    /// A JSON object of arguments, or null. What a command accepts is described
    /// by its <see cref="HostCommandInfo.ArgumentsSchema"/>.
    /// </param>
    Task<bool> InvokeCommandAsync(string commandId, string? argumentsJson = null);

    /// <summary>
    /// Registers a command other extensions and scripts can invoke. Replaces
    /// any command already registered under the same id.
    /// </summary>
    void RegisterCommand(HostCommandInfo command, Func<string?, Task> handler);

    /// <summary>Removes a command this extension registered.</summary>
    void UnregisterCommand(string commandId);

    /// <summary>
    /// Registers a hook that runs after an export has been written, with the
    /// path of the file. Used for validation and preflight - the check belongs
    /// with whoever knows the format, not in the exporter.
    /// </summary>
    void RegisterExportPostProcessor(Hooks.IExportPostProcessor processor);

    /// <summary>Removes a previously registered export post-processor.</summary>
    void UnregisterExportPostProcessor(Hooks.IExportPostProcessor processor);

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

/// <summary>
/// A Codex entry cleared for an AI model. Its sections are already stripped of
/// anything the writer withheld, so nothing here needs filtering again.
/// </summary>
public sealed class AiContextEntryInfo
{
    public string Id { get; init; } = string.Empty;

    /// <summary>"character", "location", "item", "lore", or a custom type key.</summary>
    public string TypeKey { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>Why this entry is here: "Always" for one the writer pinned into
    /// every scene, "WhenMentioned" for one this scene names.</summary>
    public string Inclusion { get; init; } = string.Empty;

    public IReadOnlyList<AiContextSectionInfo> Sections { get; init; } = [];
}

/// <summary>A section of an entry that the writer allowed through.</summary>
public sealed class AiContextSectionInfo
{
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
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
    /// <summary>
    /// Optional icon name. Empty by default: the icon system is SVG
    /// paths and lucide names, and a pictograph here was the only
    /// emoji in the entity model.
    /// </summary>
    public string Icon { get; init; } = string.Empty;
}
