using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>What to make, and how much of it.</summary>
/// <param name="Seed">
/// The same seed makes the same land. That is the whole point: a writer who
/// likes a coastline must be able to get it back, and a map that changes every
/// time it is generated is a slot machine.
/// </param>
/// <param name="Landmass">
/// 0.2 to 0.9 - roughly how much of the canvas is land. An island at the low
/// end, a continent with bays at the high end.
/// </param>
/// <param name="Rivers">How many rivers to run from high ground to the sea.</param>
/// <param name="Settlements">How many settlements to place.</param>
public sealed record TerrainRequest(
    int Seed,
    double Width,
    double Height,
    double Landmass = 0.55,
    int Rivers = 3,
    int Forests = 4,
    int Settlements = 5);

/// <summary>Land, water, woods, rivers and somewhere to live.</summary>
/// <param name="Settlements">
/// Where a settlement belongs, and roughly how big. The caller places the pin,
/// because only it knows what a pin looks like.
/// </param>
public sealed record TerrainResult(
    IReadOnlyList<MapShape> Shapes,
    IReadOnlyList<MapSpline> Rivers,
    IReadOnlyList<(double X, double Y, string Size)> Settlements);

/// <summary>
/// A first coastline, so a map does not start as a blank canvas.
///
/// Novalist could scatter vegetation and stamp buildings, and every coastline,
/// river and terrain polygon still had to be drawn by hand from nothing - which
/// is the part of mapmaking that stops a writer who is not an illustrator.
///
/// What comes out is meant to be edited. The shapes are ordinary map shapes and
/// the rivers ordinary splines, on their own layer, so the first thing a writer
/// does can be to drag a headland about rather than to accept the machine's
/// idea of their world.
///
/// Deterministic from the seed, with no randomness the caller cannot reproduce.
/// A generator a writer cannot get back to is one they cannot trust.
/// </summary>
public static class TerrainGenerator
{
    /// <summary>How many vertices a coastline gets. Enough to read as a coast,
    /// few enough that a writer can grab one and move it.</summary>
    private const int CoastPoints = 96;

    public static TerrainResult Generate(TerrainRequest request)
    {
        var width = Math.Max(1, request.Width);
        var height = Math.Max(1, request.Height);
        var landmass = Math.Clamp(request.Landmass, 0.2, 0.9);
        var random = new Random(request.Seed);

        // The sea first, so everything else sits on top of it.
        var shapes = new List<MapShape>
        {
            new()
            {
                Id = Id(random),
                Type = "water",
                Color = "#3f6f8f",
                Smooth = false,
                Points =
                [
                    new MapPoint { X = 0, Y = 0 },
                    new MapPoint { X = width, Y = 0 },
                    new MapPoint { X = width, Y = height },
                    new MapPoint { X = 0, Y = height }
                ]
            }
        };

        var coast = Coastline(random, width, height, landmass);
        shapes.Add(new MapShape
        {
            Id = Id(random),
            Type = "grass",
            Color = "#8db360",
            Smooth = true,
            Points = [.. coast]
        });

        // Higher ground inland, which is where the rivers will start.
        var centreX = width / 2;
        var centreY = height / 2;
        var hills = Blob(random, centreX, centreY, Math.Min(width, height) * landmass * 0.22, 0.35);
        shapes.Add(new MapShape
        {
            Id = Id(random),
            Type = "hills",
            Color = "#a89a6a",
            Smooth = true,
            Points = [.. hills]
        });

        for (var i = 0; i < Math.Max(0, request.Forests); i++)
        {
            var angle = random.NextDouble() * Math.Tau;
            var reach = Math.Min(width, height) * landmass * (0.18 + random.NextDouble() * 0.22);
            shapes.Add(new MapShape
            {
                Id = Id(random),
                Type = "forest",
                Color = "#4d7a3e",
                Smooth = true,
                Points = [.. Blob(
                    random,
                    centreX + Math.Cos(angle) * reach,
                    centreY + Math.Sin(angle) * reach,
                    Math.Min(width, height) * 0.07 * (0.7 + random.NextDouble()),
                    0.4)]
            });
        }

        // Rivers run from the high ground outward until they meet the coast.
        var rivers = new List<MapSpline>();
        for (var i = 0; i < Math.Max(0, request.Rivers); i++)
        {
            var angle = random.NextDouble() * Math.Tau;
            rivers.Add(new MapSpline
            {
                Id = Id(random),
                Kind = "river",
                Preset = "river",
                Points = [.. River(random, centreX, centreY, angle, coast)]
            });
        }

        // Settlements: on land, and not on top of each other. A generator that
        // stacks two towns in one bay has made one town and a mistake.
        var settlements = new List<(double, double, string)>();
        var minGap = Math.Min(width, height) * 0.12;
        for (var attempt = 0; attempt < 400 && settlements.Count < Math.Max(0, request.Settlements); attempt++)
        {
            var x = random.NextDouble() * width;
            var y = random.NextDouble() * height;
            if (!Inside(coast, x, y)) continue;
            if (settlements.Any(s => Distance(s.Item1, s.Item2, x, y) < minGap)) continue;

            var size = settlements.Count == 0 ? "city" : random.NextDouble() < 0.3 ? "town" : "village";
            settlements.Add((x, y, size));
        }

        // Centred on the origin, because that is where a map opens. Generated
        // land placed from (0,0) outward puts most of itself off screen, and a
        // writer who pressed Generate sees a corner of a coastline.
        var offsetX = -width / 2;
        var offsetY = -height / 2;
        foreach (var shape in shapes)
            foreach (var point in shape.Points)
            {
                point.X += offsetX;
                point.Y += offsetY;
            }
        foreach (var river in rivers)
            foreach (var point in river.Points)
            {
                point.X += offsetX;
                point.Y += offsetY;
            }

        return new TerrainResult(
            shapes,
            rivers,
            [.. settlements.Select(s => (s.Item1 + offsetX, s.Item2 + offsetY, s.Item3))]);
    }

