using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sdk;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;

namespace Novalist.Backend.Extensions;

/// <summary>
/// Manages the lifecycle of all extensions: discovery, loading, initialization,
/// hook collection, enable/disable, and shutdown.
/// </summary>
public sealed class ExtensionManager
{
    private readonly ExtensionLoader _loader;
    private readonly ISettingsService _settingsService;
    private readonly HostServices _hostServices;

    public ObservableCollection<ExtensionInfo> Extensions { get; } = [];

    // ── Hook collections (populated during loading) ─────────────────

    public List<RibbonItem> RibbonItems { get; } = [];
    public List<SidebarPanel> SidebarPanels { get; } = [];
    public List<StatusBarItem> StatusBarItems { get; } = [];
    public List<ContextMenuItem> ContextMenuItems { get; } = [];
    public List<ContentViewDescriptor> ContentViews { get; } = [];
    public List<SettingsPage> SettingsPages { get; } = [];
    public List<Novalist.Sdk.Models.Wizards.WizardDefinition> Wizards { get; } = [];
    public List<EntityTypeDescriptor> EntityTypes { get; } = [];
    public List<ExportFormatDescriptor> ExportFormats { get; } = [];
    public List<IAiHook> AiHooks { get; } = [];
    public List<IGrammarCheckContributor> GrammarCheckContributors { get; } = [];
    public List<ThemeOverride> ThemeOverrides { get; } = [];
    public List<HotkeyDescriptor> HotkeyBindings { get; } = [];
    public List<PropertyTypeDescriptor> PropertyTypes { get; } = [];

    // Per-extension "un-collect" actions capturing the exact hook instances added.
    // Hook contributors may return fresh instances on each GetXxx() call, so
    // removal must target the originally-collected references, not a second call.
    private readonly Dictionary<ExtensionInfo, List<Action>> _hookUndo = new();

    /// <param name="loader">Extension loader; defaults to one scanning %APPDATA%/Novalist/Extensions. Tests inject one pointing at a temp dir.</param>
    public ExtensionManager(ISettingsService settingsService, HostServices hostServices, ExtensionLoader? loader = null)
    {
        _settingsService = settingsService;
        _hostServices = hostServices;
        _loader = loader ?? new ExtensionLoader();
    }

