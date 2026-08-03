using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models.Wizards;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Extension host surface: load, enumerate, and expose contributions.</summary>
public sealed class ExtensionsRpc
{
    private readonly Workspace _workspace;
    private bool _loaded;

    public ExtensionsRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    [JsonRpcMethod("extensions/load")]
    public async Task<ExtensionInfoDto[]> LoadAsync()
    {
        if (!_loaded)
        {
            await _workspace.Settings.LoadAsync();
            // Point the extension-facing language at the effective setting before
            // any extension initializes (so GetLocalization resolves correctly).
            _workspace.SyncExtensionLanguage();
            await _workspace.ExtensionsHost.LoadAllAsync();
            _loaded = true;
            // If a project is already open, merge any freshly loaded extension
            // entity types into its custom-type registry now (project-open ran
            // before extensions were loaded).
            await _workspace.RegisterExtensionEntityTypesAsync();
        }
        return List();
    }

    [JsonRpcMethod("extensions/list")]
    public ExtensionInfoDto[] List() =>
        _workspace.ExtensionsHost.Extensions
            .Select(e => new ExtensionInfoDto(
                e.Manifest.Id,
                e.Manifest.Name,
                e.Manifest.Version,
                e.Manifest.Description,
                e.Manifest.Author,
                e.IsEnabled,
                e.LoadError))
            .ToArray();

    /// <summary>Enable or disable an installed extension; returns the updated list.</summary>
    [JsonRpcMethod("extensions/setEnabled")]
    public async Task<ExtensionInfoDto[]> SetEnabledAsync(string id, bool enabled)
    {
        if (enabled)
            await _workspace.ExtensionsHost.EnableExtensionAsync(id);
        else
            await _workspace.ExtensionsHost.DisableExtensionAsync(id);
        return List();
    }

    /// <summary>Install an extension from a local folder; returns the updated list.</summary>
    [JsonRpcMethod("extensions/install")]
    public async Task<ExtensionInfoDto[]> InstallAsync(string sourceFolder)
    {
        await _workspace.ExtensionsHost.InstallFromFolderAsync(sourceFolder);
        return List();
    }

    /// <summary>Uninstall an installed extension (deletes its files); returns the updated list.</summary>
    [JsonRpcMethod("extensions/uninstall")]
    public async Task<ExtensionInfoDto[]> UninstallAsync(string id)
    {
        await _workspace.ExtensionsHost.UninstallAsync(id);
        return List();
    }

    /// <summary>The on-disk directory extensions are installed into (for "open folder").</summary>
    [JsonRpcMethod("extensions/directory")]
    public string Directory() => _workspace.ExtensionsHost.ExtensionsDirectory;

    private readonly Dictionary<string, IWebViewController> _controllers = new();

    /// <summary>Sink for controller-initiated pushes; wired to a JSON-RPC
    /// notification by the backend host.</summary>
    public static Action<string, string, string>? WebviewPosted { get; set; }

    [JsonRpcMethod("extensions/views")]
    public WebViewInfoDto[] Views() =>
        _workspace.ExtensionsHost.Extensions
            .Where(e => e.IsEnabled && e.Manifest.Contributes != null)
            .SelectMany(e => e.Manifest.Contributes!.Views.Select(v => new WebViewInfoDto(
                e.Manifest.Id,
                v.Key,
                v.Title,
                v.IconPath,
                v.Placement,
                $"{e.Manifest.Id}/{v.Entry}",
                e.FolderPath)))
            .ToArray();

    /// <summary>
    /// The scripts extensions want to run inside the interface, as source.
    ///
    /// The path rather than the text: the interface imports it as a real module
    /// over the extension protocol. Handing over source would mean evaluating a
    /// string there, which needs unsafe-eval turned on for the whole interface -
    /// a far bigger hole than the one a plugin itself opens.
    ///
    /// An API version the host does not implement is refused rather than
    /// loaded and left to fail somewhere less obvious.
    /// </summary>
    [JsonRpcMethod("extensions/rendererPlugins")]
    public RendererPluginDto[] RendererPlugins()
    {
        var plugins = new List<RendererPluginDto>();
        foreach (var extension in _workspace.ExtensionsHost.Extensions)
        {
            if (!extension.IsEnabled || extension.Manifest.Contributes == null) continue;

            foreach (var plugin in extension.Manifest.Contributes.Renderer)
            {
                if (plugin.ApiVersion != RendererPluginApiVersion)
                {
                    plugins.Add(new RendererPluginDto(
                        extension.Manifest.Id, extension.Manifest.Name, plugin.ApiVersion,
                        string.Empty,
                        $"needs plugin API v{plugin.ApiVersion}; this Novalist speaks v{RendererPluginApiVersion}",
                        extension.FolderPath));
                    continue;
                }

                // The script has to live inside the extension's own folder. A
                // relative path that climbs out would let a manifest name any
                // file on the machine and have the interface run it.
                var root = Path.GetFullPath(extension.FolderPath);
                var entry = plugin.Entry ?? string.Empty;
                var full = Path.GetFullPath(Path.Combine(root, entry));
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
                {
                    plugins.Add(new RendererPluginDto(
                        extension.Manifest.Id, extension.Manifest.Name, plugin.ApiVersion,
                        string.Empty, "its script is missing or outside the extension folder",
                        extension.FolderPath));
                    continue;
                }

                // Forward slashes: this becomes a URL path, not a filesystem one.
                plugins.Add(new RendererPluginDto(
                    extension.Manifest.Id, extension.Manifest.Name, plugin.ApiVersion,
                    entry.Replace('\\', '/'), null, extension.FolderPath));
            }
        }
        return [.. plugins];
    }

