using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// Per-project overrides for the subset of <see cref="AppSettings"/> that may
/// differ per book (writing/language, book formatting, editor appearance).
/// Stored inside the project at <c>.novalist/settings.json</c> so it syncs via
/// git. A null property means "inherit the global value". JSON omits nulls, so
/// a project that overrides nothing serializes an empty object and inherits all.
/// Hotkeys and machine state (window geometry, recent projects, tokens) are
/// always global and intentionally absent here.
/// </summary>
public class SettingsOverrides
{
    // ── Writing / language ──────────────────────────────────────────
    [JsonPropertyName("language")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Language { get; set; }

    [JsonPropertyName("autoReplacementLanguage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AutoReplacementLanguage { get; set; }

    [JsonPropertyName("autoReplacements")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AutoReplacementPair>? AutoReplacements { get; set; }

    [JsonPropertyName("dialogueCorrectionEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DialogueCorrectionEnabled { get; set; }

    [JsonPropertyName("grammarCheckEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? GrammarCheckEnabled { get; set; }

    [JsonPropertyName("grammarCheckApiUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GrammarCheckApiUrl { get; set; }

    // ── Book formatting ─────────────────────────────────────────────
    [JsonPropertyName("enableBookParagraphSpacing")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? EnableBookParagraphSpacing { get; set; }

    [JsonPropertyName("enableBookWidth")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? EnableBookWidth { get; set; }

    [JsonPropertyName("bookPageFormat")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BookPageFormat { get; set; }

    [JsonPropertyName("bookTextBlockWidth")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? BookTextBlockWidth { get; set; }

    [JsonPropertyName("bookFontFamily")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BookFontFamily { get; set; }

    [JsonPropertyName("bookFontSize")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? BookFontSize { get; set; }

    // ── Editor appearance ───────────────────────────────────────────
    [JsonPropertyName("editorFontFamily")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EditorFontFamily { get; set; }

    [JsonPropertyName("editorFontSize")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? EditorFontSize { get; set; }

    [JsonPropertyName("theme")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Theme { get; set; }

    [JsonPropertyName("accentColor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccentColor { get; set; }

    [JsonPropertyName("typewriterScrollEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? TypewriterScrollEnabled { get; set; }

    [JsonPropertyName("typewriterScrollAnchor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TypewriterScrollAnchor { get; set; }

    [JsonPropertyName("pageViewEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? PageViewEnabled { get; set; }

    // Section grouping mirrors the Settings UI categories so each category gets
    // one Global / This-project scope toggle.

    /// <summary>True when any Appearance key (UI language, theme, accent) is overridden.</summary>
    [JsonIgnore]
    public bool HasAppearanceOverride =>
        Language != null || Theme != null || AccentColor != null;

    /// <summary>True when any Editor key (editor + book formatting) is overridden.</summary>
    [JsonIgnore]
    public bool HasEditorOverride =>
        EditorFontFamily != null || EditorFontSize != null
        || TypewriterScrollEnabled != null || TypewriterScrollAnchor != null || PageViewEnabled != null
        || EnableBookParagraphSpacing != null || EnableBookWidth != null || BookPageFormat != null
        || BookTextBlockWidth != null || BookFontFamily != null || BookFontSize != null;

    /// <summary>True when any Writing-assistance key (auto-replace, dialogue, grammar) is overridden.</summary>
    [JsonIgnore]
    public bool HasWritingOverride =>
        AutoReplacementLanguage != null || AutoReplacements != null
        || DialogueCorrectionEnabled != null || GrammarCheckEnabled != null
        || GrammarCheckApiUrl != null;

    /// <summary>Clears every Appearance override (revert section to global).</summary>
    public void ClearAppearance()
    {
        Language = null;
        Theme = null;
        AccentColor = null;
    }

    /// <summary>Clears every Editor override (revert section to global).</summary>
    public void ClearEditor()
    {
        EditorFontFamily = null;
        EditorFontSize = null;
        TypewriterScrollEnabled = null;
        TypewriterScrollAnchor = null;
        PageViewEnabled = null;
        EnableBookParagraphSpacing = null;
        EnableBookWidth = null;
        BookPageFormat = null;
        BookTextBlockWidth = null;
        BookFontFamily = null;
        BookFontSize = null;
    }

    /// <summary>Clears every Writing-assistance override (revert section to global).</summary>
    public void ClearWriting()
    {
        AutoReplacementLanguage = null;
        AutoReplacements = null;
        DialogueCorrectionEnabled = null;
        GrammarCheckEnabled = null;
        GrammarCheckApiUrl = null;
    }
}
