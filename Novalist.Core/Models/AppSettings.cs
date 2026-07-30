using System.Text.Json.Serialization;
using Novalist.Core.Services;

namespace Novalist.Core.Models;

/// <summary>
/// Application-level settings stored in the user's app data directory.
/// Implements <see cref="IEffectiveSettings"/> by returning its own (global)
/// values, so it can stand in wherever a resolved settings view is expected.
/// </summary>
public class AppSettings : IEffectiveSettings
{
    [JsonPropertyName("language")]
    public string Language { get; set; } = "en";

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "system";

    [JsonPropertyName("recentProjects")]
    public List<RecentProject> RecentProjects { get; set; } = new();

    // Newsreader at 17 is the identity's text face and body size. Both ship
    // with the app, so the default renders the same on every machine.
    [JsonPropertyName("editorFontFamily")]
    public string EditorFontFamily { get; set; } = "Newsreader";

    [JsonPropertyName("editorFontSize")]
    public double EditorFontSize { get; set; } = 17;

    /// <summary>
    /// Leading, as a multiple of the font size. A theme cannot change this -
    /// themes are colour only - so without a setting there is no way to open
    /// the lines up, which is the single most effective reading-comfort change
    /// for a dyslexic or low-vision reader.
    /// </summary>
    [JsonPropertyName("editorLineHeight")]
    public double EditorLineHeight { get; set; } = 1.7;

    /// <summary>
    /// Tints each sentence in the editor by how hard it is to read. Off by
    /// default: it is a revision tool, and a coloured page while drafting is
    /// the opposite of what drafting needs.
    /// </summary>
    [JsonPropertyName("readabilityHighlighting")]
    public bool ReadabilityHighlighting { get; set; }

    /// <summary>How fast read-aloud speaks, as a multiple of the voice's own pace.</summary>
    [JsonPropertyName("readAloudRate")]
    public double ReadAloudRate { get; set; } = 1;

    /// <summary>
    /// The installed voice read-aloud uses, by its platform URI. Null lets the
    /// system pick one for the language the scene is written in.
    /// </summary>
    [JsonPropertyName("readAloudVoiceUri")]
    public string? ReadAloudVoiceUri { get; set; }

    /// <summary>Extra space between letters, in pixels. Zero is the face's own.</summary>
    [JsonPropertyName("editorLetterSpacing")]
    public double EditorLetterSpacing { get; set; }

    /// <summary>
    /// The gap the book-paragraph-spacing toggle inserts, in ems. The toggle
    /// says whether there is a gap; this says how big.
    /// </summary>
    [JsonPropertyName("editorParagraphSpacing")]
    public double EditorParagraphSpacing { get; set; } = 0.75;

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

    /// <summary>
    /// The name put on a suggested edit. Empty is fine for a writer working
    /// alone - it is only worth filling in when more than one person is
    /// suggesting, which is exactly when an unattributed edit is useless.
    /// </summary>
    public string ReviewerName { get; set; } = string.Empty;

    [JsonPropertyName("autoReplacements")]
    public List<AutoReplacementPair> AutoReplacements { get; set; } = new();

    [JsonPropertyName("dialogueCorrectionEnabled")]
    public bool DialogueCorrectionEnabled { get; set; }

    [JsonPropertyName("grammarCheckEnabled")]
    public bool GrammarCheckEnabled { get; set; } = true;

    /// <summary>
    /// Underline misspellings in the prose surface using the platform's own
    /// spell checker. Works with no network, unlike the LanguageTool grammar
    /// check, and is on by default because an offline-first writing app that
    /// cannot spell-check offline is not much of one.
    /// </summary>
    [JsonPropertyName("spellCheckEnabled")]
    public bool SpellCheckEnabled { get; set; } = true;

    /// <summary>
    /// Language tags the spell checker loads, e.g. ["en-GB", "de-DE"]. Empty
    /// means "follow the writing language", which is what most writers want and
    /// what a fresh install does.
    /// </summary>
    [JsonPropertyName("spellCheckLanguages")]
    public List<string> SpellCheckLanguages { get; set; } = new();

    /// <summary>The stored list resolved for use. Explicit so the settings file
    /// keeps the writer's literal choice (including "empty, follow the writing
    /// language") while readers always get a usable list.</summary>
    IReadOnlyList<string> IEffectiveSettings.SpellCheckLanguages
        => Services.SpellCheckLanguages.Resolve(SpellCheckLanguages, AutoReplacementLanguage);

    /// <summary>
    /// Words the writer added from the spelling context menu. Kept here rather
    /// than in the platform dictionary so a made-up name learned on one machine
    /// travels with the settings file instead of being learned again.
    /// </summary>
    [JsonPropertyName("spellCheckCustomWords")]
    public List<string> SpellCheckCustomWords { get; set; } = new();

    /// <summary>
    /// Words the writer wants the style report to count: their own crutches,
    /// or the spellings a series bible fixes. Kept with the settings rather
    /// than the project - a writer's habits follow them from book to book.
    /// </summary>
    [JsonPropertyName("styleWatchWords")]
    public List<string> StyleWatchWords { get; set; } = new();

