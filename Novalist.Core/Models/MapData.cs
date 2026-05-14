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

    [JsonPropertyName("initialView")]
    public MapViewport InitialView { get; set; } = new();
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
}

public sealed class MapPoint
{
    [JsonPropertyName("x")]
    public double X { get; set; }
    [JsonPropertyName("y")]
    public double Y { get; set; }
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
