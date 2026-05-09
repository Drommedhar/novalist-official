using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Novalist.Desktop.ViewModels;

namespace Novalist.Desktop.Converters;

public class ToastSeverityToBrushConverter : IValueConverter
{
    public static readonly ToastSeverityToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var resourceKey = value switch
        {
            ToastSeverity.Success => "SuccessBrush",
            ToastSeverity.Warning => "WarningBrush",
            ToastSeverity.Error => "DangerBrush",
            _ => "AccentBrush"
        };
        if (Application.Current?.Resources.TryGetResource(resourceKey, Application.Current.ActualThemeVariant, out var res) == true && res is IBrush b)
            return b;
        return Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