    /// <summary>
    /// Whether composition mode dims every paragraph but the one the caret is
    /// in. Off by default: dimming is a strong preference, not a default.
    /// </summary>
    [JsonPropertyName("composeDimming")]
    public bool ComposeDimming { get; set; }

    [JsonPropertyName("typewriterScrollEnabled")]
    public bool TypewriterScrollEnabled { get; set; }

    /// <summary>Vertical anchor for typewriter scroll. "top" | "middle" | "bottom".</summary>
    [JsonPropertyName("typewriterScrollAnchor")]
    public string TypewriterScrollAnchor { get; set; } = "middle";

    [JsonPropertyName("pageViewEnabled")]
    public bool PageViewEnabled { get; set; }

    /// <summary>
    /// Custom LanguageTool API URL. When null or empty, the free public API is used.
    /// Supports self-hosted instances (e.g. "http://localhost:8081/v2/check").
    /// </summary>
    [JsonPropertyName("grammarCheckApiUrl")]
    public string? GrammarCheckApiUrl { get; set; }

    /// <summary>
    /// Optional LanguageTool Cloud API key (premium). When set and non-empty,
    /// the app will include it in requests to enable premium checks.
    /// </summary>
    [JsonPropertyName("grammarCheckApiKey")]
    public string? GrammarCheckApiKey { get; set; }

    /// <summary>
    /// Optional LanguageTool Cloud username (email) (premium). When set and non-empty,
    /// the app will include it in requests to enable premium checks.
    /// </summary>
    [JsonPropertyName("grammarCheckUsername")]
    public string? GrammarCheckUsername { get; set; }

    [JsonPropertyName("grammarCheckPickyMode")]
    public bool GrammarCheckPickyMode { get; set; }

    [JsonPropertyName("grammarCheckMotherTongue")]
    public string? GrammarCheckMotherTongue { get; set; }


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

    /// <summary>
    /// Arbitrary JSON blobs stored by extensions. Key = extension-defined string (e.g. "com.novalist.ai").
    /// Extensions read/write via IHostServices.ReadHostData / WriteHostDataAsync.
    /// </summary>
    [JsonPropertyName("extensionData")]
    public Dictionary<string, string> ExtensionData { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("extensions")]
    public Dictionary<string, bool> Extensions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// User-overridden hotkey bindings. Key = action ID (e.g. "app.nav.dashboard"),
    /// Value = key gesture string (e.g. "Ctrl+1"). Only stores overrides;
    /// missing entries use the default from the HotkeyDescriptor.
    /// </summary>
    [JsonPropertyName("hotkeyBindings")]
    public Dictionary<string, string> HotkeyBindings { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// When true, the app writes a content-safe diagnostic log to
    /// %APPDATA%/Novalist/logs so users can send it for support. Never records
    /// story content. Default off.
    /// </summary>
    [JsonPropertyName("diagnosticLoggingEnabled")]
    public bool DiagnosticLoggingEnabled { get; set; }

    /// <summary>
    /// When true, the whole project folder is archived to a rotating ZIP on
    /// project open, on project close, and every <see cref="BackupIntervalMinutes"/>
    /// minutes while it stays open. Default on: this is data safety, not a feature.
    /// </summary>
    [JsonPropertyName("backupEnabled")]
    public bool BackupEnabled { get; set; } = true;

    /// <summary>
    /// Minutes between automatic backups while a project is open. Clamped to
    /// [5, 1440] by <see cref="Services.BackupService"/>. Zero disables the
    /// interval without disabling open/close backups.
    /// </summary>
    [JsonPropertyName("backupIntervalMinutes")]
    public int BackupIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// How many archives to keep per project before the oldest is pruned.
    /// Clamped to [1, 100].
    /// </summary>
    [JsonPropertyName("backupRetentionCount")]
    public int BackupRetentionCount { get; set; } = 5;

    /// <summary>
    /// Folder the per-project archive directories live in. Empty means the
    /// default, <c>%APPDATA%/Novalist/Backups</c>. Deliberately outside the
    /// project: a backup stored inside the project dies with it.
    /// </summary>
    [JsonPropertyName("backupFolder")]
    public string BackupFolder { get; set; } = string.Empty;

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
    /// User-overridden accent color as a hex string (e.g. "#5865F2").
    /// When null, the active theme's default accent color is used.
    /// </summary>
    [JsonPropertyName("accentColor")]
    public string? AccentColor { get; set; }

    /// <summary>
    /// Design-token overrides, by token name without the leading dashes
    /// ("nl-surface-card", "nl-font-body").
    ///
    /// Appearance offered a theme, an accent colour and two folder buttons.
    /// Everything else - every surface, every size, every radius - meant
    /// hand-writing a JSON token map or a .css file and restarting. These are
    /// the same tokens, edited in the app and applied without one.
    /// </summary>
    [JsonPropertyName("themeTokens")]
    public Dictionary<string, string> ThemeTokens { get; set; } = [];

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
