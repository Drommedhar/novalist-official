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

    /// <summary>
    /// The writer's design-token overrides, applied over whichever theme is
    /// selected. Empty until they change something.
    /// </summary>
    [JsonRpcMethod("appearance/tokens")]
    public Dictionary<string, string> Tokens()
        => new(_workspace.Settings.Settings.ThemeTokens);

    /// <summary>
    /// Replaces the override set. Sent whole rather than patched: the editor
    /// knows the complete picture and a per-token patch would need a separate
    /// verb to clear one.
    ///
    /// A token name is stored without its dashes and a blank value is dropped,
    /// because an override set to nothing is not an override - it is the theme
    /// value, and storing it would pin today's theme colour into the settings
    /// file for ever.
    /// </summary>
    [JsonRpcMethod("appearance/setTokens")]
    public async Task<Dictionary<string, string>> SetTokensAsync(Dictionary<string, string>? tokens)
    {
        var cleaned = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in tokens ?? [])
        {
            var key = (name ?? string.Empty).Trim().TrimStart('-');
            if (key.Length == 0 || string.IsNullOrWhiteSpace(value)) continue;
            cleaned[key] = value.Trim();
        }

        _workspace.Settings.Settings.ThemeTokens = cleaned;
        await _workspace.Settings.SaveAsync();
        return Tokens();
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

    /// <summary>
    /// Re-reads the asset folders. Scanning happens at startup, which meant a
    /// dropped-in pack needed a restart to show up; this is the same scan on
    /// demand, so the writer can drop a file and press a button.
    /// </summary>
    [JsonRpcMethod("appearance/rescan")]
    public LanguagePackDto[] Rescan()
    {
        _workspace.UserAssets.EnsureDirectories();
        Core.Services.SceneAnalysisLexicon.RegisterUserDirectory(
            _workspace.UserAssets.AnalysisDirectory);
        return LanguagePacks();
    }

    /// <summary>
    /// What Novalist can read and write each language in.
    ///
    /// Two independent things carry a language: the interface locale and the
    /// scene-analysis lexicon. A writer can perfectly well run an English
    /// interface over a French manuscript, so the two are reported separately
    /// rather than collapsed into one "supported" flag that would be wrong for
    /// half the people asking.
    /// </summary>
    [JsonRpcMethod("appearance/languagePacks")]
    public LanguagePackDto[] LanguagePacks()
    {
        var bundledInterface = new[] { "en", "de", "zh-CN" };
        var userLocales = _workspace.UserAssets.DiscoverLocales()
            .ToDictionary(l => l.Code, l => l.Name, StringComparer.OrdinalIgnoreCase);
        var lexicons = Core.Services.SceneAnalysisLexicon.AvailableLanguages
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bundledLexicons = Core.Services.SceneAnalysisLexicon.BuiltInLanguages
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // The writing languages the Quote Style picker offers. Anything on that
        // list a writer can choose, so anything on it without a lexicon is a
        // real gap worth naming rather than leaving them to discover.
        var writingLanguages = new[]
        {
            "en", "de", "zh-CN", "fr", "es", "it", "pt", "ru", "pl", "cs", "sk", "nl", "ja", "ko"
        };

        var codes = bundledInterface
            .Concat(userLocales.Keys)
            .Concat(lexicons)
            .Concat(writingLanguages)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase);

        return [.. codes.Select(code => new LanguagePackDto(
            code,
            userLocales.TryGetValue(code, out var name) ? name : null,
            InterfaceSource(code, bundledInterface, userLocales),
            LexiconSource(code, lexicons, bundledLexicons)))];
    }

    private static string InterfaceSource(
        string code, string[] bundled, Dictionary<string, string> userLocales)
    {
        if (userLocales.ContainsKey(code)) return "user";
        return bundled.Contains(code, StringComparer.OrdinalIgnoreCase) ? "bundled" : "missing";
    }

    private static string LexiconSource(
        string code, HashSet<string> available, HashSet<string> bundled)
    {
        if (!available.Contains(code)) return "missing";
        return bundled.Contains(code) ? "bundled" : "user";
    }

    /// <summary>
    /// Writes a commented starting point for a scene-analysis lexicon into the
    /// Analysis folder, seeded from English so a contributor translates a real
    /// list instead of reverse-engineering the format from nothing. Refuses to
    /// overwrite an existing file.
    /// </summary>
    [JsonRpcMethod("appearance/writeLexiconTemplate")]
    public string WriteLexiconTemplate(string languageTag)
    {
        var tag = languageTag.Trim();
        if (tag.Length == 0) throw new InvalidOperationException("A language tag is required.");

        _workspace.UserAssets.EnsureDirectories();
        var path = System.IO.Path.Combine(
            _workspace.UserAssets.AnalysisDirectory, $"analysis.{tag}.json");
        if (System.IO.File.Exists(path))
            throw new InvalidOperationException($"A lexicon for '{tag}' already exists.");

        System.IO.File.WriteAllText(path, Core.Services.SceneAnalysisLexicon.TemplateFor(tag));
        return path;
    }
}

/// <summary>
/// How well Novalist speaks one language. <c>Interface</c> and <c>Lexicon</c>
/// are each "bundled", "user" or "missing" — independent, because reading the
/// menus in one language and writing in another is normal.
/// </summary>
public sealed record LanguagePackDto(
    string Code,
    string? Name,
    string Interface,
    string Lexicon);

public sealed record UserThemeDto(
    string Name,
    string Slug,
    IReadOnlyDictionary<string, string> Tokens,
    string? Css);

public sealed record UserLocaleDto(string Code, string Name, string Json);

public sealed record AppearanceDirectoriesDto(string Themes, string Locales, string Analysis);