    /// <summary>The plugin API this Novalist implements.</summary>
    public const int RendererPluginApiVersion = 1;

    [JsonRpcMethod("extensions/webviewMessage")]
    public async Task<string?> WebviewMessageAsync(string extensionId, string viewKey, string json)
    {
        var controller = GetController(extensionId, viewKey);
        return controller == null ? null : await controller.OnMessageAsync(json);
    }

    private IWebViewController? GetController(string extensionId, string viewKey)
    {
        var cacheKey = $"{extensionId}|{viewKey}";
        if (_controllers.TryGetValue(cacheKey, out var cached)) return cached;

        var extension = _workspace.ExtensionsHost.Extensions
            .FirstOrDefault(e => e.Manifest.Id == extensionId && e.IsEnabled);
        if (extension?.Instance is not IWebViewContributor contributor) return null;
        var controller = contributor.CreateController(viewKey);
        if (controller == null) return null;
        controller.MessagePosted += payload => WebviewPosted?.Invoke(extensionId, viewKey, payload);
        _controllers[cacheKey] = controller;
        return controller;
    }

    /// <summary>Extension-contributed settings-page metadata (category + icon).
    /// The page is metadata (category + icon); the editable form comes from the
    /// extension settings schema.</summary>
    [JsonRpcMethod("extensions/settingsPages")]
    public SettingsPageDto[] SettingsPages() =>
        _workspace.ExtensionsHost.EnumerateSettingsPages()
            .Select(x => new SettingsPageDto(x.ExtensionId, x.ExtensionName, x.Page.Category, x.Page.IconPath))
            .ToArray();

    /// <summary>Extension-contributed wizard descriptors, for the settings surface
    /// and the command palette's "Run wizard…" entries.</summary>
    [JsonRpcMethod("extensions/wizards")]
    public WizardDescriptorDto[] Wizards() =>
        _workspace.ExtensionsHost.EnumerateWizards()
            .Select(x => new WizardDescriptorDto(x.ExtensionId, x.ExtensionName, x.Def.Id, x.Def.DisplayName, x.Def.Description))
            .ToArray();

    /// <summary>Runs a contributed wizard interactively in the renderer and returns
    /// the collected result (null when the wizard was cancelled or unknown).</summary>
    [JsonRpcMethod("extensions/runWizard")]
    public async Task<WizardResult?> RunWizardAsync(string extensionId, string wizardId)
    {
        var def = _workspace.ExtensionsHost.FindWizard(extensionId, wizardId);
        if (def == null) return null;

        // The definition's OnCompleted fires inside HostServices.RunWizardAsync,
        // which both entry points go through - so a wizard reached from the
        // command palette acts exactly as one the extension ran itself.
        return await _workspace.ExtensionsHost.Host.RunWizardAsync(def, null);
    }

    [JsonRpcMethod("extensions/contributions")]
    public ExtensionContributionsDto Contributions()
    {
        var host = _workspace.ExtensionsHost;
        return new ExtensionContributionsDto(
            host.AiHooks.Count,
            host.GrammarCheckContributors.Count,
            host.ExportFormats.Select(f => f.DisplayName).ToArray(),
            Extensions.HotkeyRegistry.All
                .Select(h => new ExtensionHotkeyDto(h.ActionId, h.DisplayName, h.DefaultGesture))
                .ToArray());
    }
}

public sealed record ExtensionInfoDto(
    string Id,
    string Name,
    string Version,
    string Description,
    string Author,
    bool IsEnabled,
    string? LoadError);

public sealed record ExtensionContributionsDto(
    int AiHookCount,
    int GrammarContributorCount,
    IReadOnlyList<string> ExportFormats,
    IReadOnlyList<ExtensionHotkeyDto> Hotkeys);

public sealed record ExtensionHotkeyDto(string ActionId, string DisplayName, string DefaultGesture);

public sealed record SettingsPageDto(
    string ExtensionId,
    string ExtensionName,
    string Category,
    string? IconPath);

public sealed record WizardDescriptorDto(
    string ExtensionId,
    string ExtensionName,
    string WizardId,
    string DisplayName,
    string Description);

public sealed record WebViewInfoDto(
    string ExtensionId,
    string Key,
    string Title,
    string IconPath,
    string Placement,
    string Entry,
    string FolderPath);

/// <summary>
/// One script an extension wants to run inside the interface.
/// </summary>
/// <param name="Entry">
/// The script's path inside the extension folder, or empty when it cannot be
/// run. The interface turns this into a module URL.
/// </param>
/// <param name="Refused">
/// Why it will not be run, or null when it will. Named rather than dropped
/// silently: an extension that does nothing and says nothing is the hardest
/// kind of broken to report.
/// </param>
public sealed record RendererPluginDto(
    string ExtensionId, string ExtensionName, int ApiVersion, string Entry, string? Refused,
    /// <summary>Where the extension lives, so the interface can register the
    /// root its module URL resolves against.</summary>
    string FolderPath = "");
