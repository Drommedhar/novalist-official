using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Novalist.Desktop.Converters;

public class RelativePathToBitmapConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string relativePath || string.IsNullOrEmpty(relativePath))
            return null;

        try
        {
            var fullPath = App.EntityService.GetImageFullPath(relativePath);
            if (File.Exists(fullPath))
                return new Bitmap(fullPath);
        }
        catch
        {
            // Ignore load failures
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class AbsolutePathToBitmapConverter : IValueConverter
{
    public static readonly AbsolutePathToBitmapConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string fullPath || string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            return null;
        try
        {
            return new Bitmap(fullPath);
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
