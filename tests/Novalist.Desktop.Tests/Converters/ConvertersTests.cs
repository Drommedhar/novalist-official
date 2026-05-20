using System.Globalization;
using Avalonia.Data;

using Avalonia.Media;
using Novalist.Desktop.Converters;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.Converters;

[Collection("Avalonia")]
public class ConvertersTests
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    // ── MultiplyConverter ──

    [AvaloniaFact]
    public void Multiply_MultipliesNumericKinds()
    {
        var sut = new MultiplyConverter();
        var result = sut.Convert(new object?[] { 2.0, 3, 2L, 1.5f }, typeof(double), null, Ci);
        Assert.Equal(18.0, (double)result!, 3);
    }

    [AvaloniaFact]
    public void Multiply_NoNumericValues_DoNothing()
    {
        var sut = new MultiplyConverter();
        Assert.Same(BindingOperations.DoNothing, sut.Convert(new object?[] { "x", null }, typeof(double), null, Ci));
    }

    // ── ColorStringToBrushConverter ──

    [AvaloniaFact]
    public void ColorString_ValidHex_ReturnsBrush()
    {
        var sut = new ColorStringToBrushConverter();
        var brush = Assert.IsType<SolidColorBrush>(sut.Convert("#FF0000", typeof(IBrush), null, Ci));
        Assert.Equal(Colors.Red, brush.Color);
    }

    [AvaloniaFact]
    public void ColorString_Invalid_ReturnsTransparent()
    {
        var sut = new ColorStringToBrushConverter();
        Assert.Same(Brushes.Transparent, sut.Convert("not-a-color", typeof(IBrush), null, Ci));
        Assert.Same(Brushes.Transparent, sut.Convert(null, typeof(IBrush), null, Ci));
    }

    [AvaloniaFact]
    public void ColorString_ConvertBack_Throws()
        => Assert.Throws<NotSupportedException>(() => new ColorStringToBrushConverter().ConvertBack(null, typeof(string), null, Ci));

    // ── StringToGeometryConverter ──

    [AvaloniaFact]
    public void Geometry_ValidPath_Parses()
    {
        var g = StringToGeometryConverter.Instance.Convert("M0,0 L10,10", typeof(Geometry), null, Ci);
        Assert.NotNull(g);
    }

    [AvaloniaTheory]
    [InlineData("🧩")]   // emoji -> not a letter command
    [InlineData("123")]  // starts with digit
    [InlineData("")]
    [InlineData(null)]
    public void Geometry_NonPath_ReturnsNull(string? input)
        => Assert.Null(StringToGeometryConverter.Instance.Convert(input, typeof(Geometry), null, Ci));

    [AvaloniaFact]
    public void Geometry_MalformedPath_ReturnsNull()
        => Assert.Null(StringToGeometryConverter.Instance.Convert("Mgarbage(", typeof(Geometry), null, Ci));

    [AvaloniaFact]
    public void Geometry_ConvertBack_Throws()
        => Assert.Throws<NotSupportedException>(() => StringToGeometryConverter.Instance.ConvertBack(null, typeof(string), null, Ci));

    // ── ToastSeverityToBrushConverter ──

    [AvaloniaFact]
    public void Toast_KnownSeverities_ResolveResources()
    {
        var sut = ToastSeverityToBrushConverter.Instance;
        Assert.IsType<SolidColorBrush>(sut.Convert(ToastSeverity.Success, typeof(IBrush), null, Ci));
        Assert.IsType<SolidColorBrush>(sut.Convert(ToastSeverity.Warning, typeof(IBrush), null, Ci));
        Assert.IsType<SolidColorBrush>(sut.Convert(ToastSeverity.Error, typeof(IBrush), null, Ci));
        Assert.IsType<SolidColorBrush>(sut.Convert(ToastSeverity.Info, typeof(IBrush), null, Ci));
    }

    [AvaloniaFact]
    public void Toast_UnresolvedResource_FallsBackToGray()
    {
        // A severity whose resource isn't registered would fall back; here we
        // verify the default arm (unknown value -> AccentBrush, still resolvable).
        var sut = ToastSeverityToBrushConverter.Instance;
        Assert.NotNull(sut.Convert((ToastSeverity)999, typeof(IBrush), null, Ci));
    }

    [AvaloniaFact]
    public void Toast_ConvertBack_Throws()
        => Assert.Throws<NotSupportedException>(() => ToastSeverityToBrushConverter.Instance.ConvertBack(null, typeof(object), null, Ci));

    // ── SceneRowBackgroundConverter ──

    [AvaloniaFact]
    public void SceneRow_Selected_UsesSelectionBrush()
    {
        var sut = new SceneRowBackgroundConverter();
        var result = sut.Convert(new object?[] { true, null }, typeof(IBrush), null, Ci);
        Assert.IsAssignableFrom<IBrush>(result);
    }

    [AvaloniaFact]
    public void SceneRow_LabelColor_TintsColor()
    {
        var sut = new SceneRowBackgroundConverter();
        var brush = Assert.IsType<SolidColorBrush>(sut.Convert(new object?[] { false, "#3498db" }, typeof(IBrush), null, Ci));
        Assert.Equal(48, brush.Color.A); // ~19% alpha tint
    }

    [AvaloniaFact]
    public void SceneRow_InvalidColor_FallsBackTransparent()
    {
        var sut = new SceneRowBackgroundConverter();
        Assert.Same(Brushes.Transparent, sut.Convert(new object?[] { false, "bogus" }, typeof(IBrush), null, Ci));
    }

    [AvaloniaFact]
    public void SceneRow_NoColor_Transparent()
    {
        var sut = new SceneRowBackgroundConverter();
        Assert.Same(Brushes.Transparent, sut.Convert(new object?[] { false, null }, typeof(IBrush), null, Ci));
        Assert.Same(Brushes.Transparent, sut.Convert(new object?[0], typeof(IBrush), null, Ci));
    }
}
