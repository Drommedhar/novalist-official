using Novalist.Sdk.Hooks;
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
            await _workspace.ExtensionsHost.LoadAllAsync();
            _loaded = true;
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
                e.IsEnabled,
                e.LoadError))
            .ToArray();

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
    bool IsEnabled,
    string? LoadError);

public sealed record ExtensionContributionsDto(
    int AiHookCount,
    int GrammarContributorCount,
    IReadOnlyList<string> ExportFormats,
    IReadOnlyList<ExtensionHotkeyDto> Hotkeys);

public sealed record ExtensionHotkeyDto(string ActionId, string DisplayName, string DefaultGesture);

public sealed record WebViewInfoDto(
    string ExtensionId,
    string Key,
    string Title,
    string IconPath,
    string Placement,
    string Entry,
    string FolderPath);
