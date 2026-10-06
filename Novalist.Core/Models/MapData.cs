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

    /// <summary>
    /// What one world unit on this map is worth on the ground, and in what.
    /// Null until the writer says, and nothing is drawn or measured until then.
    ///
    /// The map had world-space units and a zoom readout and no scale, so "how
    /// many days' ride to the coast" could not be answered from a map the app
    /// itself drew.
    /// </summary>
    [JsonPropertyName("scale")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MapScale? Scale { get; set; }
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

/// <summary>
/// What the map's own units mean on the ground.
///
/// Deliberately one number and one word: a map is drawn at whatever size suits
/// it, and the only thing the app needs to know is what to multiply by. The unit
/// is free text because a world may not measure in kilometres.
/// </summary>
public sealed class MapScale
{
    /// <summary>Ground distance covered by one world unit. Must be positive.</summary>
    [JsonPropertyName("unitsPer")]
    public double UnitsPer { get; set; } = 1;

    /// <summary>What that distance is called: "km", "miles", "leagues".</summary>
    [JsonPropertyName("unit")]
    public string Unit { get; set; } = "km";

    /// <summary>
    /// Spacing of the optional grid, in world units, or 0 for no grid. Separate
    /// from the scale because a grid is a drawing aid and a scale is a fact.
    /// </summary>
    [JsonPropertyName("gridSpacing")]
    public double GridSpacing { get; set; }
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

    /// <summary>
    /// Id of another map this pin opens, or null.
    ///
    /// A pin could resolve to a Codex entry and nothing else, so a world map
    /// could mark a city and never lead to the city's own map - the maps sat in
    /// a flat list of tabs with no relationship between them, which is the one
    /// relationship maps actually have.
    /// </summary>
    [JsonPropertyName("targetMapId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetMapId { get; set; }

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
