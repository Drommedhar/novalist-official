namespace Novalist.Sdk.Models;

/// <summary>
/// Describes a theme an extension contributes. A theme restates Novalist's
/// palette: supply <see cref="Tokens"/> for the common case, or point
/// <see cref="ResourcePath"/> at a stylesheet in the extension folder when the
/// theme needs rules a token map cannot express. Contributed themes appear in
/// Settings, Appearance, Theme alongside the built-in ones.
/// </summary>
public sealed class ThemeOverride
{
    /// <summary>Display name of the theme, as it appears in the theme dropdown.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Design-token overrides, keyed by custom-property name
    /// (<c>--nl-surface-window</c>, <c>--nl-text</c>, <c>--nl-accent</c>, ...).
    /// Only the <c>--nl-*</c> tier is honoured; unlisted tokens keep their
    /// default value, so a theme can restate the whole palette or just a corner
    /// of it. Keys outside the tier and values carrying CSS punctuation are
    /// dropped — use <see cref="ResourcePath"/> for anything a declaration
    /// cannot hold.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Tokens { get; init; }

    /// <summary>
    /// Optional path to a <c>.css</c> file inside the extension folder, relative
    /// to its root. The stylesheet is injected while the theme is selected and
    /// removed when it is not, so it can carry arbitrary rules without leaking
    /// into the other themes.
    /// </summary>
    public string? ResourcePath { get; init; }

    /// <summary>
    /// Optional default accent color for this theme (e.g. "#5865F2"). A
    /// shorthand for the <c>--nl-accent</c> token; users can still override it
    /// with their own accent in settings.
    /// </summary>
    public string? AccentColor { get; init; }
}
