using System.Text.Json;
using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class EntitiesRpc
{
    [JsonRpcMethod("entities/moveToWorldBible")]
    public async Task MoveToWorldBibleAsync(string type, string id)
    {
        if (IsCustomType(type)) await _entities.MoveCustomEntityToWorldBibleAsync(type, id);
        else await _entities.MoveEntityToWorldBibleAsync(ParseType(type), id);
    }

    [JsonRpcMethod("entities/moveToBook")]
    public async Task MoveToBookAsync(string type, string id)
    {
        if (IsCustomType(type)) await _entities.MoveCustomEntityToBookAsync(type, id);
        else await _entities.MoveEntityToBookAsync(ParseType(type), id);
    }

    private static EntityType ParseType(string type) => type switch
    {
        "character" => EntityType.Character,
        "location" => EntityType.Location,
        "item" => EntityType.Item,
        "lore" => EntityType.Lore,
        _ => throw new InvalidOperationException($"Unknown entity type '{type}'.")
    };

    [JsonRpcMethod("entities/create")]
    public async Task<JsonElement> CreateAsync(string type, string name, string? templateId = null)
    {
        if (IsCustomType(type))
        {
            var definition = _entities.GetCustomEntityTypes().First(d => d.TypeKey == type);
            var custom = new CustomEntityData { EntityTypeKey = type, Name = name };
            foreach (var field in definition.DefaultFields)
            {
                custom.Fields.TryAdd(field.Key, field.DefaultValue);
            }
            if (templateId != null)
            {
                ApplyCustomEntityTemplate(custom, templateId);
            }
            await _entities.SaveCustomEntityAsync(custom);
            return WithResolvedImages(custom);
        }
        IEntityData entity = type switch
        {
            "character" => new CharacterData { Name = name },
            "location" => new LocationData { Name = name },
            "item" => new ItemData { Name = name },
            "lore" => new LoreData { Name = name },
            _ => throw new InvalidOperationException($"Unknown entity type '{type}'.")
        };
        if (templateId != null)
        {
            ApplyTemplate(entity, type, templateId);
        }
        switch (entity)
        {
            case CharacterData c:
                await _entities.SaveCharacterAsync(c);
                break;
            case LocationData l:
                await _entities.SaveLocationAsync(l);
                break;
            case ItemData i:
                await _entities.SaveItemAsync(i);
                break;
            default:
                await _entities.SaveLoreAsync((LoreData)entity);
                break;
        }
        return WithResolvedImages(entity);
    }

    [JsonRpcMethod("entities/delete")]
    public async Task DeleteAsync(string type, string id, bool isWorldBible)
    {
        if (IsCustomType(type))
        {
            await _entities.DeleteCustomEntityAsync(type, id, isWorldBible);
            return;
        }
        switch (type)
        {
            case "character":
                await _entities.DeleteCharacterAsync(id, isWorldBible);
                break;
            case "location":
                await _entities.DeleteLocationAsync(id, isWorldBible);
                break;
            case "item":
                await _entities.DeleteItemAsync(id, isWorldBible);
                break;
            case "lore":
                await _entities.DeleteLoreAsync(id, isWorldBible);
                break;
            default:
                throw new InvalidOperationException($"Unknown entity type '{type}'.");
        }
    }
}
