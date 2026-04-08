using Novalist.Core.Models;

namespace Novalist.Core.Services;

public interface IEntityService
{
    // Characters
    Task<List<CharacterData>> LoadCharactersAsync();
    Task SaveCharacterAsync(CharacterData character);
    Task DeleteCharacterAsync(string id, bool isWorldBible = false);

    // Locations
    Task<List<LocationData>> LoadLocationsAsync();
    Task SaveLocationAsync(LocationData location);
    Task DeleteLocationAsync(string id, bool isWorldBible = false);

    // Items
    Task<List<ItemData>> LoadItemsAsync();
    Task SaveItemAsync(ItemData item);
    Task DeleteItemAsync(string id, bool isWorldBible = false);

    // Lore
    Task<List<LoreData>> LoadLoreAsync();
    Task SaveLoreAsync(LoreData lore);
    Task DeleteLoreAsync(string id, bool isWorldBible = false);

    // World Bible move operations
    Task MoveEntityToWorldBibleAsync(EntityType type, string id);
    Task MoveEntityToBookAsync(EntityType type, string id);

    // Custom entities
    Task<List<CustomEntityData>> LoadCustomEntitiesAsync(string entityTypeKey);
    Task SaveCustomEntityAsync(CustomEntityData entity);
    Task DeleteCustomEntityAsync(string entityTypeKey, string id, bool isWorldBible = false);

    // Custom entity type definitions
    List<CustomEntityTypeDefinition> GetCustomEntityTypes();
    Task SaveCustomEntityTypeAsync(CustomEntityTypeDefinition definition);
    Task DeleteCustomEntityTypeAsync(string typeKey);

    // Custom entity World Bible move operations
    Task MoveCustomEntityToWorldBibleAsync(string entityTypeKey, string id);
    Task MoveCustomEntityToBookAsync(string entityTypeKey, string id);

    // Images
    Task<string> ImportImageAsync(string sourcePath);
    List<string> GetProjectImages();
    string GetImageFullPath(string relativePath);
}
