using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Novalist.Desktop.Converters;

public sealed class StringToGeometryConverter : IValueConverter
{
    public static readonly StringToGeometryConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && !string.IsNullOrEmpty(s))
        {
            // Skip emoji / non-SVG-path strings — valid SVG path data starts with a letter command (M, m, L, etc.)
            if (s.Length > 0 && !char.IsAsciiLetter(s[0]))
                return null;
            try { return Geometry.Parse(s); }
            catch { return null; }
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
