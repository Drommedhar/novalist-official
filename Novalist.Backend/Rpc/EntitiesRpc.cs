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
                    c.Id,
                    c.Name,
                    c.Fields.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty,
                    c.IsWorldBible,
                    c.Images.FirstOrDefault(),
                    c.Aliases,
                    match: c.Match,
                    locked: c.Locked))
                .ToArray();
        }
        return type switch
        {
            "character" => (await _entities.LoadCharactersAsync())
                .Select(c => Summary(c.Id, Compose(c.Name, c.Surname), c.Role, c.IsWorldBible, c.Images.FirstOrDefault(), c.Aliases, group: c.Group, gender: c.Gender, firstName: c.Name, surname: c.Surname, match: c.Match, locked: c.Locked))
                .ToArray(),
            "location" => (await _entities.LoadLocationsAsync())
                .Select(l => Summary(l.Id, l.Name, l.Description, l.IsWorldBible, l.Images.FirstOrDefault(), l.Aliases, parent: l.Parent, match: l.Match, isWorld: l.IsWorld, locked: l.Locked))
                .ToArray(),
            "item" => (await _entities.LoadItemsAsync())
                .Select(i => Summary(i.Id, i.Name, i.Description, i.IsWorldBible, i.Images.FirstOrDefault(), i.Aliases, match: i.Match, locked: i.Locked))
                .ToArray(),
            "lore" => (await _entities.LoadLoreAsync())
                .Select(l => Summary(l.Id, l.Name, l.Description, l.IsWorldBible, l.Images.FirstOrDefault(), l.Aliases, match: l.Match, locked: l.Locked))
                .ToArray(),
            _ => throw new InvalidOperationException($"Unknown entity type '{type}'.")
        };
    }

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

    [JsonRpcMethod("entities/updateLists")]
    public async Task<JsonElement> UpdateListsAsync(
        string type,
        string id,
        string[]? aliases,
        EntitySectionDto[]? sections,
        RelationshipRowDto[]? relationships)
    {
        var entity = await FindEntityAsync(type, id) ?? throw Unknown(id);
        RefuseIfLocked(entity);

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
        if (relationships != null)
        {
            // Every built-in type carries relationships, not just characters.
            var rows = relationships
                .Where(r => !string.IsNullOrWhiteSpace(r.Role) || !string.IsNullOrWhiteSpace(r.Target))
                .Select(r => new EntityRelationship { Role = r.Role, Target = r.Target })
                .ToList();
            entity.GetType().GetProperty("Relationships")!.SetValue(entity, rows);
        }

        await SaveEntityAsync(entity);
        return WithResolvedImages(entity);
    }

    /// <summary>
    /// Appends a block of text to one of an entity's free-form sections, creating
    /// that section when it does not exist yet. Used by the editor's "send this
    /// passage to the Codex" capture flow: an atomic append avoids the read/modify/
    /// write race a client-side rewrite of the whole section list would have.
    /// Works for every entity type, including custom ones.
    /// </summary>
    [JsonRpcMethod("entities/appendToSection")]
    public async Task<JsonElement> AppendToSectionAsync(
        string type, string id, string sectionTitle, string text)
    {
        var title = (sectionTitle ?? string.Empty).Trim();
        if (title.Length == 0)
            throw new InvalidOperationException("A section title is required.");
        var addition = (text ?? string.Empty).Trim();
        // Appending prose to a settled entry is a write like any other.
        RefuseIfLocked(await FindAnyAsync(type, id));

        if (IsCustomType(type))
        {
            var custom = (await _entities.LoadCustomEntitiesAsync(type)).FirstOrDefault(c => c.Id == id)
                ?? throw Unknown(id);
            AppendSection(custom.Sections, title, addition);
            await _entities.SaveCustomEntityAsync(custom);
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

        AppendSection(
            (List<EntitySection>)entity.GetType().GetProperty("Sections")!.GetValue(entity)!,
            title, addition);

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

    /// <summary>Appends to the named section (case-insensitive), separating the new
    /// text from existing content by a blank line. Creates the section if missing.</summary>
    private static void AppendSection(List<EntitySection> sections, string title, string addition)
    {
        var section = sections.FirstOrDefault(s =>
            string.Equals(s.Title, title, StringComparison.OrdinalIgnoreCase));
        if (section == null)
        {
            sections.Add(new EntitySection { Title = title, Content = addition });
            return;
        }
        section.Content = section.Content.Length == 0
            ? addition
            : $"{section.Content.TrimEnd()}\n\n{addition}";
    }

    [JsonRpcMethod("entities/inverseRole")]
    public string InverseRole(string role) =>
        _workspace.Settings.Settings.GetKnownInverseRoles(role).FirstOrDefault() ?? string.Empty;

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

    /// <summary>
    /// Writes an entry's relationships and, for each row naming an entry that
    /// exists and carrying an inverse role, authors the reciprocal on that entry
    /// and learns the role pair.
    ///
    /// <paramref name="type"/> defaults to "character" so a caller written when
    /// this was character-only keeps working. It is not character-only any more:
    /// an item's owner link used to be stored verbatim and never authored on the
    /// owner's record, so the relationship existed from one side and not the
    /// other, and the graph could not see it.
    /// </summary>
    [JsonRpcMethod("entities/setRelationships")]
    public async Task<JsonElement> SetRelationshipsAsync(
        string id, RelationshipEditRowDto[] rows, string type = "character")
    {
        var subject = await FindEntityAsync(type, id) ?? throw Unknown(id);

        // The rule itself lives in core, so an extension writing a relationship
        // gets the same write-back the Codex does rather than a copy of it.
        var result = Core.Services.RelationshipWriter.Apply(
            subject,
            subject.DisplayName,
            [.. rows.Select(r => new Core.Services.RelationshipRow(
                r.Role, r.Target, r.Category, r.InverseRole))],
            await AllEntitiesAsync());

        await SaveEntityAsync(subject);
        foreach (var target in result.Changed)
            await SaveEntityAsync(target);

        var settingsChanged = false;
        foreach (var (role, inverse) in result.Pairs)
            settingsChanged |= _workspace.Settings.Settings.LearnRelationshipPair(role, inverse);
        if (settingsChanged) await _workspace.Settings.SaveAsync();

        return WithResolvedImages(subject);
    }

    /// <summary>Every Codex entry of every type, custom types included.</summary>
    private async Task<List<Core.Models.IEntityData>> AllEntitiesAsync()
    {
        var all = new List<Core.Models.IEntityData>();
        all.AddRange(await _entities.LoadCharactersAsync());
        all.AddRange(await _entities.LoadLocationsAsync());
        all.AddRange(await _entities.LoadItemsAsync());
        all.AddRange(await _entities.LoadLoreAsync());
        foreach (var typeDef in _entities.GetCustomEntityTypes())
            all.AddRange(await _entities.LoadCustomEntitiesAsync(typeDef.TypeKey));
        return all;
    }

    /// <summary>
    /// Every group name any entry uses, so the picker offers what this project
    /// actually has rather than asking for it to be spelled the same way twice.
    /// </summary>
    [JsonRpcMethod("entities/groups")]
    public async Task<string[]> GroupsAsync()
        => [.. (await AllEntitiesAsync())
            .Select(e => e.Group)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Puts an entry in a group, or takes it out with an empty name. Any type:
    /// a faction spans them, which is the whole reason a group is worth having.
    /// </summary>
    [JsonRpcMethod("entities/setGroup")]
    public async Task<string[]> SetGroupAsync(string type, string id, string? group)
    {
        var entity = await FindEntityAsync(type, id) ?? throw Unknown(id);
        entity.Group = (group ?? string.Empty).Trim();
        await SaveEntityAsync(entity);
        return await GroupsAsync();
    }

    /// <summary>
    /// Moves a place in the tree. An empty parent lifts it to the top.
    ///
    /// Reparenting used to mean typing into an autocomplete field, so nothing
    /// ever checked the answer: a place could be made its own ancestor and the
    /// whole branch would silently vanish, because a cycle has no root and the
    /// renderer refuses to recurse forever. Returns false when the move was
    /// refused, so a drag can snap back rather than appear to have worked.
    /// </summary>
    [JsonRpcMethod("entities/setParent")]
    public async Task<bool> SetParentAsync(string id, string? parentName)
    {
        var places = await _entities.LoadLocationsAsync();
        var child = places.FirstOrDefault(l => l.Id == id) ?? throw Unknown(id);

        if (!Core.Services.PlaceHierarchy.CanReparent(places, child, parentName)) return false;

        child.Parent = (parentName ?? string.Empty).Trim();
        await _entities.SaveLocationAsync(child);
        return true;
    }

    /// <summary>
    /// Marks a place as a world, or stops it being one. A world sits at the top
    /// of the tree, so becoming one drops whatever parent it had - there is
    /// nothing above a world, which is what makes it one.
    /// </summary>
    [JsonRpcMethod("entities/setIsWorld")]
    public async Task<bool> SetIsWorldAsync(string id, bool isWorld)
    {
        var place = (await _entities.LoadLocationsAsync()).FirstOrDefault(l => l.Id == id)
            ?? throw Unknown(id);

        place.IsWorld = isWorld;
        if (isWorld) place.Parent = string.Empty;
        await _entities.SaveLocationAsync(place);
        return true;
    }

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
        object entity = type switch
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

    private EntitySummaryDto Summary(
        string id, string name, string detail, bool isWorldBible, EntityImage? image,
        IReadOnlyList<string> aliases,
        string? group = null, string? gender = null, string? parent = null, string? firstName = null,
        string? surname = null,
        EntityMatchSettings? match = null, bool isWorld = false, bool locked = false) =>
        new(id, name, detail, isWorldBible,
            image == null ? null : _entities.ResolveProjectRelativeImage(image.Path),
            aliases,
            NullIfEmpty(group), NullIfEmpty(gender), NullIfEmpty(parent),
            // The bare first name is an extra hover/mention target ("Liam" for
            // "Liam Calder"); null when it equals the composed display name.
            NullIfEmpty(firstName) is { } fn && !string.Equals(fn, name, StringComparison.Ordinal) ? fn : null,
            // Same rule as the first name: a surname that is the whole display
            // name adds nothing, and matching it twice would only make the
            // entry ambiguous against itself.
            NullIfEmpty(surname) is { } sn && !string.Equals(sn, name, StringComparison.Ordinal) ? sn : null,
            MatchDto(match, name, aliases, firstName, surname),
            isWorld,
            locked);

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
