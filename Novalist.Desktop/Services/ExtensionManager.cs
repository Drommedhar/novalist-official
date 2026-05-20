using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sdk;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;

namespace Novalist.Desktop.Services;

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
                    ViewModels.Toast.Show?.Invoke(Localization.Loc.T("toast.extensionLoadFailed", info.Manifest.Name, info.LoadError), ViewModels.ToastSeverity.Error);
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
            ViewModels.Toast.Show?.Invoke(Localization.Loc.T("toast.extensionInitFailed", info.Manifest.Name, ex.Message), ViewModels.ToastSeverity.Error);
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
            App.HotkeyService.RegisterRange(bindings);
            undo.Add(() =>
            {
                foreach (var b in bindings)
                {
                    HotkeyBindings.Remove(b);
                    App.HotkeyService.Unregister(b.ActionId);
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
    /// Returns the <see cref="HostServices"/> instance for event wiring.
    /// </summary>
    internal HostServices Host => _hostServices;
}
