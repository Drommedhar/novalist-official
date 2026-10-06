using System.Text.Json;
using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Codex entity access: list summaries per type, fetch full records.</summary>
public sealed partial class EntitiesRpc
{
    private readonly Workspace _workspace;
    private readonly EntityService _entities;
    private readonly EntityImageImporter _imageImporter;

    public EntitiesRpc(Workspace workspace, HttpClient? http = null)
    {
        _workspace = workspace;
        _entities = new EntityService(workspace.Projects);
        _imageImporter = new EntityImageImporter(_entities, http ?? new HttpClient());
    }

    [JsonRpcMethod("entities/list")]
    public async Task<EntitySummaryDto[]> ListAsync(string type)
    {
        if (IsCustomType(type))
        {
            return (await _entities.LoadCustomEntitiesAsync(type))
                .Select(c => Summary(
                    c, c.Fields.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty))
                .ToArray();
        }
        return type switch
        {
            "character" => (await _entities.LoadCharactersAsync())
                .Select(c => Summary(c, c.Role))
                .ToArray(),
            "location" => (await _entities.LoadLocationsAsync())
                .Select(l => Summary(l, l.Description))
                .ToArray(),
            "item" => (await _entities.LoadItemsAsync())
                .Select(i => Summary(i, i.Description))
                .ToArray(),
            "lore" => (await _entities.LoadLoreAsync())
                .Select(l => Summary(l, l.Description))
                .ToArray(),
            _ => throw new InvalidOperationException($"Unknown entity type '{type}'.")
        };
    }

    /// <summary>
    /// What this entry said before each of its last few saves.
    ///
    /// Snapshots covered scenes and nothing else, so typing the wrong eye
    /// colour over the right one had no answer inside the app.
    /// </summary>
    [JsonRpcMethod("entities/history")]
    public EntityRevisionDto[] History(string id)
        => [.. new EntityHistory(_workspace.Projects).List(id)
            .Select(r => new EntityRevisionDto(
                r.Id, r.SavedAt.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                r.SizeBytes))];

    /// <summary>
    /// Puts a revision back. The state being replaced is recorded first by the
    /// ordinary save path, so an unwanted restore is itself undoable.
    /// </summary>
    [JsonRpcMethod("entities/restoreRevision")]
    public async Task<JsonElement> RestoreRevisionAsync(string type, string id, string revisionId)
    {
        var stored = await new EntityHistory(_workspace.Projects).ReadAsync(id, revisionId)
            ?? throw new InvalidOperationException("That revision is no longer there.");

        // Deserialised as the type the caller says it is, then saved the way any
        // other edit is - so the write-back, the reconciler and the next
        // revision all behave exactly as they do for a hand edit.
        switch (type)
        {
            case "character":
                await _entities.SaveCharacterAsync(Read<Core.Models.CharacterData>(stored, id));
                break;
            case "location":
                await _entities.SaveLocationAsync(Read<Core.Models.LocationData>(stored, id));
                break;
            case "item":
                await _entities.SaveItemAsync(Read<Core.Models.ItemData>(stored, id));
                break;
            case "lore":
                await _entities.SaveLoreAsync(Read<Core.Models.LoreData>(stored, id));
                break;
            default:
                await _entities.SaveCustomEntityAsync(Read<Core.Models.CustomEntityData>(stored, id));
                break;
        }

        return await GetAsync(type, id);
    }

    /// <summary>
    /// A stored revision, with its id forced back to the entry being restored -
    /// a file edited by hand should not be able to write over a different entry.
    /// </summary>
    private static T Read<T>(string json, string id) where T : class
    {
        var entity = JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException("That revision could not be read.");
        var idProperty = typeof(T).GetProperty("Id");
        idProperty?.SetValue(entity, id);
        return entity;
    }

    [JsonRpcMethod("entities/get")]
    public async Task<JsonElement> GetAsync(string type, string id)
    {
        if (IsCustomType(type))
        {
            var custom = (await _entities.LoadCustomEntitiesAsync(type)).FirstOrDefault(c => c.Id == id)
                ?? throw Unknown(id);
            return WithResolvedImages(custom);
        }
        object? entity = type switch
        {
            "character" => (await _entities.LoadCharactersAsync()).FirstOrDefault(c => c.Id == id),
            "location" => (await _entities.LoadLocationsAsync()).FirstOrDefault(l => l.Id == id),
            "item" => (await _entities.LoadItemsAsync()).FirstOrDefault(i => i.Id == id),
            "lore" => (await _entities.LoadLoreAsync()).FirstOrDefault(l => l.Id == id),
            _ => throw new InvalidOperationException($"Unknown entity type '{type}'.")
        };
        return WithResolvedImages(entity ?? throw Unknown(id));
    }

