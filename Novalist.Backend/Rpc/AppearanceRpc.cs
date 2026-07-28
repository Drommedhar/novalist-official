using System.Collections.Generic;
using System.Linq;
using Novalist.Backend.Appearance;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Serves the user-supplied appearance assets the renderer applies itself:
/// themes dropped into the Themes folder and interface locales dropped into the
/// Locales folder. Both are scanned once, at startup — the renderer calls these
/// before it applies settings so a saved user theme or language is in place on
/// the first paint rather than after a flash of the default.
/// </summary>
public sealed class AppearanceRpc
{
    private readonly Workspace _workspace;

    public AppearanceRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    /// <summary>User themes discovered on disk, ordered by display name.</summary>
    [JsonRpcMethod("appearance/themes")]
    public UserThemeDto[] Themes()
        => _workspace.UserAssets.DiscoverThemes()
            .Select(t => new UserThemeDto(t.Name, t.Slug, t.Tokens, t.Css))
            .ToArray();

    /// <summary>User interface locales discovered on disk, ordered by code.</summary>
    [JsonRpcMethod("appearance/locales")]
    public UserLocaleDto[] Locales()
        => _workspace.UserAssets.DiscoverLocales()
            .Select(l => new UserLocaleDto(l.Code, l.Name, l.Json))
            .ToArray();

    /// <summary>The folders the user drops assets into, for the "open folder"
    /// buttons in Settings. Returned whether or not they currently exist.</summary>
    [JsonRpcMethod("appearance/directories")]
    public AppearanceDirectoriesDto Directories()
    {
        var assets = _workspace.UserAssets;
        return new AppearanceDirectoriesDto(
            assets.ThemesDirectory, assets.LocalesDirectory, assets.AnalysisDirectory);
    }
}

public sealed record UserThemeDto(
    string Name,
    string Slug,
    IReadOnlyDictionary<string, string> Tokens,
    string? Css);

public sealed record UserLocaleDto(string Code, string Name, string Json);

public sealed record AppearanceDirectoriesDto(string Themes, string Locales, string Analysis);
