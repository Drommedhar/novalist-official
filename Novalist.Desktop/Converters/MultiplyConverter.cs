using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Novalist.Desktop.Converters;

/// <summary>Multiplies numeric inputs together (used for fraction × container width).</summary>
public class MultiplyConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, System.Type targetType, object? parameter, CultureInfo culture)
    {
        double result = 1.0;
        bool anyValid = false;
        foreach (var v in values)
        {
            switch (v)
            {
                case double d: result *= d; anyValid = true; break;
                case float f: result *= f; anyValid = true; break;
                case int i: result *= i; anyValid = true; break;
                case long l: result *= l; anyValid = true; break;
            }
        }
        if (!anyValid) return BindingOperations.DoNothing;
        return result;
    }
}
