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
            return Geometry.Parse(s);
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
