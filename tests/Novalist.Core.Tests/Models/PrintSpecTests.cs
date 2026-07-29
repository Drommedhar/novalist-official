using Novalist.Core.Models;
using Xunit;

namespace Novalist.Core.Tests.Models;

/// <summary>
/// The page a book is printed on.
///
/// A manuscript PDF is one page size with one margin all round because it is
/// read on a screen. A bound book is not, and none of the difference is a
/// preference - a file that gets it wrong is rejected by the printer or comes
/// back with text in the gutter.
/// </summary>
public class PrintSpecTests
{
    [Fact]
    public void TheDefaultIsTheManuscriptPage()
    {
        var spec = new PrintSpec();

        Assert.Equal(8.5, spec.TrimWidthInches);
        Assert.Equal(11.0, spec.TrimHeightInches);
        Assert.Equal(0, spec.BleedInches);
        Assert.Equal(8.5, spec.MediaWidthInches);
    }

    [Fact]
    public void PageOneIsARightHandPage()
    {
        // Every bound book opens on a recto, which is what makes odd pages
        // bind on the left.
        Assert.True(PrintSpec.IsRightHandPage(1));
        Assert.False(PrintSpec.IsRightHandPage(2));
        Assert.True(PrintSpec.IsRightHandPage(3));
    }

    [Fact]
    public void MirroredMarginsSwapOnFacingPages()
    {
        var spec = new PrintSpec
        {
            MarginInsideInches = 0.9,
            MarginOutsideInches = 0.5,
            GutterFromPageCount = false,
            GutterInches = 0.1
        };

        // Right-hand page: the binding is on its left.
        Assert.Equal(1.0, spec.LeftMarginInches(1, 200), 3);
        Assert.Equal(0.5, spec.RightMarginInches(1, 200), 3);

        // Left-hand page: the binding is on its right.
        Assert.Equal(0.5, spec.LeftMarginInches(2, 200), 3);
        Assert.Equal(1.0, spec.RightMarginInches(2, 200), 3);
    }

    [Fact]
    public void WithoutMirroringTheBindingEdgeStaysOnTheLeft()
    {
        var spec = new PrintSpec
        {
            MirrorMargins = false,
            MarginInsideInches = 0.9,
            MarginOutsideInches = 0.5,
            GutterFromPageCount = false,
            GutterInches = 0.1
        };

        Assert.Equal(spec.LeftMarginInches(1, 200), spec.LeftMarginInches(2, 200), 3);
        Assert.Equal(spec.RightMarginInches(1, 200), spec.RightMarginInches(2, 200), 3);
    }

    [Theory]
    [InlineData(80, 0.375)]
    [InlineData(150, 0.375)]
    [InlineData(151, 0.5)]
    [InlineData(300, 0.5)]
    [InlineData(400, 0.625)]
    [InlineData(600, 0.75)]
    [InlineData(900, 0.875)]
    public void TheGutterGrowsWithTheBook(int pages, double expected)
        // A thick book curves more at the spine. The steps are the ones
        // print-on-demand services publish.
        => Assert.Equal(expected, new PrintSpec().EffectiveGutterInches(pages), 3);

    [Fact]
    public void AFixedGutterIgnoresThePageCount()
    {
        var spec = new PrintSpec { GutterFromPageCount = false, GutterInches = 0.25 };

        Assert.Equal(0.25, spec.EffectiveGutterInches(50), 3);
        Assert.Equal(0.25, spec.EffectiveGutterInches(900), 3);
    }

    [Fact]
    public void ANegativeGutterIsNoGutter()
        => Assert.Equal(0, new PrintSpec
        {
            GutterFromPageCount = false,
            GutterInches = -1
        }.EffectiveGutterInches(100));

    [Fact]
    public void BleedGrowsTheSheetOnEveryEdge()
    {
        var spec = new PrintSpec
        {
            TrimWidthInches = 6,
            TrimHeightInches = 9,
            BleedInches = 0.125
        };

        Assert.Equal(6.25, spec.MediaWidthInches, 3);
        Assert.Equal(9.25, spec.MediaHeightInches, 3);
    }

    [Theory]
    [InlineData("us-trade", 6.0, 9.0)]
    [InlineData("US-TRADE", 6.0, 9.0)]
    [InlineData("  a5  ", 5.83, 8.27)]
    [InlineData("pocket", 4.25, 6.87)]
    public void ATrimCanBeNamed(string name, double width, double height)
    {
        var trim = PrintSpec.NamedTrim(name);
        Assert.NotNull(trim);
        Assert.Equal(width, trim!.Value.Width, 2);
        Assert.Equal(height, trim.Value.Height, 2);
    }

    [Fact]
    public void AnUnknownTrimNameIsNotATrim()
    {
        Assert.Null(PrintSpec.NamedTrim("enormous"));
        Assert.Null(PrintSpec.NamedTrim(null));
    }

    [Fact]
    public void WithTrimAppliesANameAndLeavesAnUnknownOneAlone()
    {
        var custom = new PrintSpec { TrimWidthInches = 7.1, TrimHeightInches = 10.2 };

        Assert.Equal(6.0, custom.WithTrim("us-trade").TrimWidthInches, 3);
        // A bad string must not silently reset a size the writer measured.
        Assert.Equal(7.1, custom.WithTrim("nonsense").TrimWidthInches, 3);
    }

    [Fact]
    public void EveryNamedTrimResolves()
        => Assert.All(PrintSpec.TrimNames, name => Assert.NotNull(PrintSpec.NamedTrim(name)));
}
