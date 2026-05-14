using Avalonia.Data.Converters;

namespace Novalist.Desktop.ViewModels;

/// <summary>Small glyph converters for the layer panel rows.</summary>
public static class MapConverters
{
    /// <summary>Expanded → ▾, collapsed → ▸.</summary>
    public static readonly IValueConverter ChevronGlyph =
        new FuncValueConverter<bool, string>(expanded => expanded ? "▾" : "▸");

    /// <summary>Locked → filled square, unlocked → hollow square (geometric, not emoji).</summary>
    public static readonly IValueConverter LockGlyph =
        new FuncValueConverter<bool, string>(locked => locked ? "▣" : "▢");

    /// <summary>Visible → filled circle, hidden → hollow circle (geometric, not emoji).</summary>
    public static readonly IValueConverter EyeGlyph =
        new FuncValueConverter<bool, string>(hidden => hidden ? "○" : "●");

    /// <summary>0..1 opacity → 0..100 percentage for the compact NumericUpDown.</summary>
    public static readonly IValueConverter OpacityToPercent =
        new FuncValueConverter<double, decimal>(o => (decimal)System.Math.Round(o * 100));
}
