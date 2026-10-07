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
public sealed partial class ExtensionManager
{
    private readonly ExtensionLoader _loader;
    private readonly ISettingsService _settingsService;
    private readonly HostServices _hostServices;

    public ObservableCollection<ExtensionInfo> Extensions { get; } = [];
    internal event Action<string>? ExtensionStopping;

    // ── Hook collections (populated during loading) ─────────────────

    public List<RibbonItem> RibbonItems { get; } = [];
    public List<StatusBarItem> StatusBarItems { get; } = [];
    public List<ContextMenuItem> ContextMenuItems { get; } = [];
    public List<SettingsPage> SettingsPages { get; } = [];
    public List<Novalist.Sdk.Models.Wizards.WizardDefinition> Wizards { get; } = [];
    public List<EntityTypeDescriptor> EntityTypes { get; } = [];
    public List<ExportFormatDescriptor> ExportFormats { get; } = [];
    public List<IAiHook> AiHooks { get; } = [];
    public List<IGrammarCheckContributor> GrammarCheckContributors { get; } = [];
    public List<IArticleGeneratorContributor> ArticleGenerators { get; } = [];
    public List<IVoiceEngineContributor> VoiceEngines { get; } = [];
    public List<IDictationContributor> DictationContributors { get; } = [];
    public List<IEntityExtractionContributor> EntityExtractors { get; } = [];
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

            var instance = info.Instance
                ?? throw new InvalidOperationException("The loaded extension has no instance.");
            instance.Initialize(_hostServices);
            CollectHooks(info, instance);
        }
        catch (Exception ex)
        {
            info.LoadError = $"Initialize failed: {ex.Message}";
            RemoveHooks(info);
            try { info.Instance?.Shutdown(); } catch { /* Preserve the initialization error. */ }
            info.Instance = null;
            info.LoadContext?.Unload();
            info.LoadContext = null;
            info.IsLoaded = false;
            HostNotifications.Error?.Invoke($"Extension init failed: {info.Manifest.Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Collects hooks by inspecting the extension instance for hook interfaces.
    /// </summary>
    private void CollectHooks(ExtensionInfo info, IExtension instance)
    {
        var undo = new List<Action>();
        _hookUndo[info] = undo;

        // Helper: add the collected items to a target list and record the matching
        // removal of those exact references.
        void AddList<T>(List<T> target, IReadOnlyList<T> items)
        {
            target.AddRange(items);
            undo.Add(() => { foreach (var i in items) target.Remove(i); });
        }

        if (instance is IRibbonContributor ribbon)
            AddList(RibbonItems, ribbon.GetRibbonItems());

        if (instance is IStatusBarContributor statusBar)
            AddList(StatusBarItems, statusBar.GetStatusBarItems());

        if (instance is IContextMenuContributor contextMenu)
            AddList(ContextMenuItems, contextMenu.GetContextMenuItems());

        if (instance is ISettingsContributor settings)
            AddList(SettingsPages, settings.GetSettingsPages());

        if (instance is IWizardContributor wizardContributor)
            AddList(Wizards, wizardContributor.GetWizards());

        if (instance is IEntityTypeContributor entityType)
            AddList(EntityTypes, entityType.GetEntityTypes());

        if (instance is IExportFormatContributor exportFormat)
            AddList(ExportFormats, exportFormat.GetExportFormats());

        if (instance is IAiHook aiHook)
            AddList(AiHooks, [aiHook]);

        if (instance is IGrammarCheckContributor grammarCheck)
            AddList(GrammarCheckContributors, [grammarCheck]);

        if (instance is IArticleGeneratorContributor articleGenerator)
            AddList(ArticleGenerators, [articleGenerator]);

        if (instance is IVoiceEngineContributor voiceEngine)
            AddList(VoiceEngines, [voiceEngine]);

        if (instance is IDictationContributor dictation)
            AddList(DictationContributors, [dictation]);

        if (instance is IEntityExtractionContributor entityExtractor)
            AddList(EntityExtractors, [entityExtractor]);

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

    private static readonly JsonSerializerOptions InstallJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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
        ExtensionStopping?.Invoke(info.Manifest.Id);
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
            ExtensionStopping?.Invoke(info.Manifest.Id);
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
}
