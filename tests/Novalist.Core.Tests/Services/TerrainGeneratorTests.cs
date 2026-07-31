using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// A generated first coastline. The case that matters most is the seed: a
/// generator a writer cannot get back to is one they cannot trust.
/// </summary>
public class TerrainGeneratorTests
{
    private static TerrainRequest Request(int seed = 42) => new(seed, 1000, 800);

    [Fact]
    public void TheSameSeedMakesTheSameLand()
    {
        // Down to the ids, so two runs of one seed produce files that compare
        // equal rather than files that only look alike.
        var first = TerrainGenerator.Generate(Request());
        var second = TerrainGenerator.Generate(Request());

        Assert.Equal(first.Shapes.Count, second.Shapes.Count);
        Assert.Equal(
            first.Shapes.Select(s => s.Id),
            second.Shapes.Select(s => s.Id));
        Assert.Equal(
            first.Shapes[1].Points.Select(p => (p.X, p.Y)),
            second.Shapes[1].Points.Select(p => (p.X, p.Y)));
        Assert.Equal(first.Settlements, second.Settlements);
    }

    [Fact]
    public void ADifferentSeedMakesDifferentLand()
    {
        var first = TerrainGenerator.Generate(Request(1));
        var second = TerrainGenerator.Generate(Request(2));

        Assert.NotEqual(
            first.Shapes[1].Points.Select(p => (p.X, p.Y)),
            second.Shapes[1].Points.Select(p => (p.X, p.Y)));
    }

    [Fact]
    public void TheSeaIsUnderneathEverything()
    {
        // Drawn first, so the land sits on it rather than beside it.
        var result = TerrainGenerator.Generate(Request());

        Assert.Equal("water", result.Shapes[0].Type);
        Assert.Equal(4, result.Shapes[0].Points.Count);
    }

    [Fact]
    public void TheCoastlineIsAClosedShapeInsideTheCanvas()
    {
        var result = TerrainGenerator.Generate(Request());

        var coast = result.Shapes[1];
        Assert.Equal("grass", coast.Type);
        Assert.True(coast.Points.Count > 3);
        // Centred on the origin, which is where a map opens.
        Assert.All(coast.Points, p =>
        {
            Assert.InRange(p.X, -500, 500);
            Assert.InRange(p.Y, -400, 400);
        });
    }

    [Fact]
    public void TheLandIsCentredOnTheOrigin()
    {
        // A map opens looking at (0,0). Land generated from there outward puts
        // most of itself off screen, and a writer who pressed Generate sees a
        // corner of a coastline in the bottom right.
        var result = TerrainGenerator.Generate(Request());

        var xs = result.Shapes[1].Points.Select(p => p.X).ToList();
        var ys = result.Shapes[1].Points.Select(p => p.Y).ToList();
        Assert.InRange((xs.Min() + xs.Max()) / 2, -60, 60);
        Assert.InRange((ys.Min() + ys.Max()) / 2, -60, 60);
    }

    [Fact]
    public void MoreLandmassMakesMoreLand()
    {
        var small = TerrainGenerator.Generate(Request() with { Landmass = 0.25 });
        var large = TerrainGenerator.Generate(Request() with { Landmass = 0.85 });

        Assert.True(Spread(large.Shapes[1]) > Spread(small.Shapes[1]));
    }

    [Fact]
    public void ALandmassOutsideTheRangeIsBroughtBackIntoIt()
    {
        // Nonsense in, a map out: zero landmass would be an empty sea and a
        // writer asking for it has made a typo, not a decision.
        var none = TerrainGenerator.Generate(Request() with { Landmass = 0 });
        var all = TerrainGenerator.Generate(Request() with { Landmass = 5 });

        Assert.True(Spread(none.Shapes[1]) > 0);
        Assert.True(Spread(all.Shapes[1]) > 0);
    }

    [Fact]
    public void RiversAreMadeAndEachIsALine()
    {
        var result = TerrainGenerator.Generate(Request() with { Rivers = 4 });

        Assert.Equal(4, result.Rivers.Count);
        Assert.All(result.Rivers, r =>
        {
            Assert.Equal("river", r.Kind);
            // One point is not a river.
            Assert.True(r.Points.Count >= 2);
        });
    }

    [Fact]
    public void ForestsAreMadeOnRequest()
    {
        var result = TerrainGenerator.Generate(Request() with { Forests = 6 });

        Assert.Equal(6, result.Shapes.Count(s => s.Type == "forest"));
    }

    [Fact]
    public void AskingForNothingMakesNothingRatherThanThrowing()
    {
        var result = TerrainGenerator.Generate(
            Request() with { Rivers = 0, Forests = 0, Settlements = 0 });

        Assert.Empty(result.Rivers);
        Assert.Empty(result.Settlements);
        Assert.DoesNotContain(result.Shapes, s => s.Type == "forest");
        // The sea, the land and the high ground are always there.
        Assert.Equal(3, result.Shapes.Count);
    }

    [Fact]
    public void AskingForALessThanNothingIsTreatedAsNothing()
    {
        var result = TerrainGenerator.Generate(
            Request() with { Rivers = -3, Forests = -2, Settlements = -1 });

        Assert.Empty(result.Rivers);
        Assert.Empty(result.Settlements);
    }

    [Fact]
    public void SettlementsSitOnLand()
    {
        // A town in the sea is the one thing that makes a generated map look
        // generated.
        var result = TerrainGenerator.Generate(Request());
        var coast = result.Shapes[1].Points;

        Assert.NotEmpty(result.Settlements);
        Assert.All(result.Settlements, s => Assert.True(InsideOf(coast, s.X, s.Y)));
    }

    [Fact]
    public void SettlementsAreNotStackedOnTopOfEachOther()
    {
        // Two towns in one bay is one town and a mistake.
        var result = TerrainGenerator.Generate(Request() with { Settlements = 6 });

        var placed = result.Settlements.ToList();
        for (var i = 0; i < placed.Count; i++)
        {
            for (var j = i + 1; j < placed.Count; j++)
            {
                var dx = placed[i].X - placed[j].X;
                var dy = placed[i].Y - placed[j].Y;
                Assert.True(Math.Sqrt(dx * dx + dy * dy) > 1);
            }
        }
    }

    [Fact]
    public void TheFirstSettlementIsTheBiggest()
    {
        // A map with no city on it reads as a map of nowhere in particular.
        var result = TerrainGenerator.Generate(Request());

        Assert.Equal("city", result.Settlements[0].Size);
    }

    [Fact]
    public void ACanvasWithNoSizeStillProducesAMap()
    {
        var result = TerrainGenerator.Generate(new TerrainRequest(1, 0, 0));

        Assert.NotEmpty(result.Shapes);
    }

    private static double Spread(Novalist.Core.Models.MapShape shape)
    {
        var xs = shape.Points.Select(p => p.X).ToList();
        var ys = shape.Points.Select(p => p.Y).ToList();
        return (xs.Max() - xs.Min()) + (ys.Max() - ys.Min());
    }

    private static bool InsideOf(List<Novalist.Core.Models.MapPoint> polygon, double x, double y)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var (xi, yi) = (polygon[i].X, polygon[i].Y);
            var (xj, yj) = (polygon[j].X, polygon[j].Y);
            if (yi > y != yj > y && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }
}
