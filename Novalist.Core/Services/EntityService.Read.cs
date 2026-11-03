using System.Text.Json;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public partial class EntityService
{
    public Task<CharacterData?> LoadCharacterAsync(string id)
        => LoadEntityMergedAsync<CharacterData>(Book.CharacterFolder, Project.CharacterFolder, id);

    public Task<LocationData?> LoadLocationAsync(string id)
        => LoadEntityMergedAsync<LocationData>(Book.LocationFolder, Project.LocationFolder, id);

    public Task<ItemData?> LoadItemAsync(string id)
        => LoadEntityMergedAsync<ItemData>(Book.ItemFolder, Project.ItemFolder, id);

    public Task<LoreData?> LoadLoreEntryAsync(string id)
        => LoadEntityMergedAsync<LoreData>(Book.LoreFolder, Project.LoreFolder, id);

    public Task<CustomEntityData?> LoadCustomEntityAsync(string entityTypeKey, string id)
    {
        var definition = GetCustomEntityTypeOrThrow(entityTypeKey);
        return LoadEntityMergedAsync<CustomEntityData>(definition.FolderName, definition.FolderName, id);
    }

    private async Task<T?> LoadEntityMergedAsync<T>(string bookFolder, string wbFolder, string id)
        where T : class, IEntityData
    {
        var entity = await FindEntityInDirectoryAsync<T>(Path.Combine(BookRoot, bookFolder), id);
        if (entity != null)
        {
            entity.IsWorldBible = false;
            return entity;
        }

        var worldBibleRoot = WorldBibleRoot;
        if (worldBibleRoot == null) return null;
        entity = await FindEntityInDirectoryAsync<T>(Path.Combine(worldBibleRoot, wbFolder), id);
        if (entity != null) entity.IsWorldBible = true;
        return entity;
    }

    private async Task<T?> FindEntityInDirectoryAsync<T>(string directory, string id) where T : class, IEntityData
    {
        if (!await _files.DirectoryExistsAsync(directory)) return null;
        var paths = await _files.GetFilesAsync(directory, "*.json");
        var canonical = paths.FirstOrDefault(path =>
            string.Equals(Path.GetFileNameWithoutExtension(path), id, StringComparison.Ordinal));
        if (canonical != null)
        {
            var entity = await ReadEntityAsync<T>(canonical);
            if (entity?.Id == id) return entity;
        }

        // External tools may rename a JSON file without changing its entity ID.
        foreach (var path in paths)
        {
            if (path == canonical) continue;
            var entity = await ReadEntityAsync<T>(path);
            if (entity?.Id == id) return entity;
        }
        return null;
    }

    private async Task<T?> ReadEntityAsync<T>(string path) where T : class, IEntityData
    {
        var json = await _files.ReadTextAsync(path);
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            // Malformed entries are skipped just as they are when listing the Codex.
            return null;
        }
    }
}