    /// <summary>
    /// Discovers, loads, and initializes all enabled extensions.
    /// </summary>
    public async Task LoadAllAsync()
    {
        var discovered = _loader.DiscoverExtensions();
        var enabledMap = _settingsService.Settings.Extensions;

        foreach (var info in discovered)
        {
            // Idempotent: a repeated LoadAllAsync (e.g. on RPC reconnect / re-hydrate)
            // must not add the same extension again, which produced duplicate list
            // rows and duplicate hook registrations.
            if (Extensions.Any(e =>
                    string.Equals(e.Manifest.Id, info.Manifest.Id, StringComparison.OrdinalIgnoreCase)))
                continue;

            // Check enable/disable state (default: enabled)
            if (enabledMap.TryGetValue(info.Manifest.Id, out var enabled))
                info.IsEnabled = enabled;
            else
                info.IsEnabled = true;

            Extensions.Add(info);

            if (!info.IsEnabled)
                continue;

            if (!_loader.LoadExtension(info))
            {
                if (!string.IsNullOrWhiteSpace(info.LoadError))
                    HostNotifications.Error?.Invoke($"Extension load failed: {info.Manifest.Name}: {info.LoadError}");
                continue;
            }

            InitializeExtension(info);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Initializes a loaded extension: calls Initialize and collects hooks.
    /// </summary>
    private void InitializeExtension(ExtensionInfo info)
    {
        // Only ever called after a successful LoadExtension, so Instance is set.
        try
        {
            // Register locale folder so GetLocalization() works during Initialize
            var localesDir = System.IO.Path.Combine(info.FolderPath, "Locales");
            _hostServices.RegisterExtensionLocales(info.Manifest.Id, localesDir);

            info.Instance!.Initialize(_hostServices);
            CollectHooks(info);
        }
        catch (Exception ex)
        {
            info.LoadError = $"Initialize failed: {ex.Message}";
            info.IsLoaded = false;
            HostNotifications.Error?.Invoke($"Extension init failed: {info.Manifest.Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Collects hooks by inspecting the extension instance for hook interfaces.
    /// </summary>
    private void CollectHooks(ExtensionInfo info)
    {
        // Called only from InitializeExtension after a successful load.
        var instance = info.Instance!;

        var undo = new List<Action>();

        // Helper: add the collected items to a target list and record the matching
        // removal of those exact references.
        void AddList<T>(List<T> target, IReadOnlyList<T> items)
        {
            target.AddRange(items);
            undo.Add(() => { foreach (var i in items) target.Remove(i); });
        }

        if (instance is IRibbonContributor ribbon)
            AddList(RibbonItems, ribbon.GetRibbonItems());

        if (instance is ISidebarContributor sidebar)
            AddList(SidebarPanels, sidebar.GetSidebarPanels());

        if (instance is IStatusBarContributor statusBar)
            AddList(StatusBarItems, statusBar.GetStatusBarItems());

        if (instance is IContextMenuContributor contextMenu)
            AddList(ContextMenuItems, contextMenu.GetContextMenuItems());

        if (instance is IContentViewContributor contentView)
            AddList(ContentViews, contentView.GetContentViews());

        if (instance is ISettingsContributor settings)
            AddList(SettingsPages, settings.GetSettingsPages());

        if (instance is IWizardContributor wizardContributor)
            AddList(Wizards, wizardContributor.GetWizards());

        if (instance is IEntityTypeContributor entityType)
            AddList(EntityTypes, entityType.GetEntityTypes());

        if (instance is IExportFormatContributor exportFormat)
            AddList(ExportFormats, exportFormat.GetExportFormats());

        if (instance is IAiHook aiHook)
        {
            AiHooks.Add(aiHook);
            undo.Add(() => AiHooks.Remove(aiHook));
        }

        if (instance is IGrammarCheckContributor grammarCheck)
        {
            GrammarCheckContributors.Add(grammarCheck);
            undo.Add(() => GrammarCheckContributors.Remove(grammarCheck));
        }

        if (instance is IThemeContributor theme)
            AddList(ThemeOverrides, theme.GetThemeOverrides());

        if (instance is IHotkeyContributor hotkey)
        {
            var bindings = hotkey.GetHotkeyBindings();
            HotkeyBindings.AddRange(bindings);
            HotkeyRegistry.RegisterRange(bindings);
            undo.Add(() =>
            {
                foreach (var b in bindings)
                {
                    HotkeyBindings.Remove(b);
                    HotkeyRegistry.Unregister(b.ActionId);
                }
            });
        }

        if (instance is IEditorExtension editorExt)
        {
            _hostServices.RegisterEditorExtension(editorExt);
            undo.Add(() => _hostServices.UnregisterEditorExtension(editorExt));
        }

        if (instance is IPropertyTypeContributor propertyType)
            AddList(PropertyTypes, propertyType.GetPropertyTypes());

        _hookUndo[info] = undo;
    }

    /// <summary>
    /// Removes the hooks contributed by a specific extension, targeting the exact
    /// instances captured during <see cref="CollectHooks"/>.
    /// </summary>
    private void RemoveHooks(ExtensionInfo info)
    {
        if (!_hookUndo.TryGetValue(info, out var undo))
            return;

        foreach (var revert in undo)
            revert();

        _hookUndo.Remove(info);
    }

    /// <summary>
    /// Discovers a newly installed extension by ID, adds it to the Extensions list,
    /// enables it, and loads + initializes it.
    /// </summary>
    public async Task DiscoverAndEnableAsync(string extensionId)
    {
        // Don't duplicate if already known
        if (Extensions.Any(e => string.Equals(e.Manifest.Id, extensionId, StringComparison.OrdinalIgnoreCase)))
            return;

        var discovered = _loader.DiscoverExtensions();
        var info = discovered.FirstOrDefault(e =>
            string.Equals(e.Manifest.Id, extensionId, StringComparison.OrdinalIgnoreCase));

        if (info == null) return;

        info.IsEnabled = true;
        _settingsService.Settings.Extensions[extensionId] = true;
        await _settingsService.SaveAsync();

        Extensions.Add(info);

        if (_loader.LoadExtension(info))
        {
            InitializeExtension(info);
        }
    }

    /// <summary>
    /// Reloads an extension from disk after its files changed underneath us (a
    /// gallery install or update wrote a new version into the extensions folder).
    /// Unloads and drops the currently-loaded instance if present, then
    /// re-discovers, enables, loads, and initializes it from the new files so the
    /// change takes effect without an app restart. No-op when the id is neither
    /// loaded nor present on disk.
    /// </summary>
    public async Task ReloadExtensionAsync(string extensionId)
    {
        var existing = Extensions.FirstOrDefault(e =>
            string.Equals(e.Manifest.Id, extensionId, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            // Unloads the collectible assembly context (memory-loaded, no file
            // locks) so the freshly written DLLs can be loaded below.
            await DisableExtensionAsync(existing.Manifest.Id);
            Extensions.Remove(existing);
        }

        await DiscoverAndEnableAsync(extensionId);
    }

    private static readonly JsonSerializerOptions InstallJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Installs an extension from a source folder: validates its manifest, copies
    /// the folder into the extensions directory (replacing any previous install of
    /// the same id), then discovers, enables, loads, and initializes it.
    /// Returns the installed extension id.
    /// </summary>
    public async Task<string> InstallFromFolderAsync(string sourceFolder)
    {
        if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
            throw new DirectoryNotFoundException("The selected folder does not exist.");

        var manifestPath = Path.Combine(sourceFolder, "extension.json");
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException("The selected folder does not contain an extension.json manifest.");

        ExtensionManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ExtensionManifest>(File.ReadAllText(manifestPath), InstallJsonOptions);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"extension.json could not be parsed: {ex.Message}");
        }

        if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id))
            throw new InvalidOperationException("extension.json is missing a valid \"id\".");

        var id = manifest.Id;
        Log.Info($"Extension install from folder: id={id}.");

        // Replace any previous install of the same id (unload + delete files first).
        await RemoveInstalledAsync(id);

        var target = Path.Combine(_loader.ExtensionsDirectory, SanitizeFolderName(id));
        CopyDirectory(sourceFolder, target);

        await DiscoverAndEnableAsync(id);
        return id;
    }

    /// <summary>
    /// Uninstalls an extension: unloads it if loaded, deletes its folder from disk,
    /// and drops its persisted enable/disable state.
    /// </summary>
    public async Task UninstallAsync(string extensionId)
    {
        Log.Info($"Extension uninstall: id={extensionId}.");
        await RemoveInstalledAsync(extensionId);
        _settingsService.Settings.Extensions.Remove(extensionId);
        await _settingsService.SaveAsync();
    }

    /// <summary>
    /// Unloads (if loaded), removes from the live collection, and deletes the
    /// on-disk folder for the given extension id. No-op when not installed.
    /// </summary>
    private async Task RemoveInstalledAsync(string extensionId)
    {
        var info = Extensions.FirstOrDefault(e =>
            string.Equals(e.Manifest.Id, extensionId, StringComparison.OrdinalIgnoreCase));
        if (info == null)
            return;

        // DisableExtensionAsync unloads the assembly context (memory-loaded, so no
        // file locks are held) and persists the disabled state.
        await DisableExtensionAsync(info.Manifest.Id);
        Extensions.Remove(info);

        if (!string.IsNullOrEmpty(info.FolderPath) && Directory.Exists(info.FolderPath))
            Directory.Delete(info.FolderPath, recursive: true);
    }

    private static string SanitizeFolderName(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(id.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }

    /// <summary>
    /// Enables an extension and persists the state.
    /// Requires app restart to take effect.
    /// </summary>
    public async Task EnableExtensionAsync(string extensionId)
    {
        var info = Extensions.FirstOrDefault(e => e.Manifest.Id == extensionId);
        if (info == null) return;

        info.IsEnabled = true;
        _settingsService.Settings.Extensions[extensionId] = true;
        await _settingsService.SaveAsync();

        // Load and initialize if not already loaded
        if (!info.IsLoaded && _loader.LoadExtension(info))
        {
            InitializeExtension(info);
        }
    }

    /// <summary>
    /// Disables an extension and persists the state.
    /// Shuts down the extension if currently loaded.
    /// </summary>
    public async Task DisableExtensionAsync(string extensionId)
    {
        var info = Extensions.FirstOrDefault(e => e.Manifest.Id == extensionId);
        if (info == null) return;

        info.IsEnabled = false;
        _settingsService.Settings.Extensions[extensionId] = false;
        await _settingsService.SaveAsync();

        if (info.IsLoaded)
        {
            RemoveHooks(info);
            try { info.Instance?.Shutdown(); } catch { /* swallow */ }
            info.Instance = null;
            info.LoadContext?.Unload();
            info.LoadContext = null;
            info.IsLoaded = false;
        }
    }

    /// <summary>
    /// Shuts down all loaded extensions. Called on app exit.
    /// </summary>
    public void ShutdownAll()
    {
        foreach (var info in Extensions.Where(e => e.IsLoaded))
        {
            try
            {
                RemoveHooks(info);
                info.Instance?.Shutdown();
            }
            catch { /* swallow — never let an extension crash shutdown */ }
            info.Instance = null;
            info.LoadContext?.Unload();
            info.LoadContext = null;
            info.IsLoaded = false;
        }
    }

    /// <summary>
    /// Enumerates every contributed settings page together with its owning
    /// extension id, for the renderer's extension-settings surface.
    /// </summary>
    public IEnumerable<(string ExtensionId, string ExtensionName, SettingsPage Page)> EnumerateSettingsPages()
    {
        foreach (var e in Extensions.Where(e => e.IsLoaded && e.Instance is ISettingsContributor))
        {
            foreach (var page in ((ISettingsContributor)e.Instance!).GetSettingsPages())
                yield return (e.Manifest.Id, e.Manifest.Name, page);
        }
    }

    /// <summary>
    /// Enumerates every contributed wizard together with its owning extension id.
    /// </summary>
    public IEnumerable<(string ExtensionId, string ExtensionName, Novalist.Sdk.Models.Wizards.WizardDefinition Def)> EnumerateWizards()
    {
        foreach (var e in Extensions.Where(e => e.IsLoaded && e.Instance is IWizardContributor))
        {
            foreach (var def in ((IWizardContributor)e.Instance!).GetWizards())
                yield return (e.Manifest.Id, e.Manifest.Name, def);
        }
    }

    /// <summary>Finds a contributed wizard definition by extension + wizard id, or
    /// null when not found. Returns a freshly built definition whose runtime
    /// callbacks are live.</summary>
    public Novalist.Sdk.Models.Wizards.WizardDefinition? FindWizard(string extensionId, string wizardId)
    {
        var extension = Extensions.FirstOrDefault(e =>
            e.IsLoaded && e.Manifest.Id == extensionId && e.Instance is IWizardContributor);
        if (extension?.Instance is not IWizardContributor contributor)
            return null;
        return contributor.GetWizards().FirstOrDefault(w => w.Id == wizardId);
    }

    // ── Contribution surfacing + execution (Electron host bridges) ───────
    // These enumerate the live contributor instances (so runtime closures stay
    // valid) and assign each surfaced item a stable, opaque id the renderer
    // echoes back to invoke the matching callback across the RPC boundary.

    /// <summary>Extension-contributed inline actions, flattened across all
    /// registered <see cref="IInlineActionContributor"/>s and ordered by
    /// priority (lower first), matching the desktop editor's ordering.</summary>
    public IReadOnlyList<Novalist.Sdk.Hooks.InlineActionDescriptor> GetInlineActionDescriptors()
        => _hostServices.GetInlineActionContributors()
            .SelectMany(c => c.GetInlineActions())
            .OrderBy(a => a.Priority)
            .ToList();

    /// <summary>Runs the inline action with the given id against the request, by
    /// locating the contributor that owns it. Null when no contributor claims the
    /// id.</summary>
    public async Task<Novalist.Sdk.Hooks.InlineActionResult?> ExecuteInlineActionAsync(
        string actionId, Novalist.Sdk.Hooks.InlineActionRequest request, CancellationToken cancellationToken)
    {
        foreach (var contributor in _hostServices.GetInlineActionContributors())
        {
            if (contributor.GetInlineActions().Any(a => a.Id == actionId))
                return await contributor.ExecuteAsync(actionId, request, cancellationToken);
        }
        return null;
    }

    /// <summary>Enumerates contributed context-menu items with a stable id
    /// ("{extensionId}#ctx#{index}") for round-trip execution.</summary>
    public IEnumerable<(string Id, string ExtensionId, ContextMenuItem Item)> EnumerateContextMenuItemsWithIds()
    {
        foreach (var e in Extensions.Where(e => e.IsLoaded && e.Instance is IContextMenuContributor))
        {
            var items = ((IContextMenuContributor)e.Instance!).GetContextMenuItems();
            for (var i = 0; i < items.Count; i++)
                yield return ($"{e.Manifest.Id}#ctx#{i}", e.Manifest.Id, items[i]);
        }
    }

    /// <summary>Invokes a contributed context-menu item's click handler with the
    /// supplied context object (respecting its visibility guard).</summary>
    public void ExecuteContextMenuItem(string id, object? context)
    {
        var match = EnumerateContextMenuItemsWithIds().FirstOrDefault(x => x.Id == id).Item;
        if (match == null) return;
        if (match.IsVisible != null && !match.IsVisible(context)) return;
        match.OnClick?.Invoke(context);
    }

    /// <summary>Enumerates contributed status-bar items with a stable id
    /// ("{extensionId}#sb#{itemId}") plus their current text/tooltip.</summary>
    public IEnumerable<(string Id, string ExtensionId, StatusBarItem Item)> EnumerateStatusBarItemsWithIds()
    {
        foreach (var e in Extensions.Where(e => e.IsLoaded && e.Instance is IStatusBarContributor))
        {
            foreach (var item in ((IStatusBarContributor)e.Instance!).GetStatusBarItems())
                yield return ($"{e.Manifest.Id}#sb#{item.Id}", e.Manifest.Id, item);
        }
    }

    /// <summary>Invokes a contributed status-bar item's click handler.</summary>
    public void ExecuteStatusBarItem(string id)
    {
        var match = EnumerateStatusBarItemsWithIds().FirstOrDefault(x => x.Id == id).Item;
        match?.OnClick?.Invoke();
    }

    /// <summary>Enumerates contributed theme overrides with their owning extension
    /// id. Only the portable <see cref="ThemeOverride.AccentColor"/> is meaningful
    /// on the Electron host; Avalonia <c>Styles</c>/<c>ResourcePath</c> are ignored.</summary>
    public IEnumerable<(string ExtensionId, ThemeOverride Theme)> EnumerateThemes()
    {
        foreach (var e in Extensions.Where(e => e.IsLoaded && e.Instance is IThemeContributor))
        {
            foreach (var theme in ((IThemeContributor)e.Instance!).GetThemeOverrides())
                yield return (e.Manifest.Id, theme);
        }
    }

    /// <summary>Enumerates contributors of a declarative settings schema.</summary>
    public IEnumerable<(string ExtensionId, string ExtensionName, Novalist.Sdk.Hooks.ISettingsSchemaContributor Contributor)> EnumerateSettingsSchemas()
    {
        foreach (var e in Extensions.Where(e => e.IsLoaded && e.Instance is Novalist.Sdk.Hooks.ISettingsSchemaContributor))
            yield return (e.Manifest.Id, e.Manifest.Name, (Novalist.Sdk.Hooks.ISettingsSchemaContributor)e.Instance!);
    }

    /// <summary>Applies edited settings values for the schema-contributing
    /// extension with the given id. No-op when the id is unknown.</summary>
    public async Task ApplySettingsSchemaAsync(string extensionId, IReadOnlyDictionary<string, string> values)
    {
        var match = EnumerateSettingsSchemas().FirstOrDefault(x => x.ExtensionId == extensionId);
        if (match.Contributor == null) return;
        await match.Contributor.ApplySettingsAsync(values);
    }

    /// <summary>
    /// Returns the <see cref="HostServices"/> instance for event wiring.
    /// </summary>
    internal HostServices Host => _hostServices;

    /// <summary>The on-disk directory extensions are discovered from and installed into.</summary>
    public string ExtensionsDirectory => _loader.ExtensionsDirectory;
}
