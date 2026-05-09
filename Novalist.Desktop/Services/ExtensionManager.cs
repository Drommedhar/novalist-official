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
    private readonly ExtensionLoader _loader = new();
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

    public ExtensionManager(ISettingsService settingsService, HostServices hostServices)
    {
        _settingsService = settingsService;
        _hostServices = hostServices;
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
        if (info.Instance == null)
            return;

        try
        {
            // Register locale folder so GetLocalization() works during Initialize
            var localesDir = System.IO.Path.Combine(info.FolderPath, "Locales");
            _hostServices.RegisterExtensionLocales(info.Manifest.Id, localesDir);

            info.Instance.Initialize(_hostServices);
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
        var instance = info.Instance;
        if (instance == null)
            return;

        if (instance is IRibbonContributor ribbon)
            RibbonItems.AddRange(ribbon.GetRibbonItems());

        if (instance is ISidebarContributor sidebar)
            SidebarPanels.AddRange(sidebar.GetSidebarPanels());

        if (instance is IStatusBarContributor statusBar)
            StatusBarItems.AddRange(statusBar.GetStatusBarItems());

        if (instance is IContextMenuContributor contextMenu)
            ContextMenuItems.AddRange(contextMenu.GetContextMenuItems());

        if (instance is IContentViewContributor contentView)
            ContentViews.AddRange(contentView.GetContentViews());

        if (instance is ISettingsContributor settings)
            SettingsPages.AddRange(settings.GetSettingsPages());

        if (instance is IEntityTypeContributor entityType)
            EntityTypes.AddRange(entityType.GetEntityTypes());

        if (instance is IExportFormatContributor exportFormat)
            ExportFormats.AddRange(exportFormat.GetExportFormats());

        if (instance is IAiHook aiHook)
            AiHooks.Add(aiHook);

        if (instance is IGrammarCheckContributor grammarCheck)
            GrammarCheckContributors.Add(grammarCheck);

        if (instance is IThemeContributor theme)
            ThemeOverrides.AddRange(theme.GetThemeOverrides());

        if (instance is IHotkeyContributor hotkey)
        {
            var bindings = hotkey.GetHotkeyBindings();
            HotkeyBindings.AddRange(bindings);
            App.HotkeyService.RegisterRange(bindings);
        }

        if (instance is IEditorExtension editorExt)
            _hostServices.RegisterEditorExtension(editorExt);

        if (instance is IPropertyTypeContributor propertyType)
            PropertyTypes.AddRange(propertyType.GetPropertyTypes());
    }

    /// <summary>
    /// Removes hooks contributed by a specific extension.
    /// </summary>
    private void RemoveHooks(ExtensionInfo info)
    {
        var instance = info.Instance;
        if (instance == null)
            return;

        if (instance is IRibbonContributor ribbon)
        {
            var items = ribbon.GetRibbonItems();
            foreach (var item in items)
                RibbonItems.Remove(item);
        }

        if (instance is ISidebarContributor sidebar)
        {
            var panels = sidebar.GetSidebarPanels();
            foreach (var panel in panels)
                SidebarPanels.Remove(panel);
        }

        if (instance is IStatusBarContributor statusBar)
        {
            var items = statusBar.GetStatusBarItems();
            foreach (var item in items)
                StatusBarItems.Remove(item);
        }

        if (instance is IContextMenuContributor contextMenu)
        {
            var items = contextMenu.GetContextMenuItems();
            foreach (var item in items)
                ContextMenuItems.Remove(item);
        }

        if (instance is IContentViewContributor contentView)
        {
            var views = contentView.GetContentViews();
            foreach (var view in views)
                ContentViews.Remove(view);
        }

        if (instance is ISettingsContributor settings)
        {
            var pages = settings.GetSettingsPages();
            foreach (var page in pages)
                SettingsPages.Remove(page);
        }

        if (instance is IEntityTypeContributor entityType)
        {
            var types = entityType.GetEntityTypes();
            foreach (var t in types)
                EntityTypes.Remove(t);
        }

        if (instance is IExportFormatContributor exportFormat)
        {
            var formats = exportFormat.GetExportFormats();
            foreach (var f in formats)
                ExportFormats.Remove(f);
        }

        if (instance is IAiHook aiHook)
            AiHooks.Remove(aiHook);

        if (instance is IGrammarCheckContributor grammarCheck)
            GrammarCheckContributors.Remove(grammarCheck);

        if (instance is IThemeContributor theme)
        {
            var overrides = theme.GetThemeOverrides();
            foreach (var o in overrides)
                ThemeOverrides.Remove(o);
        }

        if (instance is IHotkeyContributor hotkey)
        {
            var bindings = hotkey.GetHotkeyBindings();
            foreach (var b in bindings)
            {
                HotkeyBindings.Remove(b);
                App.HotkeyService.Unregister(b.ActionId);
            }
        }

        if (instance is IEditorExtension editorExt)
            _hostServices.UnregisterEditorExtension(editorExt);

        if (instance is IPropertyTypeContributor propertyType)
        {
            var types = propertyType.GetPropertyTypes();
            foreach (var t in types)
                PropertyTypes.Remove(t);
        }
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
