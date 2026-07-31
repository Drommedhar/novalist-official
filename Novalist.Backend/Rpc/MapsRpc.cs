using System.Text.Json;
using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Interactive maps: list, load, and persist per-draft map JSON.</summary>
public sealed class MapsRpc
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    private readonly Workspace _workspace;

    public MapsRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private MapService Service => new(_workspace.Projects, _workspace.FileService);

    [JsonRpcMethod("maps/list")]
    public MapRefDto[] List()
    {
        var book = _workspace.Projects.ActiveBook
            ?? throw new InvalidOperationException("No project open.");
        return book.Maps.Select(m => new MapRefDto(m.Id, m.Name)).ToArray();
    }

    /// <summary>
    /// Project-root-relative path of the active book (e.g. "Frostschwur"), so
    /// the renderer can prefix map image paths (which are book-root-relative)
    /// for the project-rooted novalist-project:// protocol. Empty when unknown.
    /// </summary>
    [JsonRpcMethod("maps/imageBase")]
    public string ImageBase()
    {
        var projects = _workspace.Projects;
        if (projects.ProjectRoot == null || projects.ActiveBookRoot == null) return string.Empty;
        return Path.GetRelativePath(projects.ProjectRoot, projects.ActiveBookRoot).Replace('\\', '/');
    }

    [JsonRpcMethod("maps/create")]
    public async Task<MapLoadDto> CreateAsync(string name)
    {
        var map = await Service.CreateMapAsync(name);
        return new MapLoadDto(map.Id, map.Name, JsonSerializer.Serialize(map, JsonOptions));
    }

    /// <summary>
    /// Puts a first coastline on a map, on a layer of its own.
    ///
    /// Every coastline, river and terrain polygon had to be drawn by hand from
    /// a blank canvas, which is the part of mapmaking that stops a writer who
    /// is not an illustrator. What comes out is ordinary shapes and splines, so
    /// the first thing they do can be to drag a headland about.
    ///
    /// A layer of its own so it can be hidden, or thrown away whole, without
    /// touching anything they drew themselves.
    /// </summary>
    [JsonRpcMethod("maps/generateTerrain")]
    public async Task<MapLoadDto?> GenerateTerrainAsync(
        string mapId, int seed, double width, double height,
        double landmass = 0.55, int rivers = 3, int forests = 4, int settlements = 5)
    {
        var map = await Service.LoadMapAsync(mapId);
        if (map == null) return null;

        var result = Core.Services.TerrainGenerator.Generate(
            new Core.Services.TerrainRequest(
                seed, width, height, landmass, rivers, forests, settlements));

        var layer = new Core.Models.MapLayerNode
        {
            Id = $"generated-{seed}",
            Name = $"Generated ({seed})",
            Shapes = [.. result.Shapes],
            Splines = [.. result.Rivers]
        };
        // Underneath what the writer drew: generated land is a background for
        // their map, not a thing pasted over the top of it.
        map.Layers.Insert(0, layer);

        // Settlements as pins, because a pin is what a place on a map is here.
        foreach (var (x, y, size) in result.Settlements)
        {
            map.Pins.Add(new Core.Models.MapPin
            {
                Id = $"gen-{seed}-{map.Pins.Count}",
                X = x,
                Y = y,
                Label = string.Empty,
                Style = "dot"
            });
        }

        await Service.SaveMapAsync(map);
        return new MapLoadDto(map.Id, map.Name, JsonSerializer.Serialize(map, JsonOptions));
    }

    [JsonRpcMethod("maps/load")]
    public async Task<MapLoadDto?> LoadAsync(string mapId)
    {
        var map = await Service.LoadMapAsync(mapId);
        return map == null
            ? null
            : new MapLoadDto(map.Id, map.Name, JsonSerializer.Serialize(map, JsonOptions));
    }

    [JsonRpcMethod("maps/save")]
    public async Task SaveAsync(string json)
    {
        var map = JsonSerializer.Deserialize<MapData>(json, JsonOptions)
            ?? throw new InvalidOperationException("Invalid map payload.");
        await Service.SaveMapAsync(map);
    }

    [JsonRpcMethod("maps/rename")]
    public async Task<MapRefDto[]> RenameAsync(string mapId, string newName)
    {
        await Service.RenameMapAsync(mapId, newName);
        return List();
    }

    [JsonRpcMethod("maps/delete")]
    public async Task<MapRefDto[]> DeleteAsync(string mapId)
    {
        await Service.DeleteMapAsync(mapId);
        return List();
    }
}

public sealed record MapRefDto(string Id, string Name);

public sealed record MapLoadDto(string Id, string Name, string Json);
