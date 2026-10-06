using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;

namespace Novalist.Sdk.Services;

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

    /// <summary>Snapshots, other drafts, and the project's files.</summary>
    IExtensionArchiveService ArchiveService { get; }

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

/// <summary>Section included by the host's AI-context policy; sections the writer marked AiHidden have already been removed.</summary>
public sealed class AiContextSectionInfo
{
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
}
