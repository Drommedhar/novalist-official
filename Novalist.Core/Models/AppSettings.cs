using System.Text.Json.Serialization;
using Novalist.Sdk.Models;

namespace Novalist.Core.Models;

/// <summary>
/// Application-level settings stored in the user's app data directory.
/// </summary>
public class AppSettings
{
    [JsonPropertyName("language")]
    public string Language { get; set; } = "en";

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "system";

    [JsonPropertyName("recentProjects")]
    public List<RecentProject> RecentProjects { get; set; } = new();

    [JsonPropertyName("editorFontFamily")]
    public string EditorFontFamily { get; set; } = "Inter";

    [JsonPropertyName("editorFontSize")]
    public double EditorFontSize { get; set; } = 14;

    [JsonPropertyName("enableBookParagraphSpacing")]
    public bool EnableBookParagraphSpacing { get; set; }

    [JsonPropertyName("enableBookWidth")]
    public bool EnableBookWidth { get; set; }

    [JsonPropertyName("bookPageFormat")]
    public string BookPageFormat { get; set; } = "USTrade6x9";

    [JsonPropertyName("bookTextBlockWidth")]
    public double? BookTextBlockWidth { get; set; }

    [JsonPropertyName("bookFontFamily")]
    public string BookFontFamily { get; set; } = "Times New Roman";

    [JsonPropertyName("bookFontSize")]
    public double BookFontSize { get; set; } = 11;

    [JsonPropertyName("autoReplacementLanguage")]
    public string AutoReplacementLanguage { get; set; } = "en";

    [JsonPropertyName("autoReplacements")]
    public List<AutoReplacementPair> AutoReplacements { get; set; } = new();

    [JsonPropertyName("dialogueCorrectionEnabled")]
    public bool DialogueCorrectionEnabled { get; set; }

    [JsonPropertyName("windowWidth")]
    public double WindowWidth { get; set; } = 1400;

    [JsonPropertyName("windowHeight")]
    public double WindowHeight { get; set; } = 900;

    [JsonPropertyName("windowX")]
    public double? WindowX { get; set; }

    [JsonPropertyName("windowY")]
    public double? WindowY { get; set; }

    [JsonPropertyName("isMaximized")]
    public bool IsMaximized { get; set; }

    [JsonPropertyName("explorerWidth")]
    public double ExplorerWidth { get; set; } = 280;

    [JsonPropertyName("sidebarWidth")]
    public double SidebarWidth { get; set; } = 300;

    [JsonPropertyName("relationshipPairs")]
    public Dictionary<string, List<string>> RelationshipPairs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("ai")]
    public AiSettings Ai { get; set; } = new();

    [JsonPropertyName("extensions")]
    public Dictionary<string, bool> Extensions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// User-overridden hotkey bindings. Key = action ID (e.g. "app.nav.dashboard"),
    /// Value = key gesture string (e.g. "Ctrl+1"). Only stores overrides;
    /// missing entries use the default from the HotkeyDescriptor.
    /// </summary>
    [JsonPropertyName("hotkeyBindings")]
    public Dictionary<string, string> HotkeyBindings { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("checkForUpdates")]
    public bool CheckForUpdates { get; set; } = true;

    [JsonPropertyName("checkForExtensionUpdates")]
    public bool CheckForExtensionUpdates { get; set; } = true;

    /// <summary>
    /// Optional GitHub personal access token for extension gallery API requests.
    /// Increases the rate limit from 60 to 5000 requests/hour.
    /// </summary>
    [JsonPropertyName("githubToken")]
    public string? GitHubToken { get; set; }

    /// <summary>
    /// Ensures auto-replacements are populated from the language preset if empty.
    /// Call after deserialization.
    /// </summary>
    public void EnsureDefaults()
    {
        if (AutoReplacements.Count == 0)
        {
            AutoReplacements = AutoReplacementDefaults.GetPreset(AutoReplacementLanguage);
        }
    }

    public IReadOnlyList<string> GetKnownInverseRoles(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return [];

        return RelationshipPairs.TryGetValue(role.Trim(), out var matches)
            ? matches
            : [];
    }

    public bool LearnRelationshipPair(string role, string inverseRole)
    {
        if (string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(inverseRole))
            return false;

        var normalizedRole = role.Trim();
        var normalizedInverseRole = inverseRole.Trim();
        var changed = false;

        changed |= AddRelationshipPair(normalizedRole, normalizedInverseRole);
        changed |= AddRelationshipPair(normalizedInverseRole, normalizedRole);

        return changed;
    }

    private bool AddRelationshipPair(string role, string inverseRole)
    {
        if (!RelationshipPairs.TryGetValue(role, out var values))
        {
            values = [];
            RelationshipPairs[role] = values;
        }

        if (values.Any(existing => string.Equals(existing, inverseRole, StringComparison.OrdinalIgnoreCase)))
            return false;

        values.Add(inverseRole);
        values.Sort(StringComparer.OrdinalIgnoreCase);
        return true;
    }
}

