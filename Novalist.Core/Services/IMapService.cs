using Novalist.Core.Models;

namespace Novalist.Core.Services;

public interface IMapService
{
    Task<MapData> CreateMapAsync(string name);
    Task<MapData?> LoadMapAsync(string mapId);
    Task SaveMapAsync(MapData map);
    Task DeleteMapAsync(string mapId);
    Task RenameMapAsync(string mapId, string newName);
    string GetMapsRoot();
}
