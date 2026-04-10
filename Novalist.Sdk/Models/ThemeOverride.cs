using Avalonia.Styling;

namespace Novalist.Sdk.Models;

/// <summary>
/// Describes a theme override contributed by an extension.
/// </summary>
public sealed class ThemeOverride
{
    /// <summary>Display name of the theme.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Avalonia Styles object containing resource overrides.
    /// The host merges this into Application.Styles.
    /// </summary>
    public Styles? Styles { get; init; }

    /// <summary>
    /// Alternative: path to a .axaml resource file within the extension folder.
    /// The host loads and merges this at startup.
    /// </summary>
    public string? ResourcePath { get; init; }

    /// <summary>
    /// Optional default accent color for this theme (e.g. "#5865F2").
    /// Users can override this in settings.
    /// </summary>
    public string? AccentColor { get; init; }
}