    /// <summary>
    /// Refuses a write to a settled entry.
    ///
    /// A world bible is a contract with the reader: once a character's eyes are
    /// brown in three published chapters, changing that field is a decision
    /// rather than a typo. Nothing stopped a stray keystroke in a detail pane
    /// from rewriting canon silently.
    /// </summary>
    private static void RefuseIfLocked(Core.Models.IEntityData? entity)
    {
        if (entity?.Locked == true)
            throw new InvalidOperationException(LockedMessage);
    }

    /// <summary>
    /// The one string the renderer matches on to show its own wording. Matched
    /// rather than coded because every other refusal here is an exception too,
    /// and a second mechanism for one case is a mechanism nobody maintains.
    /// </summary>
    public const string LockedMessage = "entity-locked";

    /// <summary>
    /// Settles an entry, or unsettles it.
    ///
    /// The only write a locked entry accepts, because the writer has to be able
    /// to change their mind - a lock that cannot be undone is a lock nobody
    /// uses.
    /// </summary>
    [JsonRpcMethod("entities/setLocked")]
    public async Task<bool> SetLockedAsync(string type, string id, bool locked)
    {
        var entity = await FindAnyAsync(type, id) ?? throw Unknown(id);
        entity.Locked = locked;
        await SaveEntityAsync(entity);
        return locked;
    }

    /// <summary>Whichever type it is, as the interface every type implements.</summary>
    private async Task<Core.Models.IEntityData?> FindAnyAsync(string type, string id)
        => IsCustomType(type)
            ? (await _entities.LoadCustomEntitiesAsync(type)).FirstOrDefault(c => c.Id == id)
            : type switch
            {
                "character" => (await _entities.LoadCharactersAsync())
                    .FirstOrDefault(c => c.Id == id) as Core.Models.IEntityData,
                "location" => (await _entities.LoadLocationsAsync()).FirstOrDefault(l => l.Id == id),
                "item" => (await _entities.LoadItemsAsync()).FirstOrDefault(i => i.Id == id),
                "lore" => (await _entities.LoadLoreAsync()).FirstOrDefault(l => l.Id == id),
                _ => null
            };

    [JsonRpcMethod("entities/update")]
    public async Task<JsonElement> UpdateAsync(string type, string id, Dictionary<string, string> fields)
    {
        RefuseIfLocked(await FindAnyAsync(type, id));

        if (IsCustomType(type))
        {
            var custom = (await _entities.LoadCustomEntitiesAsync(type)).FirstOrDefault(c => c.Id == id)
                ?? throw Unknown(id);
            var previousCustomName = custom.Name;
            foreach (var (key, value) in fields)
            {
                if (key == "name") custom.Name = value;
                else custom.Fields[key] = value;
            }
            await _entities.SaveCustomEntityAsync(custom);
            await CascadeRenameAsync(id, previousCustomName, custom.Name);
            return WithResolvedImages(custom);
        }
        object entity = type switch
        {
            "character" => (await _entities.LoadCharactersAsync()).FirstOrDefault(c => c.Id == id) as object,
            "location" => (await _entities.LoadLocationsAsync()).FirstOrDefault(l => l.Id == id),
            "item" => (await _entities.LoadItemsAsync()).FirstOrDefault(i => i.Id == id),
            "lore" => (await _entities.LoadLoreAsync()).FirstOrDefault(l => l.Id == id),
            _ => throw new InvalidOperationException($"Unknown entity type '{type}'.")
        } ?? throw Unknown(id);

        // Captured before the write: most references store the display name, not
        // the id, so the cascade needs the name as it was.
        var previousName = DisplayNameOf(entity);

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

        // Propagate the new name to every name-keyed reference: relationship
        // targets, location parents, POV overrides, section wiki-links, and the
        // id-keyed mention spans in prose. Without this a rename silently
        // orphans everything that pointed at the entity.
        await CascadeRenameAsync(id, previousName, DisplayNameOf(entity));

        return WithResolvedImages(entity);
    }

