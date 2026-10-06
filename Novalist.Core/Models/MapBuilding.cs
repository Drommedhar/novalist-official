using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>A placed building — a typed footprint polygon that can optionally
/// carry a multi-floor interior plan. Buildings live on a layer node.</summary>
public sealed class MapBuilding
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>"rowHome" | "singleFamily" | "school" | "police" | "fireStation"
    /// | "hall" | "playground" | "trainStation" — drives the footprint generator
    /// and roof colour.</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "singleFamily";

    /// <summary>Generated footprint polygon (closed), world coordinates.</summary>
    [JsonPropertyName("footprint")]
    public List<MapPoint> Footprint { get; set; } = new();

    /// <summary>Footprint rotation in degrees.</summary>
    [JsonPropertyName("rotation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double Rotation { get; set; }

    [JsonPropertyName("roof")]
    public MapRoof Roof { get; set; } = new();

    /// <summary>Number of floors (0 = no interior, e.g. playgrounds).</summary>
    [JsonPropertyName("floorCount")]
    public int FloorCount { get; set; } = 1;

    /// <summary>The floor currently shown / being edited (0-based; ground = 0).</summary>
    [JsonPropertyName("activeFloor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ActiveFloor { get; set; }

    /// <summary>Zoom at/above which the floor plan replaces the roof view.</summary>
    [JsonPropertyName("planMinZoom")]
    public double PlanMinZoom { get; set; } = 4;

    /// <summary>Per-floor interiors; length tracks <see cref="FloorCount"/>.</summary>
    [JsonPropertyName("floors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<MapFloor> Floors { get; set; } = new();

    /// <summary>Visible-zoom-range floor; null/0 = no minimum.</summary>
    [JsonPropertyName("minZoom")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MinZoom { get; set; }

    /// <summary>Visible-zoom-range ceiling; null/0 = no maximum.</summary>
    [JsonPropertyName("maxZoom")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MaxZoom { get; set; }
}

/// <summary>A building's roof — drives how upper floors inset from the footprint.</summary>
public sealed class MapRoof
{
    /// <summary>"gable" | "hip" | "flat".</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "gable";

    /// <summary>Roof pitch — scales how fast upper floors lose area. 0 = flat.</summary>
    [JsonPropertyName("pitch")]
    public double Pitch { get; set; } = 0.5;
}

/// <summary>One floor of a building's interior plan.</summary>
public sealed class MapFloor
{
    [JsonPropertyName("walls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<MapWall> Walls { get; set; } = new();

    [JsonPropertyName("openings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<MapOpening> Openings { get; set; } = new();

    [JsonPropertyName("stairs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<MapStair> Stairs { get; set; } = new();

    /// <summary>Floor-scoped text labels — shown only while this floor is shown.</summary>
    [JsonPropertyName("labels")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<MapLabel> Labels { get; set; } = new();

    /// <summary>Floor-scoped pins — shown only while this floor is shown.</summary>
    [JsonPropertyName("pins")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<MapPin> Pins { get; set; } = new();
}

/// <summary>A straight interior wall segment, world coordinates.</summary>
public sealed class MapWall
{
    [JsonPropertyName("x1")] public double X1 { get; set; }
    [JsonPropertyName("y1")] public double Y1 { get; set; }
    [JsonPropertyName("x2")] public double X2 { get; set; }
    [JsonPropertyName("y2")] public double Y2 { get; set; }

    [JsonPropertyName("thickness")]
    public double Thickness { get; set; } = 3;
}

public sealed class MapOpening
{
    /// <summary>&gt;= 0 = index into the floor's <see cref="MapFloor.Walls"/>;
    /// &lt; 0 = an edge of the floor's outer outline (edge index = -WallIndex - 1).</summary>
    [JsonPropertyName("wallIndex")]
    public int WallIndex { get; set; }

    /// <summary>Position along the wall, 0..1.</summary>
    [JsonPropertyName("t")]
    public double T { get; set; } = 0.5;

    [JsonPropertyName("width")]
    public double Width { get; set; } = 8;

    /// <summary>"door" | "window".</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "door";

    /// <summary>Door swing side — flips which side of the wall the leaf opens to.</summary>
    [JsonPropertyName("flip")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Flip { get; set; }
}

public sealed class MapStair
{
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    [JsonPropertyName("width")] public double Width { get; set; } = 10;
    [JsonPropertyName("length")] public double Length { get; set; } = 18;

    [JsonPropertyName("rotation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double Rotation { get; set; }

    /// <summary>"up" | "down".</summary>
    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "up";
}
