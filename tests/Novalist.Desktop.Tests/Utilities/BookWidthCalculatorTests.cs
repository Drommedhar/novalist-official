using NSubstitute;
using Novalist.Core.Services;
using Novalist.Desktop.Tests;
using Novalist.Desktop.Utilities;
using Xunit;

namespace Novalist.Desktop.Tests.Utilities;

[Collection("Avalonia")]
public class BookWidthCalculatorTests
{
    [AvaloniaTheory]
    [InlineData("Digest5_5x8_5", null, 4.3)]
    [InlineData("A5", null, 4.63)]
    [InlineData("MassMarket", null, 3.35)]
    [InlineData("USTrade6x9", null, 4.75)]
    [InlineData("unknown", null, 4.75)]      // default
    [InlineData("Custom", 7.0, 7.0)]
    [InlineData("Custom", 0.0, 4.75)]        // custom <= 0 -> default
    public void GetTextBlockWidthInches(string format, double? custom, double expected)
        => Assert.Equal(expected, BookWidthCalculator.GetTextBlockWidthInches(format, custom));

    [AvaloniaTheory]
    [InlineData("USTrade6x9", "US Trade (6×9)")]
    [InlineData("Digest5_5x8_5", "Digest (5.5×8.5)")]
    [InlineData("A5", "A5 (5.83×8.27)")]
    [InlineData("MassMarket", "Mass Market (4.25×6.87)")]
    [InlineData("Custom", "Custom")]
    [InlineData("weird", "weird")]
    public void GetPageFormatDisplayName(string format, string expected)
        => Assert.Equal(expected, BookWidthCalculator.GetPageFormatDisplayName(format));

    [AvaloniaFact]
    public void PageFormats_AreListed()
        => Assert.Contains("USTrade6x9", BookWidthCalculator.PageFormats);

    [AvaloniaFact]
    public void MeasureAverageCharWidth_NonNegative()
    {
        var w = BookWidthCalculator.MeasureAverageCharWidth("Arial", 12);
        Assert.True(w >= 0);
    }

    private static IEffectiveSettings Settings()
    {
        var s = Substitute.For<IEffectiveSettings>();
        s.BookPageFormat.Returns("USTrade6x9");
        s.BookTextBlockWidth.Returns((double?)null);
        s.BookFontFamily.Returns("Arial");
        s.BookFontSize.Returns(12.0);
        s.EditorFontFamily.Returns("Arial");
        s.EditorFontSize.Returns(14.0);
        return s;
    }

    [AvaloniaFact]
    public void Calculate_ReturnsAtLeastMinWidth()
        => Assert.True(BookWidthCalculator.Calculate(Settings()) >= 300.0);

    [AvaloniaFact]
    public void EstimateCharsPerLine_ReturnsPositive()
        => Assert.True(BookWidthCalculator.EstimateCharsPerLine(Settings()) > 0);
}
