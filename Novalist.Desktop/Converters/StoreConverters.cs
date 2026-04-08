using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Novalist.Desktop.Services;

/// <summary>
/// Returns true when the bound SelectedTab value equals the ConverterParameter.
/// </summary>
public sealed class TabVisibilityConverter : IValueConverter
{
    public static readonly TabVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int selected && parameter is string paramStr && int.TryParse(paramStr, out var target))
            return selected == target;
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Returns SemiBold when the bound SelectedTab equals the ConverterParameter, Normal otherwise.
/// </summary>
public sealed class TabFontWeightConverter : IValueConverter
{
    public static readonly TabFontWeightConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int selected && parameter is string paramStr && int.TryParse(paramStr, out var target))
            return selected == target ? FontWeight.SemiBold : FontWeight.Normal;
        return FontWeight.Normal;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Joins a list of strings with ", " for display.
/// </summary>
public sealed class TagsJoinConverter : IValueConverter
{
    public static readonly TagsJoinConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is IEnumerable<string> tags)
            return string.Join(", ", tags);
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
