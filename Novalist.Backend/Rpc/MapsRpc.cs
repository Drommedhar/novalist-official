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

    [JsonRpcMethod("maps/create")]
    public async Task<MapLoadDto> CreateAsync(string name)
    {
        var map = await Service.CreateMapAsync(name);
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
