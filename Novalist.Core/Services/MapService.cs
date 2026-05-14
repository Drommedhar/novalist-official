using System.Text.Json;
using System.Text.Json.Nodes;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public sealed class MapService : IMapService
{
    private readonly IProjectService _projectService;
    private readonly IFileService _fileService;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public MapService(IProjectService projectService, IFileService fileService)
    {
        _projectService = projectService;
        _fileService = fileService;
    }

    public string GetMapsRoot()
    {
        var draftRoot = _projectService.ActiveDraftRoot ?? _projectService.ActiveBookRoot
            ?? throw new InvalidOperationException("No active book.");
        return _fileService.CombinePath(draftRoot, "Maps");
    }

    public async Task<MapData> CreateMapAsync(string name)
    {
        if (_projectService.ActiveBook == null)
            throw new InvalidOperationException("No active book.");

        var id = $"map-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var fileName = $"{id}.json";
        var map = new MapData
        {
            Id = id,
            Name = name,
            FileName = fileName,
        };

        var mapsRoot = GetMapsRoot();
        await _fileService.CreateDirectoryAsync(mapsRoot);
        await SaveMapAsync(map);

        _projectService.ActiveBook.Maps.Add(new MapReference
        {
            Id = id,
            Name = name,
            FileName = fileName,
            CreatedAt = DateTime.UtcNow,
        });
        await _projectService.SaveProjectAsync();
        return map;
    }

    public async Task<MapData?> LoadMapAsync(string mapId)
    {
        var book = _projectService.ActiveBook;
        if (book == null) return null;
        var reference = book.Maps.FirstOrDefault(m => string.Equals(m.Id, mapId, StringComparison.OrdinalIgnoreCase));
        if (reference == null) return null;
        var path = _fileService.CombinePath(GetMapsRoot(), reference.FileName);
        if (!await _fileService.ExistsAsync(path)) return null;
        var json = await _fileService.ReadTextAsync(path);
        return DeserializeWithMigration(json);
    }

    /// <summary>
    /// Deserializes map JSON, migrating the legacy v1 <c>groups[].layers[]</c>
    /// shape to the v2 recursive <c>layers[]</c> tree. Each old group becomes a
    /// parent node; its old layers become that node's children.
    /// </summary>
    internal static MapData? DeserializeWithMigration(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject();
        if (root == null) return JsonSerializer.Deserialize<MapData>(json, JsonOptions);

        // v2 already has "layers" and no "groups" — deserialize directly.
        if (root.ContainsKey("layers") && !root.ContainsKey("groups"))
            return JsonSerializer.Deserialize<MapData>(json, JsonOptions);

        // v1 migration: rewrite "groups" into a recursive "layers" array.
        if (root.TryGetPropertyValue("groups", out var groupsNode) && groupsNode is JsonArray groups)
        {
            var layers = new JsonArray();
            foreach (var g in groups)
            {
                if (g is not JsonObject group) continue;
                var node = new JsonObject
                {
                    ["id"] = group["id"]?.GetValue<string>() ?? string.Empty,
                    ["name"] = group["name"]?.GetValue<string>() ?? "Group",
                    ["opacity"] = 1.0,
                    ["locked"] = false,
                    ["hidden"] = false,
                    ["expanded"] = true,
                    ["images"] = new JsonArray(),
                    ["isConnectedSet"] = group["isConnectedSet"]?.GetValue<bool>() ?? false,
                };
                var defMember = group["defaultMemberLayerId"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(defMember)) node["defaultMemberLayerId"] = defMember;

                var children = new JsonArray();
                if (group["layers"] is JsonArray oldLayers)
                {
                    foreach (var l in oldLayers)
                    {
                        if (l is not JsonObject layer) continue;
                        // An old layer maps 1:1 to a leaf node; clone its fields.
                        var leaf = JsonNode.Parse(layer.ToJsonString())!.AsObject();
                        if (!leaf.ContainsKey("expanded")) leaf["expanded"] = true;
                        if (!leaf.ContainsKey("children")) leaf["children"] = new JsonArray();
                        children.Add(leaf);
                    }
                }
                node["children"] = children;
                layers.Add(node);
            }
            root.Remove("groups");
            root["layers"] = layers;
            root["version"] = 2;
        }
        return JsonSerializer.Deserialize<MapData>(root.ToJsonString(), JsonOptions);
    }

    public async Task SaveMapAsync(MapData map)
    {
        var mapsRoot = GetMapsRoot();
        await _fileService.CreateDirectoryAsync(mapsRoot);
        var path = _fileService.CombinePath(mapsRoot, string.IsNullOrEmpty(map.FileName) ? map.Id + ".json" : map.FileName);
        var json = JsonSerializer.Serialize(map, JsonOptions);
        await _fileService.WriteTextAsync(path, json);
    }

    public async Task DeleteMapAsync(string mapId)
    {
        var book = _projectService.ActiveBook;
        if (book == null) return;
        var reference = book.Maps.FirstOrDefault(m => string.Equals(m.Id, mapId, StringComparison.OrdinalIgnoreCase));
        if (reference == null) return;
        var path = _fileService.CombinePath(GetMapsRoot(), reference.FileName);
        if (await _fileService.ExistsAsync(path))
            await _fileService.DeleteFileAsync(path);
        book.Maps.Remove(reference);
        await _projectService.SaveProjectAsync();
    }

    public async Task RenameMapAsync(string mapId, string newName)
    {
        var book = _projectService.ActiveBook;
        if (book == null) return;
        var reference = book.Maps.FirstOrDefault(m => string.Equals(m.Id, mapId, StringComparison.OrdinalIgnoreCase));
        if (reference == null) return;
        reference.Name = newName;
        var map = await LoadMapAsync(mapId);
        if (map != null)
        {
            map.Name = newName;
            await SaveMapAsync(map);
        }
        await _projectService.SaveProjectAsync();
    }
}
