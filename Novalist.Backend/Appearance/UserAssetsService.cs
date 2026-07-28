using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Novalist.Backend.Extensions;

namespace Novalist.Backend.Appearance;

/// <summary>A theme discovered in the user's Themes folder. Exactly one of
/// <see cref="Tokens"/> and <see cref="Css"/> carries the palette: a
/// <c>.json</c> file yields a token map, a <c>.css</c> file yields raw CSS the
/// renderer injects only while the theme is selected.</summary>
public sealed record UserTheme(
    string Name,
    string Slug,
    IReadOnlyDictionary<string, string> Tokens,
    string? Css);

/// <summary>A UI language discovered in the user's Locales folder.
/// <see cref="Json"/> is the raw file content; the renderer flattens and merges
/// it over the bundled locale of the same code.</summary>
public sealed record UserLocale(string Code, string Name, string Json);

/// <summary>
/// Discovers user-supplied themes, interface locales, and scene-analysis
/// lexicons dropped into folders beside the extensions directory. The three
/// folders live under the same settings root as <c>settings.json</c> and
/// <c>Extensions/</c>, so an isolated data dir (NOVALIST_SETTINGS_DIR) gets
/// isolated assets.
///
/// Scanning happens once at startup. Nothing here loads code — a theme is data
/// (a token map or a stylesheet) and a locale is data, so a dropped file can
/// change how the app looks and reads but cannot execute.
/// </summary>
public sealed class UserAssetsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly string _root;

    /// <param name="settingsRoot">Settings root holding the asset folders;
    /// defaults to the same root the extensions loader uses. Tests pass a temp dir.</param>
    public UserAssetsService(string? settingsRoot = null)
        => _root = settingsRoot ?? DefaultRoot();

    /// <summary>The settings root the asset folders hang off. Mirrors
    /// <see cref="Extensions.ExtensionLoader.GetExtensionsDirectory"/>'s root so
    /// Themes/, Locales/ and Analysis/ sit next to Extensions/.</summary>
    public static string DefaultRoot()
        => Environment.GetEnvironmentVariable("NOVALIST_SETTINGS_DIR")
           ?? Path.Combine(
               Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Novalist");

    /// <summary>Folder holding user theme files (<c>*.json</c> token maps and
    /// <c>*.css</c> stylesheets).</summary>
    public string ThemesDirectory => Path.Combine(_root, "Themes");

    /// <summary>Folder holding user interface locale files (<c>&lt;code&gt;.json</c>).</summary>
    public string LocalesDirectory => Path.Combine(_root, "Locales");

    /// <summary>Folder holding user scene-analysis lexicons
    /// (<c>analysis.&lt;tag&gt;.json</c>).</summary>
    public string AnalysisDirectory => Path.Combine(_root, "Analysis");

    /// <summary>Creates the three asset folders if they are missing, so a user
    /// who goes looking for somewhere to drop a theme finds it already there.
    /// Failures are swallowed: a read-only or unavailable settings root must not
    /// stop the app from starting.</summary>
    public void EnsureDirectories()
    {
        foreach (var dir in new[] { ThemesDirectory, LocalesDirectory, AnalysisDirectory })
        {
            try { Directory.CreateDirectory(dir); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Could not create user asset folder: {ex.GetType().Name}");
            }
        }
    }

    // ── Themes ──────────────────────────────────────────────────────────

    /// <summary>
    /// Reads every theme file in the Themes folder. A malformed file is skipped
    /// rather than failing the scan, so one bad theme cannot cost the user the
    /// rest of them. Ordered by display name for a stable dropdown.
    /// </summary>
    public IReadOnlyList<UserTheme> DiscoverThemes()
    {
        var themes = new List<UserTheme>();
        foreach (var file in EnumerateFiles(ThemesDirectory))
        {
            var extension = Path.GetExtension(file).ToLowerInvariant();
            if (extension is not (".json" or ".css")) continue;

            var text = TryRead(file);
            if (text == null) continue;

            var stem = Path.GetFileNameWithoutExtension(file);
            var theme = extension == ".json"
                ? ParseTokenTheme(text, stem)
                : new UserTheme(stem, Slugify(stem), new Dictionary<string, string>(), text);
            if (theme == null) continue;
            if (themes.Any(t => string.Equals(t.Slug, theme.Slug, StringComparison.Ordinal))) continue;
            themes.Add(theme);
        }
        return themes.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    /// <summary>
    /// Builds a theme from a token-map JSON file. Null when the JSON is
    /// unreadable or declares no usable token. Kept internal-but-testable so the
    /// filtering rules can be exercised without touching disk.
    /// </summary>
    internal static UserTheme? ParseTokenTheme(string json, string fallbackName)
    {
        ThemeFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ThemeFile>(json, JsonOptions);
        }
        catch (JsonException)
        {
            Log.Warn("Skipped a user theme file that is not valid JSON");
            return null;
        }
        if (file == null) return null;

        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in file.Tokens ?? new Dictionary<string, string>())
        {
            if (!IsTokenName(key) || !IsTokenValue(value)) continue;
            tokens[key] = value.Trim();
        }
        if (tokens.Count == 0)
        {
            Log.Warn("Skipped a user theme file that declares no usable token");
            return null;
        }

        var name = string.IsNullOrWhiteSpace(file.Name) ? fallbackName : file.Name.Trim();
        return new UserTheme(name, Slugify(fallbackName), tokens, null);
    }

    /// <summary>
    /// Whether a key names a Novalist design token. Themes restate the palette
    /// only, so the contract is the <c>--nl-*</c> tier; the brand layer
    /// (<c>--nv-*</c>) is the corporate identity and is not overridable.
    /// </summary>
    internal static bool IsTokenName(string? key)
        => key != null
           && key.StartsWith("--nl-", StringComparison.Ordinal)
           && key.Length > 5
           && key.Skip(2).All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    /// <summary>
    /// Whether a value is safe to emit as a CSS declaration. A token map is
    /// wrapped in a rule the renderer builds, so a value carrying a brace,
    /// semicolon, or comment marker could close that rule and inject arbitrary
    /// CSS. Token themes are the safe format by design, so such values are
    /// dropped rather than escaped — a stylesheet is what the .css form is for.
    /// </summary>
    internal static bool IsTokenValue(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Length <= 512
           && value.IndexOfAny([';', '{', '}', '<', '>']) < 0
           && !value.Contains("/*", StringComparison.Ordinal)
           && !value.Contains("*/", StringComparison.Ordinal);

    /// <summary>
    /// Turns a file name into a CSS-safe theme slug. Prefixed so a user theme
    /// can never collide with a built-in <c>data-theme</c> value.
    /// </summary>
    internal static string Slugify(string name)
    {
        var chars = name.Trim().ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = new string(chars).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return $"user-{(slug.Length == 0 ? "theme" : slug)}";
    }

    // ── Locales ─────────────────────────────────────────────────────────

    /// <summary>
    /// Reads every locale file in the Locales folder. The file name is the
    /// language code ("fr.json" -> "fr"); the display name comes from the file's
    /// own <c>language.name</c> so a language lists under its native name.
    /// </summary>
    public IReadOnlyList<UserLocale> DiscoverLocales()
    {
        var locales = new List<UserLocale>();
        foreach (var file in EnumerateFiles(LocalesDirectory))
        {
            if (!string.Equals(Path.GetExtension(file), ".json", StringComparison.OrdinalIgnoreCase))
                continue;

            var code = Path.GetFileNameWithoutExtension(file).Trim();
            if (code.Length == 0 || !IsLanguageCode(code)) continue;
            if (locales.Any(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))) continue;

            var text = TryRead(file);
            if (text == null) continue;

            var name = ReadLanguageName(text);
            if (name == null)
            {
                Log.Warn("Skipped a user locale file that is not valid JSON");
                continue;
            }
            locales.Add(new UserLocale(code, name.Length == 0 ? code : name, text));
        }
        return locales.OrderBy(l => l.Code, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>Whether a file stem is shaped like a BCP-47 tag Novalist accepts
    /// ("fr", "pt-BR"). Guards the language dropdown against stray files.</summary>
    internal static bool IsLanguageCode(string code)
    {
        var parts = code.Split('-');
        if (parts.Length is < 1 or > 3) return false;
        return parts.All(p => p.Length is >= 2 and <= 8 && p.All(char.IsAsciiLetterOrDigit));
    }

    /// <summary>The locale's own <c>language.name</c>, empty when the file omits
    /// it, or null when the file is not valid JSON.</summary>
    internal static string? ReadLanguageName(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (document.RootElement.TryGetProperty("language", out var language)
                && language.ValueKind == JsonValueKind.Object
                && language.TryGetProperty("name", out var name)
                && name.ValueKind == JsonValueKind.String)
            {
                return name.GetString() ?? string.Empty;
            }
            return string.Empty;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ── Shared helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Files in an asset folder, in a stable case-insensitive order so which of
    /// two files claiming the same slug wins does not depend on the filesystem.
    /// A folder that is missing or cannot be listed yields nothing — both are
    /// ordinary states (the folders are created at startup but a user can delete
    /// one), so neither is worth a diagnostic.
    /// </summary>
    private static IEnumerable<string> EnumerateFiles(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? TryRead(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not read a user asset file: {ex.GetType().Name}");
            return null;
        }
    }

    private sealed class ThemeFile
    {
        public string? Name { get; set; }
        public Dictionary<string, string>? Tokens { get; set; }
    }
}
