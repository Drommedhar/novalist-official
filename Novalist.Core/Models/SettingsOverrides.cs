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

    [JsonPropertyName("spellCheckEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SpellCheckEnabled { get; set; }

    [JsonPropertyName("spellCheckLanguages")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? SpellCheckLanguages { get; set; }

    [JsonPropertyName("grammarCheckApiUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GrammarCheckApiUrl { get; set; }

    [JsonPropertyName("grammarCheckApiKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GrammarCheckApiKey { get; set; }

    [JsonPropertyName("grammarCheckUsername")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GrammarCheckUsername { get; set; }

    [JsonPropertyName("grammarCheckPickyMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? GrammarCheckPickyMode { get; set; }

    [JsonPropertyName("grammarCheckMotherTongue")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GrammarCheckMotherTongue { get; set; }

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

    [JsonPropertyName("editorLineHeight")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? EditorLineHeight { get; set; }

    [JsonPropertyName("readabilityHighlighting")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ReadabilityHighlighting { get; set; }

    [JsonPropertyName("readAloudRate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ReadAloudRate { get; set; }

    [JsonPropertyName("readAloudVoiceUri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReadAloudVoiceUri { get; set; }

    [JsonPropertyName("editorLetterSpacing")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? EditorLetterSpacing { get; set; }

    [JsonPropertyName("editorParagraphSpacing")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? EditorParagraphSpacing { get; set; }

    [JsonPropertyName("theme")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Theme { get; set; }

    [JsonPropertyName("accentColor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccentColor { get; set; }

    [JsonPropertyName("composeDimming")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ComposeDimming { get; set; }

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
        || EditorLineHeight != null || EditorLetterSpacing != null
        || ReadAloudRate != null || ReadAloudVoiceUri != null
        || ReadabilityHighlighting != null
        || EditorParagraphSpacing != null
        || ComposeDimming != null
        || TypewriterScrollEnabled != null || TypewriterScrollAnchor != null || PageViewEnabled != null
        || EnableBookParagraphSpacing != null || EnableBookWidth != null || BookPageFormat != null
        || BookTextBlockWidth != null || BookFontFamily != null || BookFontSize != null;

    /// <summary>True when any Writing-assistance key (auto-replace, dialogue, grammar) is overridden.</summary>
    [JsonIgnore]
    public bool HasWritingOverride =>
        AutoReplacementLanguage != null || AutoReplacements != null
        || DialogueCorrectionEnabled != null || GrammarCheckEnabled != null
        || SpellCheckEnabled != null || SpellCheckLanguages != null
        || GrammarCheckApiUrl != null || GrammarCheckApiKey != null || GrammarCheckUsername != null
        || GrammarCheckPickyMode != null || GrammarCheckMotherTongue != null;

    /// <summary>
    /// Pins the Appearance section to the project by copying the values in
    /// effect right now into the overrides. This is what turning the section's
    /// project-override switch on does: the project keeps what it currently
    /// looks like, and later edits to the global defaults no longer reach it.
    /// The inverse of <see cref="ClearAppearance"/>.
    /// </summary>
    public void PinAppearance(Services.IEffectiveSettings source)
    {
        Language = source.Language;
        Theme = source.Theme;
        AccentColor = source.AccentColor;
    }

    /// <summary>Pins the Editor section to the project. See <see cref="PinAppearance"/>.</summary>
    public void PinEditor(Services.IEffectiveSettings source)
    {
        EditorFontFamily = source.EditorFontFamily;
        EditorFontSize = source.EditorFontSize;
        EditorLineHeight = source.EditorLineHeight;
        ReadabilityHighlighting = source.ReadabilityHighlighting;
        ReadAloudRate = source.ReadAloudRate;
        ReadAloudVoiceUri = source.ReadAloudVoiceUri;
        EditorLetterSpacing = source.EditorLetterSpacing;
        EditorParagraphSpacing = source.EditorParagraphSpacing;
        ComposeDimming = source.ComposeDimming;
        TypewriterScrollEnabled = source.TypewriterScrollEnabled;
        TypewriterScrollAnchor = source.TypewriterScrollAnchor;
        PageViewEnabled = source.PageViewEnabled;
        EnableBookParagraphSpacing = source.EnableBookParagraphSpacing;
        EnableBookWidth = source.EnableBookWidth;
        BookPageFormat = source.BookPageFormat;
        BookTextBlockWidth = source.BookTextBlockWidth;
        BookFontFamily = source.BookFontFamily;
        BookFontSize = source.BookFontSize;
    }

    /// <summary>Pins the Writing-assistance section to the project. See <see cref="PinAppearance"/>.</summary>
    public void PinWriting(Services.IEffectiveSettings source)
    {
        AutoReplacementLanguage = source.AutoReplacementLanguage;
        AutoReplacements = [.. source.AutoReplacements];
        DialogueCorrectionEnabled = source.DialogueCorrectionEnabled;
        GrammarCheckEnabled = source.GrammarCheckEnabled;
        SpellCheckEnabled = source.SpellCheckEnabled;
        SpellCheckLanguages = [.. source.SpellCheckLanguages];
        GrammarCheckApiUrl = source.GrammarCheckApiUrl;
        GrammarCheckApiKey = source.GrammarCheckApiKey;
        GrammarCheckUsername = source.GrammarCheckUsername;
        GrammarCheckPickyMode = source.GrammarCheckPickyMode;
        GrammarCheckMotherTongue = source.GrammarCheckMotherTongue;
    }

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
        EditorLineHeight = null;
        ReadabilityHighlighting = null;
        ReadAloudRate = null;
        ReadAloudVoiceUri = null;
        EditorLetterSpacing = null;
        EditorParagraphSpacing = null;
        ComposeDimming = null;
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
        SpellCheckEnabled = null;
        SpellCheckLanguages = null;
        GrammarCheckApiUrl = null;
        GrammarCheckApiKey = null;
        GrammarCheckUsername = null;
        GrammarCheckPickyMode = null;
        GrammarCheckMotherTongue = null;
    }
}
