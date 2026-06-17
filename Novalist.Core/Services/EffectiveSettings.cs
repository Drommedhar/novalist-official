using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>
/// Resolves each overridable setting to <c>projectOverride ?? global</c>.
/// Holds a reference to the live <see cref="AppSettings"/> and a getter for the
/// active project's <see cref="SettingsOverrides"/> (null when no project is
/// open), so values stay correct as either side changes without rebuilding.
/// </summary>
public sealed class EffectiveSettings : IEffectiveSettings
{
    private readonly Func<AppSettings> _global;
    private readonly Func<SettingsOverrides?> _overrides;

    public EffectiveSettings(Func<AppSettings> global, Func<SettingsOverrides?> overrides)
    {
        _global = global;
        _overrides = overrides;
    }

    private SettingsOverrides? O => _overrides();
    private AppSettings G => _global();

    public string Language => O?.Language ?? G.Language;
    public string Theme => O?.Theme ?? G.Theme;
    public string? AccentColor => O?.AccentColor ?? G.AccentColor;

    public string EditorFontFamily => O?.EditorFontFamily ?? G.EditorFontFamily;
    public double EditorFontSize => O?.EditorFontSize ?? G.EditorFontSize;
    public bool TypewriterScrollEnabled => O?.TypewriterScrollEnabled ?? G.TypewriterScrollEnabled;
    public string TypewriterScrollAnchor => O?.TypewriterScrollAnchor ?? G.TypewriterScrollAnchor;
    public bool PageViewEnabled => O?.PageViewEnabled ?? G.PageViewEnabled;

    public bool EnableBookParagraphSpacing => O?.EnableBookParagraphSpacing ?? G.EnableBookParagraphSpacing;
    public bool EnableBookWidth => O?.EnableBookWidth ?? G.EnableBookWidth;
    public string BookPageFormat => O?.BookPageFormat ?? G.BookPageFormat;
    public double? BookTextBlockWidth => O?.BookTextBlockWidth ?? G.BookTextBlockWidth;
    public string BookFontFamily => O?.BookFontFamily ?? G.BookFontFamily;
    public double BookFontSize => O?.BookFontSize ?? G.BookFontSize;

    public string AutoReplacementLanguage => O?.AutoReplacementLanguage ?? G.AutoReplacementLanguage;
    public List<AutoReplacementPair> AutoReplacements => O?.AutoReplacements ?? G.AutoReplacements;
    public bool DialogueCorrectionEnabled => O?.DialogueCorrectionEnabled ?? G.DialogueCorrectionEnabled;
    public bool GrammarCheckEnabled => O?.GrammarCheckEnabled ?? G.GrammarCheckEnabled;
    public string? GrammarCheckApiUrl => O?.GrammarCheckApiUrl ?? G.GrammarCheckApiUrl;
    public string? GrammarCheckApiKey => O?.GrammarCheckApiKey ?? G.GrammarCheckApiKey;
    public string? GrammarCheckUsername => O?.GrammarCheckUsername ?? G.GrammarCheckUsername;
    public bool GrammarCheckPickyMode => O?.GrammarCheckPickyMode ?? G.GrammarCheckPickyMode;
    public string? GrammarCheckMotherTongue => O?.GrammarCheckMotherTongue ?? G.GrammarCheckMotherTongue;
}
