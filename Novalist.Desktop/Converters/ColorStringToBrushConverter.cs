using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Novalist.Desktop.Converters;

/// <summary>Converts "#RRGGBB" / named color strings to a SolidColorBrush.</summary>
public class ColorStringToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && !string.IsNullOrWhiteSpace(s))
        {
            try { return new SolidColorBrush(Color.Parse(s)); }
            catch { return Brushes.Transparent; }
        }
        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
