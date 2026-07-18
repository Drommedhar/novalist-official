using System.Text.Json;
using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Codex entity access: list summaries per type, fetch full records.</summary>
public sealed class EntitiesRpc
{
    private readonly Workspace _workspace;
    private readonly EntityService _entities;

    public EntitiesRpc(Workspace workspace)
    {
        _workspace = workspace;
        _entities = new EntityService(workspace.Projects);
    }

    [JsonRpcMethod("entities/list")]
    public async Task<EntitySummaryDto[]> ListAsync(string type)
    {
        return type switch
        {
            "character" => (await _entities.LoadCharactersAsync())
                .Select(c => Summary(c.Id, Compose(c.Name, c.Surname), c.Role, c.IsWorldBible, c.Images.FirstOrDefault()))
                .ToArray(),
            "location" => (await _entities.LoadLocationsAsync())
                .Select(l => Summary(l.Id, l.Name, l.Description, l.IsWorldBible, l.Images.FirstOrDefault()))
                .ToArray(),
            "item" => (await _entities.LoadItemsAsync())
                .Select(i => Summary(i.Id, i.Name, i.Description, i.IsWorldBible, i.Images.FirstOrDefault()))
                .ToArray(),
            "lore" => (await _entities.LoadLoreAsync())
                .Select(l => Summary(l.Id, l.Name, l.Description, l.IsWorldBible, l.Images.FirstOrDefault()))
                .ToArray(),
            _ => throw new InvalidOperationException($"Unknown entity type '{type}'.")
        };
    }

    [JsonRpcMethod("entities/get")]
    public async Task<JsonElement> GetAsync(string type, string id)
    {
        object? entity = type switch
        {
            "character" => (await _entities.LoadCharactersAsync()).FirstOrDefault(c => c.Id == id),
            "location" => (await _entities.LoadLocationsAsync()).FirstOrDefault(l => l.Id == id),
            "item" => (await _entities.LoadItemsAsync()).FirstOrDefault(i => i.Id == id),
            "lore" => (await _entities.LoadLoreAsync()).FirstOrDefault(l => l.Id == id),
            _ => throw new InvalidOperationException($"Unknown entity type '{type}'.")
        };
        return JsonSerializer.SerializeToElement(entity ?? throw Unknown(id), JsonOptions);
    }

    [JsonRpcMethod("entities/update")]
    public async Task<JsonElement> UpdateAsync(string type, string id, Dictionary<string, string> fields)
    {
        object entity = type switch
        {
            "character" => (await _entities.LoadCharactersAsync()).FirstOrDefault(c => c.Id == id) as object,
            "location" => (await _entities.LoadLocationsAsync()).FirstOrDefault(l => l.Id == id),
            "item" => (await _entities.LoadItemsAsync()).FirstOrDefault(i => i.Id == id),
            "lore" => (await _entities.LoadLoreAsync()).FirstOrDefault(l => l.Id == id),
            _ => throw new InvalidOperationException($"Unknown entity type '{type}'.")
        } ?? throw Unknown(id);

        foreach (var (key, value) in fields)
        {
            var property = entity.GetType().GetProperty(
                char.ToUpperInvariant(key[0]) + key[1..]);
            if (property?.CanWrite == true && property.PropertyType == typeof(string))
            {
                property.SetValue(entity, value);
            }
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
        return JsonSerializer.SerializeToElement(entity, JsonOptions);
    }

    [JsonRpcMethod("entities/updateLists")]
    public async Task<JsonElement> UpdateListsAsync(
        string type,
        string id,
        string[]? aliases,
        EntitySectionDto[]? sections,
        RelationshipRowDto[]? relationships)
    {
        object entity = type switch
        {
            "character" => (await _entities.LoadCharactersAsync()).FirstOrDefault(c => c.Id == id) as object,
            "location" => (await _entities.LoadLocationsAsync()).FirstOrDefault(l => l.Id == id),
            "item" => (await _entities.LoadItemsAsync()).FirstOrDefault(i => i.Id == id),
            "lore" => (await _entities.LoadLoreAsync()).FirstOrDefault(l => l.Id == id),
            _ => throw new InvalidOperationException($"Unknown entity type '{type}'.")
        } ?? throw Unknown(id);

        if (aliases != null)
        {
            var target = (List<string>)entity.GetType().GetProperty("Aliases")!.GetValue(entity)!;
            target.Clear();
            target.AddRange(aliases.Where(a => !string.IsNullOrWhiteSpace(a)));
        }
        if (sections != null)
        {
            var target = (List<EntitySection>)entity.GetType().GetProperty("Sections")!.GetValue(entity)!;
            target.Clear();
            target.AddRange(sections.Select(s => new EntitySection { Title = s.Title, Content = s.Content }));
        }
        if (relationships != null && entity is CharacterData character)
        {
            character.Relationships = relationships
                .Where(r => !string.IsNullOrWhiteSpace(r.Role) || !string.IsNullOrWhiteSpace(r.Target))
                .Select(r => new EntityRelationship { Role = r.Role, Target = r.Target })
                .ToList();
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
        return JsonSerializer.SerializeToElement(entity, JsonOptions);
    }

    [JsonRpcMethod("entities/create")]
    public async Task<JsonElement> CreateAsync(string type, string name)
    {
        object entity = type switch
        {
            "character" => new CharacterData { Name = name },
            "location" => new LocationData { Name = name },
            "item" => new ItemData { Name = name },
            "lore" => new LoreData { Name = name },
            _ => throw new InvalidOperationException($"Unknown entity type '{type}'.")
        };
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
        return JsonSerializer.SerializeToElement(entity, JsonOptions);
    }

    [JsonRpcMethod("entities/delete")]
    public async Task DeleteAsync(string type, string id, bool isWorldBible)
    {
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

    [JsonRpcMethod("scenes/setSynopsis")]
    public async Task SetSynopsisAsync(string chapterGuid, string sceneId, string synopsis)
    {
        var (_, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        scene.Synopsis = synopsis.Length == 0 ? null : synopsis;
        await _workspace.Projects.SaveScenesAsync();
    }

    [JsonRpcMethod("scenes/setNotes")]
    public async Task SetNotesAsync(string chapterGuid, string sceneId, string notes)
    {
        var (_, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        scene.Notes = notes.Length == 0 ? null : notes;
        await _workspace.Projects.SaveScenesAsync();
    }

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static InvalidOperationException Unknown(string id) => new($"Unknown entity '{id}'.");

    private static string Compose(string name, string surname) =>
        surname.Length == 0 ? name : $"{name} {surname}";

    private static EntitySummaryDto Summary(
        string id, string name, string detail, bool isWorldBible, EntityImage? image) =>
        new(id, name, detail, isWorldBible, image?.Path);
}

public sealed record EntitySectionDto(string Title, string Content);

public sealed record RelationshipRowDto(string Role, string Target);

public sealed record EntitySummaryDto(
    string Id,
    string Name,
    string Detail,
    bool IsWorldBible,
    string? ImagePath);
