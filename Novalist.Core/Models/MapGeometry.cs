using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

public sealed class MapPoint
{
    [JsonPropertyName("x")]
    public double X { get; set; }
    [JsonPropertyName("y")]
    public double Y { get; set; }
}

/// <summary>
/// A road or river drawn as a smoothed (Catmull-Rom) polyline. The
/// <see cref="Preset"/> selects a visual profile (casing, fill bands, lane
/// markings) defined in the map WebView; each point carries its own width so
/// the spline can taper, and an optional per-point type override so it can
/// morph along its length.
/// </summary>
public sealed class MapSpline
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>"road" or "river" — selects which profile table to use.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "road";

    /// <summary>Profile preset key, e.g. "motorway", "residential", "river", "canal".</summary>
    [JsonPropertyName("preset")]
    public string Preset { get; set; } = string.Empty;

    [JsonPropertyName("closed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Closed { get; set; }

    /// <summary>Centerline override: "" = preset default, else "none", "single",
    /// "dashed", "double", "solid-dashed". Edge lines always come from the preset.</summary>
    [JsonPropertyName("markingStyle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MarkingStyle { get; set; }

    /// <summary>Hex colour override for the casing (under-road outline).
    /// Null = use the preset (and per-knot type cross-fade).</summary>
    [JsonPropertyName("casingColor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CasingColor { get; set; }

    /// <summary>Hex colour override for the road/river fill. Null = preset.</summary>
    [JsonPropertyName("fillColor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FillColor { get; set; }

    /// <summary>Hex colour override for all lane markings. Null = preset.</summary>
    [JsonPropertyName("markingColor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MarkingColor { get; set; }

    [JsonPropertyName("points")]
    public List<MapSplinePoint> Points { get; set; } = new();

    /// <summary>Visible-zoom-range floor; null/0 = no minimum.</summary>
    [JsonPropertyName("minZoom")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MinZoom { get; set; }

    /// <summary>Visible-zoom-range ceiling; null/0 = no maximum.</summary>
    [JsonPropertyName("maxZoom")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MaxZoom { get; set; }
}

public sealed class MapSplinePoint
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    /// <summary>Full width of the spline at this knot, in world units.</summary>
    [JsonPropertyName("width")]
    public double Width { get; set; } = 24;

    /// <summary>Optional preset override at this knot so the spline can blend
    /// into a different type along its length. Null = inherit the spline preset.</summary>
    [JsonPropertyName("typeOverride")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TypeOverride { get; set; }

    /// <summary>How softly the type change cross-fades over the segment leaving
    /// this knot: 0 = hard cut at the midpoint, 1 = full linear blend.</summary>
    [JsonPropertyName("blendFactor")]
    public double BlendFactor { get; set; } = 1.0;

    /// <summary>Optional centerline style for the segment leaving this knot —
    /// overrides the spline's <see cref="MapSpline.MarkingStyle"/>. Null = inherit.
    /// Values: "none", "single", "dashed", "double", "solid-dashed".</summary>
    [JsonPropertyName("markingStyle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MarkingStyle { get; set; }

    /// <summary>Corner sharpness: 0 = fully smooth (Catmull-Rom), 1 = hard corner
    /// (straight in, straight out). Scales the knot's Hermite tangent magnitude.</summary>
    [JsonPropertyName("sharpness")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double Sharpness { get; set; }

    /// <summary>Optional tangent-direction override at this knot, in radians.
    /// Null = direction auto-derived from neighbours.</summary>
    [JsonPropertyName("angle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Angle { get; set; }
}

/// <summary>A closed-polygon terrain shape painted on the map — grass, forest,
/// concrete, sand, hills, mountain, water, etc. Flat colour fill with a
/// user-controlled feathered edge so adjacent shapes blend naturally.</summary>
public sealed class MapShape
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>"grass" | "forest" | "concrete" | "sand" | "hills" | "mountain"
    /// | "water" | "custom" — seeds the colour at creation time.</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "grass";

    /// <summary>Hex fill colour; seeded from the type preset, user-overridable.</summary>
    [JsonPropertyName("color")]
    public string Color { get; set; } = "#8db360";

    /// <summary>True = Catmull-Rom curve through the points; false = straight polygon.</summary>
    [JsonPropertyName("smooth")]
    public bool Smooth { get; set; } = true;

    /// <summary>Feathered-edge width in world units. 0 = crisp edge.</summary>
    [JsonPropertyName("blendStrength")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double BlendStrength { get; set; }

    /// <summary>Closed polygon vertices (>= 3).</summary>
    [JsonPropertyName("points")]
    public List<MapPoint> Points { get; set; } = new();

    /// <summary>Visible-zoom-range floor; null/0 = no minimum.</summary>
    [JsonPropertyName("minZoom")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MinZoom { get; set; }

    /// <summary>Visible-zoom-range ceiling; null/0 = no maximum.</summary>
    [JsonPropertyName("maxZoom")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MaxZoom { get; set; }
}
