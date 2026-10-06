using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// A user-authored cross-section profile — the band/marking stack swept along a
/// spline (e.g. sidewalk | curb | lanes | median | lanes | curb | sidewalk).
/// Mirrors the built-in profile shape used by the map WebView's renderer.
/// </summary>
public sealed class MapProfile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>"road" or "river" — which builtin table it sits alongside.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "road";

    [JsonPropertyName("defaultWidth")]
    public double DefaultWidth { get; set; } = 24;

    /// <summary>Dark under-road outline.</summary>
    [JsonPropertyName("casingColor")]
    public string CasingColor { get; set; } = "#3f3413";

    /// <summary>World units the casing extends beyond the road edge on each side.</summary>
    [JsonPropertyName("casingExtra")]
    public double CasingExtra { get; set; } = 3;

    /// <summary>Fill bands, inner→outer. <c>From</c>/<c>To</c> are half-width
    /// fractions in -1..1 (0 = centerline, ±1 = edge).</summary>
    [JsonPropertyName("bands")]
    public List<MapProfileBand> Bands { get; set; } = new();

    [JsonPropertyName("markings")]
    public List<MapProfileMarking> Markings { get; set; } = new();

    /// <summary>If true, the spline is drawn with straight segments (no smoothing).</summary>
    [JsonPropertyName("straight")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Straight { get; set; }
}

public sealed class MapProfileBand
{
    /// <summary>Inner edge, half-width fraction (-1..1).</summary>
    [JsonPropertyName("from")]
    public double From { get; set; } = -1;

    /// <summary>Outer edge, half-width fraction (-1..1).</summary>
    [JsonPropertyName("to")]
    public double To { get; set; } = 1;

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#cccccc";
}

public sealed class MapProfileMarking
{
    /// <summary>Half-width fraction (-1..1) the line runs at.</summary>
    [JsonPropertyName("offset")]
    public double Offset { get; set; }

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#ffffff";

    /// <summary>Stroke width in screen pixels.</summary>
    [JsonPropertyName("width")]
    public double Width { get; set; } = 1.5;

    /// <summary>Optional dash pattern (screen px). Empty/null = solid.</summary>
    [JsonPropertyName("dash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<double>? Dash { get; set; }
}
