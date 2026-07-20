namespace Novalist.Sdk.Models;

/// <summary>
/// Describes a theme an extension contributes. On the Electron host only the
/// portable <see cref="AccentColor"/> is applied.
/// </summary>
public sealed class ThemeOverride
{
    /// <summary>Display name of the theme.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional path to a CSS resource file within the extension folder.</summary>
    public string? ResourcePath { get; init; }

    /// <summary>
    /// Optional default accent color for this theme (e.g. "#5865F2").
    /// Users can override this in settings.
    /// </summary>
    public string? AccentColor { get; init; }
}