    /// <summary>
    /// Display name as other records would have stored it. Only ever called on
    /// the four built-in types; custom entities take the earlier branch and use
    /// their own Name directly. Lore is the default arm, mirroring the save
    /// switch above.
    /// </summary>
    private static string DisplayNameOf(object entity) => entity switch
    {
        CharacterData c => c.DisplayName,
        LocationData l => l.Name,
        ItemData i => i.Name,
        _ => ((LoreData)entity).Name
    };

    private async Task CascadeRenameAsync(string entityId, string oldName, string newName)
    {
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
            return;

        await new EntityRenameService(_workspace.Projects, _entities)
            .CascadeAsync(entityId, oldName, newName);
    }

    /// <summary>Any entity by type and id, built-in or custom.</summary>
    private async Task<Core.Models.IEntityData?> FindEntityAsync(string type, string id)
    {
        if (IsCustomType(type))
            return (await _entities.LoadCustomEntitiesAsync(type)).FirstOrDefault(c => c.Id == id);

        return type switch
        {
            "character" => (await _entities.LoadCharactersAsync()).FirstOrDefault(c => c.Id == id),
            "location" => (await _entities.LoadLocationsAsync()).FirstOrDefault(l => l.Id == id),
            "item" => (await _entities.LoadItemsAsync()).FirstOrDefault(i => i.Id == id),
            "lore" => (await _entities.LoadLoreAsync()).FirstOrDefault(l => l.Id == id),
            _ => null
        };
    }

    private async Task SaveEntityAsync(Core.Models.IEntityData entity)
    {
        switch (entity)
        {
            case CharacterData c: await _entities.SaveCharacterAsync(c); break;
            case LocationData l: await _entities.SaveLocationAsync(l); break;
            case ItemData i: await _entities.SaveItemAsync(i); break;
            case LoreData lo: await _entities.SaveLoreAsync(lo); break;
            default: await _entities.SaveCustomEntityAsync((CustomEntityData)entity); break;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static InvalidOperationException Unknown(string id) => new($"Unknown entity '{id}'.");

    private static string Compose(string name, string surname) =>
        surname.Length == 0 ? name : $"{name} {surname}";

    private EntitySummaryDto Summary(IEntityData entity, string detail)
    {
        var character = entity as CharacterData;
        var location = entity as LocationData;
        var name = character == null ? entity.DisplayName : Compose(character.Name, character.Surname);
        var image = entity.Images.FirstOrDefault();
        var firstName = character?.Name;
        var surname = character?.Surname;
        return new(entity.Id, name, detail, entity.IsWorldBible,
            image == null ? null : _entities.ResolveProjectRelativeImage(image.Path),
            entity.Aliases,
            NullIfEmpty(character?.Group), NullIfEmpty(character?.Gender), NullIfEmpty(location?.Parent),
            // The bare first name is an extra hover/mention target ("Liam" for
            // "Liam Calder"); null when it equals the composed display name.
            NullIfEmpty(firstName) is { } fn && !string.Equals(fn, name, StringComparison.Ordinal) ? fn : null,
            // Same rule as the first name: a surname that is the whole display
            // name adds nothing, and matching it twice would only make the
            // entry ambiguous against itself.
            NullIfEmpty(surname) is { } sn && !string.Equals(sn, name, StringComparison.Ordinal) ? sn : null,
            MatchDto(entity.Match, name, entity.Aliases, firstName, surname),
            location?.IsWorld ?? false,
            entity.Locked);
    }

    /// <summary>Projects the stored match settings, precomputing the plural forms
    /// of every matchable text so the client never has to know English plural
    /// rules. Null when nothing is customised, which keeps the common payload
    /// exactly the size it was.</summary>
    private static EntityMatchDto? MatchDto(
        EntityMatchSettings? match, string name, IReadOnlyList<string> aliases, string? firstName,
        string? surname = null)
    {
        if (match == null) return null;
        if (!match.CaseSensitive && !match.MatchPlurals
            && match.Exclusions.Count == 0 && match.IgnoredSceneIds.Count == 0) return null;

        var plurals = new List<string>();
        foreach (var text in new[] { name, firstName, surname }.Concat(aliases))
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            plurals.AddRange(match.PluralFormsOf(text));
        }

        return new EntityMatchDto(
            match.CaseSensitive, match.MatchPlurals,
            [.. match.Exclusions], [.. match.IgnoredSceneIds], [.. plurals.Distinct()]);
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
