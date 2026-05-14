using System.Text.Json.Serialization;

namespace Novalist.Core.Models;

/// <summary>
/// Interactive map definition. Stored per-draft at
/// <c>Books/&lt;book&gt;/Drafts/&lt;draft&gt;/Maps/&lt;mapId&gt;.json</c>.
/// </summary>
public sealed class MapData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; set; } = 2;

    /// <summary>Top-level layer nodes. A node with children acts as a group;
    /// a node without children is a plain layer. Arbitrary nesting depth.</summary>
    [JsonPropertyName("layers")]
    public List<MapLayerNode> Layers { get; set; } = new();

    [JsonPropertyName("pins")]
    public List<MapPin> Pins { get; set; } = new();

    /// <summary>Free-standing text annotations placed in world space
    /// (region names, area notes).</summary>
    [JsonPropertyName("labels")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<MapLabel> Labels { get; set; } = new();

    /// <summary>User-authored cross-section profiles for roads/rivers. A spline
    /// references one by setting <c>Preset</c> to <c>"custom:&lt;id&gt;"</c>.</summary>
    [JsonPropertyName("customProfiles")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<MapProfile> CustomProfiles { get; set; } = new();

    [JsonPropertyName("initialView")]
    public MapViewport InitialView { get; set; } = new();

    /// <summary>Optional map-wide clip boundary — everything outside this polygon
    /// is hidden, and the polygon itself is stroked as a visible frame.
    /// Null = no border.</summary>
    [JsonPropertyName("border")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MapBorder? Border { get; set; }
}

/// <summary>A map-wide clip boundary: a closed polygon that hides everything
/// outside it and is drawn as a visible outline frame. One per map.</summary>
public sealed class MapBorder
{
    /// <summary>Closed polygon vertices (>= 3), in world coordinates.</summary>
    [JsonPropertyName("points")]
    public List<MapPoint> Points { get; set; } = new();

    /// <summary>Hex colour of the visible outline stroke.</summary>
    [JsonPropertyName("outlineColor")]
    public string OutlineColor { get; set; } = "#1c1a18";

    /// <summary>Outline stroke width in world units.</summary>
    [JsonPropertyName("outlineWidth")]
    public double OutlineWidth { get; set; } = 4;
}

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

/// <summary>
/// Recursive layer node. Affinity-style: every layer is the same type. A node
/// becomes a "group" purely by having <see cref="Children"/>. Images may live
/// on any node. Group-only behaviors (<see cref="IsConnectedSet"/>) only take
/// effect when the node has children.
/// </summary>
public sealed class MapLayerNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("opacity")]
    public double Opacity { get; set; } = 1.0;

    [JsonPropertyName("locked")]
    public bool Locked { get; set; }

    [JsonPropertyName("hidden")]
    public bool Hidden { get; set; }

    /// <summary>Panel expand/collapse state — persisted for UX continuity.</summary>
    [JsonPropertyName("expanded")]
    public bool Expanded { get; set; } = true;

    [JsonPropertyName("images")]
    public List<MapImage> Images { get; set; } = new();

    [JsonPropertyName("splines")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<MapSpline> Splines { get; set; } = new();

    /// <summary>Closed-polygon terrain shapes (grass, forest, sand, …) on this node.</summary>
    [JsonPropertyName("shapes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<MapShape> Shapes { get; set; } = new();

    /// <summary>Placed buildings (typed footprints, optionally with floor plans) on this node.</summary>
    [JsonPropertyName("buildings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<MapBuilding> Buildings { get; set; } = new();

    [JsonPropertyName("children")]
    public List<MapLayerNode> Children { get; set; } = new();

    /// <summary>When <c>true</c> and the node has children, exactly one child
    /// renders at a time (floor stacks, level-of-detail swaps).</summary>
    [JsonPropertyName("isConnectedSet")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsConnectedSet { get; set; }

    [JsonPropertyName("defaultMemberLayerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultMemberLayerId { get; set; }

    [JsonPropertyName("minZoom")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MinZoom { get; set; }

    [JsonPropertyName("maxZoom")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MaxZoom { get; set; }
}

public sealed class MapImage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Relative path under the book's <c>Images/</c> folder.</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }

    [JsonPropertyName("rotation")]
    public double Rotation { get; set; }

    [JsonPropertyName("clipPolygon")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<MapPoint>? ClipPolygon { get; set; }

    [JsonPropertyName("minZoom")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MinZoom { get; set; }

    [JsonPropertyName("maxZoom")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MaxZoom { get; set; }
}

public sealed class MapPin
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("style")]
    public string Style { get; set; } = "dot"; // dot | marker | svg

    [JsonPropertyName("iconPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IconPath { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>Hex string like "#f9c46a"; null = default theme pin color.</summary>
    [JsonPropertyName("color")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Color { get; set; }

    [JsonPropertyName("entityType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EntityType { get; set; }

    [JsonPropertyName("entityId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EntityId { get; set; }

    [JsonPropertyName("connectedGroupId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConnectedGroupId { get; set; }

    /// <summary>Owning layer node id. Empty = unassigned (always visible).</summary>
    [JsonPropertyName("layerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string LayerId { get; set; } = string.Empty;

    /// <summary>Visible-zoom-range floor; null/0 = no minimum.</summary>
    [JsonPropertyName("minZoom")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MinZoom { get; set; }

    /// <summary>Visible-zoom-range ceiling; null/0 = no maximum.</summary>
    [JsonPropertyName("maxZoom")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MaxZoom { get; set; }
}

/// <summary>A free-standing text annotation placed in the map's world space.
/// <see cref="FontSize"/> is in world units, so the text scales with the map
/// zoom and stays glued to the area it names.</summary>
public sealed class MapLabel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("fontSize")]
    public double FontSize { get; set; } = 18;

    /// <summary>CSS font-family stack; empty = the map's default UI font.</summary>
    [JsonPropertyName("fontFamily")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string FontFamily { get; set; } = string.Empty;

    /// <summary>"left" | "center" | "right".</summary>
    [JsonPropertyName("align")]
    public string Align { get; set; } = "center";

    /// <summary>Hex string like "#ffffff".</summary>
    [JsonPropertyName("color")]
    public string Color { get; set; } = "#ffffff";

    /// <summary>Owning layer node id. Empty = unassigned (always visible).</summary>
    [JsonPropertyName("layerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string LayerId { get; set; } = string.Empty;

    /// <summary>Visible-zoom-range floor; null/0 = no minimum.</summary>
    [JsonPropertyName("minZoom")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MinZoom { get; set; }

    /// <summary>Visible-zoom-range ceiling; null/0 = no maximum.</summary>
    [JsonPropertyName("maxZoom")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MaxZoom { get; set; }
}

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

/// <summary>A door or window anchored on a wall.</summary>
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

/// <summary>A staircase block on a floor.</summary>
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

public sealed class MapViewport
{
    [JsonPropertyName("centerX")]
    public double CenterX { get; set; }

    [JsonPropertyName("centerY")]
    public double CenterY { get; set; }

    [JsonPropertyName("zoom")]
    public double Zoom { get; set; } = 1.0;
}

/// <summary>Lightweight reference held in <see cref="BookData.Maps"/>;
/// the full <see cref="MapData"/> lives in its own JSON file.</summary>
public sealed class MapReference
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