public class RecentProject
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("lastOpened")]
    public DateTime LastOpened { get; set; }

    [JsonPropertyName("coverImagePath")]
    public string CoverImagePath { get; set; } = string.Empty;
}

public class AutoReplacementPair
{
    [JsonPropertyName("start")]
    public string Start { get; set; } = string.Empty;

    [JsonPropertyName("end")]
    public string End { get; set; } = string.Empty;

    [JsonPropertyName("startReplace")]
    public string StartReplace { get; set; } = string.Empty;

    [JsonPropertyName("endReplace")]
    public string EndReplace { get; set; } = string.Empty;
}

public static class AutoReplacementDefaults
{
    private static readonly AutoReplacementPair[] CommonReplacements =
    [
        new() { Start = "--", End = "--", StartReplace = "\u2014", EndReplace = "\u2014" },
        new() { Start = "...", End = "...", StartReplace = "\u2026", EndReplace = "\u2026" }
    ];

    private static readonly Dictionary<string, AutoReplacementPair[]> LanguagePresets = new()
    {
        ["en"] = [
            new() { Start = "'", End = "'", StartReplace = "\u201C", EndReplace = "\u201D" },
            .. CommonReplacements
        ],
        ["de-low"] = [
            new() { Start = "'", End = "'", StartReplace = "\u201E", EndReplace = "\u201C" },
            .. CommonReplacements
        ],
        ["de-guillemet"] = [
            new() { Start = "'", End = "'", StartReplace = "\u00BB", EndReplace = "\u00AB" },
            .. CommonReplacements
        ],
        ["fr"] = [
            new() { Start = "'", End = "'", StartReplace = "\u00AB\u00A0", EndReplace = "\u00A0\u00BB" },
            .. CommonReplacements
        ],
        ["es"] = [
            new() { Start = "'", End = "'", StartReplace = "\u00AB", EndReplace = "\u00BB" },
            .. CommonReplacements
        ],
        ["it"] = [
            new() { Start = "'", End = "'", StartReplace = "\u00AB", EndReplace = "\u00BB" },
            .. CommonReplacements
        ],
        ["pt"] = [
            new() { Start = "'", End = "'", StartReplace = "\u00AB", EndReplace = "\u00BB" },
            .. CommonReplacements
        ],
        ["ru"] = [
            new() { Start = "'", End = "'", StartReplace = "\u00AB", EndReplace = "\u00BB" },
            .. CommonReplacements
        ],
        ["pl"] = [
            new() { Start = "'", End = "'", StartReplace = "\u201E", EndReplace = "\u201C" },
            .. CommonReplacements
        ],
        ["cs"] = [
            new() { Start = "'", End = "'", StartReplace = "\u201E", EndReplace = "\u201C" },
            .. CommonReplacements
        ],
        ["sk"] = [
            new() { Start = "'", End = "'", StartReplace = "\u201E", EndReplace = "\u201C" },
            .. CommonReplacements
        ],
    };

    public static List<string> AvailableLanguages => [.. LanguagePresets.Keys];

    public static List<AutoReplacementPair> GetPreset(string language)
    {
        if (LanguagePresets.TryGetValue(language, out var pairs))
            return pairs.Select(p => new AutoReplacementPair
            {
                Start = p.Start,
                End = p.End,
                StartReplace = p.StartReplace,
                EndReplace = p.EndReplace
            }).ToList();

        return GetPreset("en");
    }
}
