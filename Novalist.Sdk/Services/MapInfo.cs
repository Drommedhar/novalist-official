
namespace Novalist.Sdk.Services;

public sealed class MapPinInfo
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public double X { get; init; }
    public double Y { get; init; }

    /// <summary>The Codex entry this pin stands for, or empty.</summary>
    public string EntityId { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;

    /// <summary>Another map this pin opens, or empty.</summary>
    public string TargetMapId { get; init; } = string.Empty;
}

/// <summary>A map of the world, with what is marked on it.</summary>
public sealed class MapInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    /// <summary>Project-relative images used by all map layers and pin icons.</summary>
    public IReadOnlyList<string> ImagePaths { get; init; } = [];
    public IReadOnlyList<MapPinInfo> Pins { get; init; } = [];
}
