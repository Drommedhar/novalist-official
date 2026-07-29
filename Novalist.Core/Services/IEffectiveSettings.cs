using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>
/// Read-only view of the settings that can be overridden per-project.
/// Each getter resolves to the active project's override when set, otherwise
/// the global value. Implemented by <see cref="AppSettings"/> (returns its own
/// values directly) and by <see cref="EffectiveSettings"/> (resolves
/// override ?? global).
/// </summary>
public interface IEffectiveSettings
{
    string Language { get; }
    string Theme { get; }
    string? AccentColor { get; }

    string EditorFontFamily { get; }
    double EditorFontSize { get; }
    double EditorLineHeight { get; }
    bool ReadabilityHighlighting { get; }
    double ReadAloudRate { get; }
    string? ReadAloudVoiceUri { get; }
    double EditorLetterSpacing { get; }
    double EditorParagraphSpacing { get; }
    /// <summary>Whether the editor dims every paragraph but the current one.</summary>
    bool ComposeDimming { get; }

    bool TypewriterScrollEnabled { get; }
    string TypewriterScrollAnchor { get; }
    bool PageViewEnabled { get; }

    bool EnableBookParagraphSpacing { get; }
    bool EnableBookWidth { get; }
    string BookPageFormat { get; }
    double? BookTextBlockWidth { get; }
    string BookFontFamily { get; }
    double BookFontSize { get; }

    string AutoReplacementLanguage { get; }

    /// <summary>The name put on a suggested edit.</summary>
    string ReviewerName { get; }
    List<AutoReplacementPair> AutoReplacements { get; }
    bool DialogueCorrectionEnabled { get; }
    bool GrammarCheckEnabled { get; }
    bool SpellCheckEnabled { get; }

    /// <summary>Language tags the spell checker loads. Falls back to the writing
    /// language, so this is never empty.</summary>
    IReadOnlyList<string> SpellCheckLanguages { get; }
    string? GrammarCheckApiUrl { get; }
    string? GrammarCheckApiKey { get; }
    string? GrammarCheckUsername { get; }
    bool GrammarCheckPickyMode { get; }
    string? GrammarCheckMotherTongue { get; }
}
