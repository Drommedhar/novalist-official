using Avalonia;
using Avalonia.Media;

namespace Novalist.Desktop.Utilities;

/// <summary>
/// Resolves a brush key from the active Avalonia theme dictionary to a
/// CSS-style hex string. Used by views that bridge to embedded WebViews
/// (EditorView, ManuscriptView, MapView) where the theme colors must be
/// pushed into HTML/CSS instead of binding directly.
/// </summary>
public static class ThemeColors
{
    /// <summary>Returns the brush's color as <c>#RRGGBB</c>, or <paramref name="fallback"/>
    /// if the key cannot be resolved.</summary>
    public static string Resolve(string brushKey, string fallback)
    {
        if (Application.Current is { } app
            && app.TryGetResource(brushKey, app.ActualThemeVariant, out var res)
            && res is ISolidColorBrush brush)
        {
            return Format(brush.Color);
        }
        return fallback;
    }

    public static string Format(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