    /// <summary>
    /// A closed coast: a circle pushed in and out by a few slow waves.
    ///
    /// Layered sine rather than noise on purpose - it is a dozen lines, it is
    /// exactly reproducible on any machine, and at this scale a reader cannot
    /// tell the difference between this and a proper noise field.
    /// </summary>
    private static List<MapPoint> Coastline(Random random, double width, double height, double landmass)
    {
        var centreX = width / 2;
        var centreY = height / 2;
        var radius = Math.Min(width, height) / 2 * landmass;

        // Three waves at different rates, each starting somewhere of its own.
        var phases = new[] { random.NextDouble() * Math.Tau, random.NextDouble() * Math.Tau, random.NextDouble() * Math.Tau };
        var rates = new[] { 3.0, 7.0, 13.0 };
        var depths = new[] { 0.18, 0.09, 0.05 };

        var points = new List<MapPoint>(CoastPoints);
        for (var i = 0; i < CoastPoints; i++)
        {
            var angle = Math.Tau * i / CoastPoints;
            var wobble = 1.0;
            for (var w = 0; w < rates.Length; w++)
                wobble += Math.Sin(angle * rates[w] + phases[w]) * depths[w];

            points.Add(new MapPoint
            {
                X = Math.Clamp(centreX + Math.Cos(angle) * radius * wobble, 0, width),
                Y = Math.Clamp(centreY + Math.Sin(angle) * radius * wobble, 0, height)
            });
        }
        return points;
    }

    /// <summary>A rounded patch: the same trick as the coast, smaller.</summary>
    private static List<MapPoint> Blob(Random random, double x, double y, double radius, double roughness)
    {
        var phase = random.NextDouble() * Math.Tau;
        var points = new List<MapPoint>(24);
        for (var i = 0; i < 24; i++)
        {
            var angle = Math.Tau * i / 24;
            var wobble = 1 + Math.Sin(angle * 4 + phase) * roughness * 0.5
                + Math.Sin(angle * 9 + phase * 2) * roughness * 0.25;
            points.Add(new MapPoint
            {
                X = x + Math.Cos(angle) * radius * wobble,
                Y = y + Math.Sin(angle) * radius * wobble
            });
        }
        return points;
    }

    /// <summary>
    /// A river from the high ground outward, wandering as it goes, stopping
    /// when it leaves the land. A river that runs on across the sea is the one
    /// thing that makes a generated map look generated.
    /// </summary>
    private static List<MapSplinePoint> River(
        Random random, double x, double y, double angle, List<MapPoint> coast)
    {
        // Narrowing as it goes would be backwards: a river gathers water on its
        // way to the sea, so it widens.
        var width = 6.0;
        var points = new List<MapSplinePoint> { new() { X = x, Y = y, Width = width } };
        var step = 14.0;

        for (var i = 0; i < 60; i++)
        {
            // Wander, but never turn back on itself: rivers meander, they do
            // not change their minds.
            angle += (random.NextDouble() - 0.5) * 0.5;
            x += Math.Cos(angle) * step;
            y += Math.Sin(angle) * step;
            if (!Inside(coast, x, y)) break;
            width += 0.4;
            points.Add(new MapSplinePoint { X = x, Y = y, Width = width });
        }

        // One point is not a river. Two makes a line to the sea.
        if (points.Count == 1)
            points.Add(new MapSplinePoint { X = x, Y = y, Width = width });
        return points;
    }

    /// <summary>Ray casting: is this point inside the closed polygon.</summary>
    private static bool Inside(List<MapPoint> polygon, double x, double y)
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

    private static double Distance(double ax, double ay, double bx, double by)
        => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    /// <summary>
    /// An id from the same stream as everything else, so a seed reproduces the
    /// ids too - two runs of one seed produce files that compare equal.
    /// </summary>
    private static string Id(Random random)
    {
        Span<byte> bytes = stackalloc byte[8];
        random.NextBytes(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
