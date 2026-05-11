using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace Novalist.Desktop.Converters;

/// <summary>
/// Background for a scene/chapter tree row:
/// - If selected → selection brush.
/// - Else if label-color set → tinted (low-alpha) version of that color.
/// - Else transparent.
/// </summary>
public class SceneRowBackgroundConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var isSelected = values.Count > 0 && values[0] is bool b && b;
        var color = values.Count > 1 ? values[1] as string : null;

        if (isSelected)
        {
            if (Application.Current is { } app
                && app.TryGetResource("ListBoxItemSelectedBackground", ThemeVariant.Dark, out var sel)
                && sel is IBrush br)
                return br;
            return new SolidColorBrush(Color.FromArgb(120, 100, 150, 200));
        }

        if (!string.IsNullOrWhiteSpace(color))
        {
            try
            {
                var c = Color.Parse(color);
                return new SolidColorBrush(Color.FromArgb(48, c.R, c.G, c.B)); // ~19% alpha tint
            }
            catch { /* fall through */ }
        }
        return Brushes.Transparent;
    }
}
