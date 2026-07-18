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
