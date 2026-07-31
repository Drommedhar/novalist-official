using System.Text.Json;
using Novalist.Backend.Extensions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using Novalist.Sdk.Hooks;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Codex entity access: list summaries per type, fetch full records.</summary>
public sealed class EntitiesRpc
{
    private readonly Workspace _workspace;
    private readonly EntityService _entities;
    private readonly HttpClient _http;

    public EntitiesRpc(Workspace workspace, HttpClient? http = null)
    {
        _workspace = workspace;
        _entities = new EntityService(workspace.Projects);
        _http = http ?? new HttpClient();
    }

    [JsonRpcMethod("entities/customTypes")]
    public CustomEntityTypeDefinition[] GetCustomTypes() =>
        _entities.GetCustomEntityTypes().ToArray();

    /// <summary>
    /// Starting points for the entity types a worldbuilder ends up needing.
    ///
    /// The type builder is an empty form, so everybody who wants species, a
    /// magic system, factions or a language rebuilds the same field list by
    /// hand and rebuilds it differently every time. A pack fills the form in
    /// and then gets out of the way - nothing is created until the writer
    /// saves, and the fields are theirs to change first.
    /// </summary>
    [JsonRpcMethod("entities/typePacks")]
    public CustomTypeSpecDto[] TypePacks()
        => [.. GenreTypePacks.All.Select(pack => new CustomTypeSpecDto(
            pack.TypeKey,
            pack.DisplayName,
            pack.DisplayNamePlural,
            [.. pack.DefaultFields.Select(f => new CustomFieldSpecDto(
                f.Key, f.DisplayName, f.Type.ToString(), f.DefaultValue, f.EnumOptions?.ToArray(),
                f.Required, f.Prompt))],
            pack.Features.IncludeImages,
            pack.Features.IncludeRelationships,
            pack.Features.IncludeSections))];

    [JsonRpcMethod("entities/saveCustomType")]
    public async Task<CustomEntityTypeDefinition[]> SaveCustomTypeAsync(CustomTypeSpecDto spec)
    {
        var isEditing = !string.IsNullOrWhiteSpace(spec.TypeKey);
        if (isEditing)
        {
            var existing = _entities.GetCustomEntityTypes().FirstOrDefault(d => d.TypeKey == spec.TypeKey);
            if (existing is { IsUserSource: false })
                throw new InvalidOperationException($"Custom type is not editable: {spec.TypeKey}");
        }
        var key = isEditing ? spec.TypeKey! : GenerateTypeKey(spec.DisplayName);
        var name = spec.DisplayName.Trim();
        await _entities.SaveCustomEntityTypeAsync(new CustomEntityTypeDefinition
        {
            TypeKey = key,
            DisplayName = name,
            DisplayNamePlural = string.IsNullOrWhiteSpace(spec.DisplayNamePlural) ? name + "s" : spec.DisplayNamePlural.Trim(),
            Icon = string.Empty,
            FolderName = key,
            Source = "user",
            DefaultFields = (spec.Fields ?? []).Select(f => new CustomEntityFieldDefinition
            {
                Key = string.IsNullOrWhiteSpace(f.Key)
                    ? f.DisplayName.Replace(" ", "", StringComparison.Ordinal)
                    : f.Key,
                DisplayName = f.DisplayName,
                Type = Enum.Parse<CustomPropertyType>(f.Type, ignoreCase: true),
                DefaultValue = f.DefaultValue ?? string.Empty,
                EnumOptions = f.EnumOptions is { Length: > 0 } ? [.. f.EnumOptions] : null,
                Required = f.Required,
                Prompt = (f.Prompt ?? string.Empty).Trim()
            }).ToList(),
            Features = new CustomEntityFeatures
            {
                IncludeImages = spec.IncludeImages,
                IncludeRelationships = spec.IncludeRelationships,
                IncludeSections = spec.IncludeSections
            }
        });
        return GetCustomTypes();
    }

    [JsonRpcMethod("entities/deleteCustomType")]
    public async Task<CustomEntityTypeDefinition[]> DeleteCustomTypeAsync(string typeKey)
    {
        var definition = _entities.GetCustomEntityTypes().FirstOrDefault(d => d.TypeKey == typeKey)
            ?? throw new InvalidOperationException($"Unknown custom type: {typeKey}");
        if (!definition.IsUserSource)
            throw new InvalidOperationException($"Custom type is not deletable: {typeKey}");
        await _entities.DeleteCustomEntityTypeAsync(typeKey);
        return GetCustomTypes();
    }

    private static string GenerateTypeKey(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return "custom_" + Guid.NewGuid().ToString("N")[..8];
        return string.Concat(displayName.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_'))
            .Trim('_');
    }

    private bool IsCustomType(string type) =>
        _entities.GetCustomEntityTypes().Any(d => d.TypeKey == type);

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
                .Select(c => Summary(c.Id, Compose(c.Name, c.Surname), c.Role, c.IsWorldBible, c.Images.FirstOrDefault(), c.Aliases, group: c.Group, gender: c.Gender, firstName: c.Name, match: c.Match, locked: c.Locked))
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
        if (relationships != null)
        {
            // Every built-in type carries relationships, not just characters.
            var rows = relationships
                .Where(r => !string.IsNullOrWhiteSpace(r.Role) || !string.IsNullOrWhiteSpace(r.Target))
                .Select(r => new EntityRelationship { Role = r.Role, Target = r.Target })
                .ToList();
            entity.GetType().GetProperty("Relationships")!.SetValue(entity, rows);
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

    /// <summary>Whether an extension offers an enabled entity extractor. Drives the
    /// Inspector's "find new entries in this scene" affordance.</summary>
    [JsonRpcMethod("entities/extractorAvailable")]
    public bool ExtractorAvailable()
        => _workspace.ExtensionHostOrNull?.IsEntityExtractorAvailable ?? false;

    /// <summary>
    /// Asks an extension to propose Codex entries for the people, places, and
    /// things a scene mentions that are not in the Codex yet. Returns proposals
    /// only — nothing is written until the caller creates them via
    /// <c>entities/create</c>. Names the project already knows, and proposals with
    /// an unknown type key, are filtered out here rather than trusted.
    /// </summary>
    [JsonRpcMethod("entities/extractFromScene")]
    public async Task<EntityProposalsDto> ExtractFromSceneAsync(
        string chapterGuid, string sceneId, CancellationToken cancellationToken)
    {
        var host = _workspace.ExtensionHostOrNull;
        if (host == null || !host.IsEntityExtractorAvailable)
            return new EntityProposalsDto([], null);

        var (chapter, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        var prose = TextDiff.StripHtml(
            await _workspace.Projects.ReadSceneContentAsync(chapter, scene));
        if (string.IsNullOrWhiteSpace(prose))
            return new EntityProposalsDto([], null);

        var known = await BuildKnownNamesAsync();
        var typeKeys = new List<string> { "character", "location", "item", "lore" };
        typeKeys.AddRange(_entities.GetCustomEntityTypes().Select(t => t.TypeKey));

        // Non-null: availability was just checked above.
        var result = (await host.ExtractEntitiesAsync(
            new EntityExtractionRequest
            {
                Context = prose,
                KnownNames = known.ToArray(),
                AvailableTypeKeys = typeKeys
            },
            cancellationToken))!;

        if (!string.IsNullOrEmpty(result.Error))
            return new EntityProposalsDto([], result.Error);

        var proposals = result.Proposals
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Where(p => typeKeys.Contains(p.TypeKey, StringComparer.OrdinalIgnoreCase))
            .Where(p => !known.Contains(EntityResolveIndex.Normalize(p.Name)))
            .GroupBy(p => EntityResolveIndex.Normalize(p.Name), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select(p => new EntityProposalDto(p.TypeKey, p.Name.Trim(), p.Detail))
            .ToArray();

        Log.Info($"entities/extractFromScene returned={result.Proposals.Count} kept={proposals.Length}.");
        return new EntityProposalsDto(proposals, null);
    }

    /// <summary>Every name and alias the Codex already knows, normalized — used to
    /// drop redundant proposals.</summary>
    private async Task<HashSet<string>> BuildKnownNamesAsync()
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? value)
        {
            var normalized = EntityResolveIndex.Normalize(value);
            if (normalized.Length > 0) known.Add(normalized);
        }

        foreach (var c in await _entities.LoadCharactersAsync())
        {
            Add(Compose(c.Name, c.Surname));
            Add(c.Name);
            foreach (var alias in c.Aliases) Add(alias);
        }
        foreach (var l in await _entities.LoadLocationsAsync())
        {
            Add(l.Name);
            foreach (var alias in l.Aliases) Add(alias);
        }
        foreach (var i in await _entities.LoadItemsAsync())
        {
            Add(i.Name);
            foreach (var alias in i.Aliases) Add(alias);
        }
        foreach (var l in await _entities.LoadLoreAsync())
        {
            Add(l.Name);
            foreach (var alias in l.Aliases) Add(alias);
        }
        foreach (var typeDef in _entities.GetCustomEntityTypes())
            foreach (var e in await _entities.LoadCustomEntitiesAsync(typeDef.TypeKey))
            {
                Add(e.Name);
                foreach (var alias in e.Aliases) Add(alias);
            }
        return known;
    }

    [JsonRpcMethod("entities/relationshipSuggestions")]
    public async Task<RelationshipSuggestionsDto> RelationshipSuggestionsAsync()
    {
        var characters = await _entities.LoadCharactersAsync();
        // Every entry, not only the characters. A place is owned by somebody, a
        // relic belongs to a house, a law binds a city - and offering only
        // character names to a location's relationship row is why nobody ever
        // filled one in.
        var everyName = characters.Select(c => Compose(c.Name, c.Surname))
            .Concat((await _entities.LoadLocationsAsync()).Select(l => l.Name))
            .Concat((await _entities.LoadItemsAsync()).Select(i => i.Name))
            .Concat((await _entities.LoadLoreAsync()).Select(l => l.Name));

        foreach (var type in _entities.GetCustomEntityTypes())
            everyName = everyName.Concat(
                (await _entities.LoadCustomEntitiesAsync(type.TypeKey)).Select(e => e.Name));

        var names = everyName
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        // Roles already used anywhere, so a second location can be filed the
        // same way as the first rather than by remembering the wording.
        var roles = characters.SelectMany(c => c.Relationships.Select(r => r.Role))
            .Concat((await _entities.LoadLocationsAsync()).SelectMany(l => l.Relationships.Select(r => r.Role)))
            .Concat((await _entities.LoadItemsAsync()).SelectMany(i => i.Relationships.Select(r => r.Role)))
            .Concat((await _entities.LoadLoreAsync()).SelectMany(l => l.Relationships.Select(r => r.Role)))
            .Concat(_workspace.Settings.Settings.RelationshipPairs.Keys)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new RelationshipSuggestionsDto(names, roles);
    }

    [JsonRpcMethod("entities/inverseRole")]
    public string InverseRole(string role) =>
        _workspace.Settings.Settings.GetKnownInverseRoles(role).FirstOrDefault() ?? string.Empty;

    /// <summary>
    /// Writes a character's relationships and, for each row that names an
    /// existing character and carries an inverse role, adds the reciprocal
    /// relationship on that target and learns the role pair (ported from
    /// EntityEditorViewModel.SyncInverseRelationshipsAsync).
    /// </summary>
    /// <summary>
    /// Whether this entry may reach an AI model, and which of its sections are
    /// withheld. Read by the Codex panel; enforced for extensions by
    /// <see cref="Core.Services.AiContextPolicy"/>.
    /// </summary>
    [JsonRpcMethod("entities/getAiPolicy")]
    public async Task<AiPolicyDto> GetAiPolicyAsync(string type, string id)
    {
        var entity = await FindEntityAsync(type, id);
        return new AiPolicyDto(
            (entity?.Ai ?? Core.Models.AiInclusion.WhenMentioned).ToString(),
            [.. SectionsOf(entity).Select((sec, index) =>
                new AiSectionDto(index, sec.Title, sec.AiHidden))]);
    }

    /// <summary>
    /// Sets the entry's inclusion, and which sections are withheld by index.
    /// Indices that no longer name a section are ignored rather than throwing:
    /// the panel's view of the sections can be one edit behind.
    /// </summary>
    [JsonRpcMethod("entities/setAiPolicy")]
    public async Task<AiPolicyDto> SetAiPolicyAsync(string type, string id, string inclusion, int[] hiddenSections)
    {
        var entity = await FindEntityAsync(type, id) ?? throw Unknown(id);

        entity.Ai = Enum.TryParse<Core.Models.AiInclusion>(inclusion, ignoreCase: true, out var parsed)
            ? parsed
            // An unknown value falls back to the default rather than to Never:
            // silently hiding an entry the writer expects the model to see is
            // the more surprising failure of the two.
            : Core.Models.AiInclusion.WhenMentioned;

        var hidden = (hiddenSections ?? []).ToHashSet();
        var sections = SectionsOf(entity);
        for (var i = 0; i < sections.Count; i++) sections[i].AiHidden = hidden.Contains(i);

        await SaveEntityAsync(entity);
        return await GetAiPolicyAsync(type, id);
    }

    /// <summary>
    /// Who among readers may see this entry, and which of its sections.
    ///
    /// A separate axis from the AI policy on purpose: a writer may be happy for
    /// a model to know the twist while planning and never for a reader to find
    /// it in a world page. One switch for both would force a choice nobody
    /// should have to make.
    /// </summary>
    [JsonRpcMethod("entities/getReaderPolicy")]
    public async Task<ReaderPolicyDto> GetReaderPolicyAsync(string type, string id)
    {
        var entity = await FindEntityAsync(type, id);
        return new ReaderPolicyDto(
            entity?.ReaderHidden ?? false,
            [.. SectionsOf(entity).Select((sec, index) =>
                new ReaderSectionDto(index, sec.Title, sec.ReaderHidden))]);
    }

    /// <summary>
    /// Sets whether the entry, and which of its sections, are kept from
    /// readers. Indices that no longer name a section are ignored: the panel's
    /// view of the sections can be one edit behind.
    /// </summary>
    [JsonRpcMethod("entities/setReaderPolicy")]
    public async Task<ReaderPolicyDto> SetReaderPolicyAsync(
        string type, string id, bool hidden, int[] hiddenSections)
    {
        var entity = await FindEntityAsync(type, id) ?? throw Unknown(id);

        entity.ReaderHidden = hidden;
        var withheld = (hiddenSections ?? []).ToHashSet();
        var sections = SectionsOf(entity);
        for (var i = 0; i < sections.Count; i++) sections[i].ReaderHidden = withheld.Contains(i);

        await SaveEntityAsync(entity);
        return await GetReaderPolicyAsync(type, id);
    }

    /// <summary>The entry's rich-text sections, or none for a type that has
    /// no section support.</summary>
    private static List<Core.Models.EntitySection> SectionsOf(Core.Models.IEntityData? entity)
        => entity switch
        {
            CharacterData c => c.Sections,
            LocationData l => l.Sections,
            ItemData i => i.Sections,
            LoreData lo => lo.Sections,
            CustomEntityData ce => ce.Sections,
            _ => []
        };

    /// <summary>What this entry is like at particular points in the story.</summary>
    [JsonRpcMethod("entities/getStateOverrides")]
    public async Task<StateOverrideDto[]> GetStateOverridesAsync(string type, string id)
    {
        var entity = await FindEntityAsync(type, id);
        return [.. (entity?.StateOverrides ?? []).Select(ToDto)];
    }

    /// <summary>
    /// Replaces the entry's state overrides. One that restates nothing is
    /// dropped, since an empty override would claim the entry differs at that
    /// point while saying nothing about how.
    /// </summary>
    [JsonRpcMethod("entities/setStateOverrides")]
    public async Task<StateOverrideDto[]> SetStateOverridesAsync(
        string type, string id, StateOverrideDto[] overrides)
    {
        var entity = await FindEntityAsync(type, id) ?? throw Unknown(id);

        entity.StateOverrides = [.. (overrides ?? [])
            .Select(o => new Core.Models.EntityStateOverride
            {
                Act = NullIfEmpty(o.Act),
                Chapter = o.Chapter ?? string.Empty,
                Scene = NullIfEmpty(o.Scene),
                Name = NullIfEmpty(o.Name),
                Description = NullIfEmpty(o.Description),
                Note = NullIfEmpty(o.Note),
                Gone = o.Gone,
                Fields = o.Fields is { Count: > 0 }
                    ? o.Fields.Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
                        .ToDictionary(kv => kv.Key.Trim(), kv => kv.Value ?? string.Empty)
                    : null
            })
            .Where(o => o.HasValues)];

        await SaveEntityAsync(entity);
        return await GetStateOverridesAsync(type, id);
    }

    /// <summary>
    /// What the entry is like in the given context. Returns the restated values
    /// and the scope they came from, so a reader can be told they are seeing
    /// the entry at a point in the story rather than in general.
    /// </summary>
    [JsonRpcMethod("entities/resolveState")]
    public async Task<ResolvedStateDto> ResolveStateAsync(
        string type, string id, string? act, string? chapterGuid,
        string? chapterTitle, string? sceneTitle)
    {
        var entity = await FindEntityAsync(type, id);
        var resolved = Core.Services.EntityStateResolver.Resolve(
            entity?.StateOverrides ?? [], act, chapterGuid, chapterTitle, sceneTitle);

        return new ResolvedStateDto(
            resolved.Name, resolved.Description,
            resolved.Fields.ToDictionary(kv => kv.Key, kv => kv.Value),
            resolved.Note, resolved.ScopeLabel, resolved.IsOverridden);
    }

    private static StateOverrideDto ToDto(Core.Models.EntityStateOverride o)
        => new(o.Act, o.Chapter, o.Scene, o.Name, o.Description,
            o.Fields?.ToDictionary(kv => kv.Key, kv => kv.Value), o.Note, o.Gone);

    /// <summary>How an entry's name is recognised in prose.</summary>
    [JsonRpcMethod("entities/getMatchSettings")]
    public async Task<MatchSettingsDto> GetMatchSettingsAsync(string type, string id)
    {
        var entity = await FindEntityAsync(type, id);
        var match = entity?.Match ?? new Core.Models.EntityMatchSettings();
        return new MatchSettingsDto(
            match.CaseSensitive,
            match.MatchPlurals,
            match.Exclusions.ToArray(),
            match.IgnoredSceneIds.ToArray());
    }

    /// <summary>
    /// Replaces an entry's match settings. Blank exclusions are dropped so a
    /// half-typed row cannot silently suppress every detection.
    /// </summary>
    [JsonRpcMethod("entities/setMatchSettings")]
    public async Task<MatchSettingsDto> SetMatchSettingsAsync(
        string type, string id, bool caseSensitive, bool matchPlurals,
        string[] exclusions, string[] ignoredSceneIds)
    {
        var entity = await FindEntityAsync(type, id) ?? throw Unknown(id);

        entity.Match = new Core.Models.EntityMatchSettings
        {
            CaseSensitive = caseSensitive,
            MatchPlurals = matchPlurals,
            Exclusions = (exclusions ?? [])
                .Select(e => (e ?? string.Empty).Trim())
                .Where(e => e.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            IgnoredSceneIds = (ignoredSceneIds ?? [])
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.Ordinal)
                .ToList()
        };

        await SaveEntityAsync(entity);
        return await GetMatchSettingsAsync(type, id);
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

    [JsonRpcMethod("entities/setOverride")]
    public async Task<JsonElement> SetOverrideAsync(
        string characterId,
        string chapterGuid,
        string? sceneTitle,
        Dictionary<string, string> fields,
        Dictionary<string, string>? customProperties = null)
    {
        var character = (await _entities.LoadCharactersAsync()).FirstOrDefault(c => c.Id == characterId)
            ?? throw Unknown(characterId);
        var existing = FindOrCreateOverride(character, chapterGuid, sceneTitle);
        foreach (var (key, value) in fields)
        {
            var property = typeof(CharacterOverride).GetProperty(
                char.ToUpperInvariant(key[0]) + key[1..]);
            if (property?.CanWrite == true && property.PropertyType == typeof(string))
            {
                // Empty means inherit the base value (stored as null, the diff model).
                property.SetValue(existing, string.IsNullOrEmpty(value) ? null : value);
            }
        }

        // Per-scope custom-property overrides layer over the base set in the peek
        // card; an empty map clears them (inherit the base entirely). Blank values
        // are dropped so a cleared field inherits rather than blanks the base.
        if (customProperties != null)
        {
            var kept = customProperties
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            existing.CustomProperties = kept.Count == 0 ? null : kept;
        }

        await _entities.SaveCharacterAsync(character);
        return WithResolvedImages(character);
    }

    [JsonRpcMethod("entities/removeOverride")]
    public async Task<JsonElement> RemoveOverrideAsync(
        string characterId, string chapterGuid, string? sceneTitle)
    {
        var character = (await _entities.LoadCharactersAsync()).FirstOrDefault(c => c.Id == characterId)
            ?? throw Unknown(characterId);
        character.ChapterOverrides.RemoveAll(o =>
            o.Chapter == chapterGuid && (o.Scene ?? string.Empty) == (sceneTitle ?? string.Empty));
        await _entities.SaveCharacterAsync(character);
        return WithResolvedImages(character);
    }

    /// <summary>
    /// Replaces the per-scope override image list. <paramref name="images"/> null
    /// resets the scope to inherit the base entity's images; a list (possibly
    /// empty) replaces them wholesale — the desktop
    /// <c>EntityEditorViewModel</c> override-image semantics (null = inherit,
    /// otherwise the override owns the list). Used by the inline overrides editor
    /// for gallery-add, remove, rename, and reset-to-inherit.
    /// </summary>
    [JsonRpcMethod("entities/setOverrideImages")]
    public async Task<JsonElement> SetOverrideImagesAsync(
        string characterId, string chapterGuid, string? sceneTitle, EntityImageDto[]? images)
    {
        var character = await LoadCharacterAsync(characterId);
        var ovr = FindOrCreateOverride(character, chapterGuid, sceneTitle);
        ovr.Images = images?
            .Select(i => new EntityImage { Name = i.Name, Path = i.Path })
            .ToList();
        await _entities.SaveCharacterAsync(character);
        return WithResolvedImages(character);
    }

    /// <summary>Imports a file into the entity image folder and appends it to the
    /// scope's override image list (seeding the list from the base images the
    /// first time it diverges). Serves both file-picker and clipboard paste.</summary>
    [JsonRpcMethod("entities/addOverrideImage")]
    public async Task<JsonElement> AddOverrideImageAsync(
        string characterId, string chapterGuid, string? sceneTitle, string path)
    {
        var relative = await _entities.ImportImageAsync(path);
        var name = Path.GetFileNameWithoutExtension(relative);
        return await MutateOverrideImagesAsync(characterId, chapterGuid, sceneTitle,
            images => images.Add(new EntityImage { Name = name, Path = relative }));
    }

    /// <summary>Downloads a remote image and appends it to the scope's override
    /// image list (seeding from the base images on first divergence).</summary>
    [JsonRpcMethod("entities/addOverrideImageFromUrl")]
    public async Task<JsonElement> AddOverrideImageFromUrlAsync(
        string characterId, string chapterGuid, string? sceneTitle, string url)
    {
        var (relative, fileName) = await DownloadAndImportImageAsync(url);
        return await MutateOverrideImagesAsync(characterId, chapterGuid, sceneTitle,
            images => images.Add(new EntityImage
            {
                Name = Path.GetFileNameWithoutExtension(fileName),
                Path = relative
            }));
    }

    /// <summary>Replaces the per-scope override relationship list. Null resets the
    /// scope to inherit the base relationships; a list (possibly empty, blank rows
    /// dropped) replaces them. Mirrors desktop override-relationship semantics.</summary>
    [JsonRpcMethod("entities/setOverrideRelationships")]
    public async Task<JsonElement> SetOverrideRelationshipsAsync(
        string characterId, string chapterGuid, string? sceneTitle, RelationshipRowDto[]? relationships)
    {
        var character = await LoadCharacterAsync(characterId);
        var ovr = FindOrCreateOverride(character, chapterGuid, sceneTitle);
        ovr.Relationships = relationships?
            .Where(r => !string.IsNullOrWhiteSpace(r.Role) || !string.IsNullOrWhiteSpace(r.Target))
            .Select(r => new EntityRelationship { Role = r.Role.Trim(), Target = r.Target.Trim() })
            .ToList();
        await _entities.SaveCharacterAsync(character);
        return WithResolvedImages(character);
    }

    /// <summary>Replaces the per-scope override section list. Null resets the scope
    /// to inherit the base sections; a list (possibly empty) replaces them. Mirrors
    /// desktop override-section semantics.</summary>
    [JsonRpcMethod("entities/setOverrideSections")]
    public async Task<JsonElement> SetOverrideSectionsAsync(
        string characterId, string chapterGuid, string? sceneTitle, EntitySectionDto[]? sections)
    {
        var character = await LoadCharacterAsync(characterId);
        var ovr = FindOrCreateOverride(character, chapterGuid, sceneTitle);
        ovr.Sections = sections?
            .Select(s => new EntitySection { Title = s.Title, Content = s.Content })
            .ToList();
        await _entities.SaveCharacterAsync(character);
        return WithResolvedImages(character);
    }

    private async Task<CharacterData> LoadCharacterAsync(string characterId) =>
        (await _entities.LoadCharactersAsync()).FirstOrDefault(c => c.Id == characterId)
            ?? throw Unknown(characterId);

    /// <summary>Finds the chapter/scene override matching the scope, creating and
    /// appending an empty one when absent. Scene is matched exactly (null == "").</summary>
    private static CharacterOverride FindOrCreateOverride(
        CharacterData character, string chapterGuid, string? sceneTitle)
    {
        var existing = character.ChapterOverrides.FirstOrDefault(o =>
            o.Chapter == chapterGuid && (o.Scene ?? string.Empty) == (sceneTitle ?? string.Empty));
        if (existing == null)
        {
            existing = new CharacterOverride { Chapter = chapterGuid, Scene = sceneTitle };
            character.ChapterOverrides.Add(existing);
        }
        return existing;
    }

    /// <summary>Mutates the scope's override image list in place, seeding it from a
    /// deep copy of the base images the first time the scope diverges (so an add
    /// starts from what the peek currently shows), then persists.</summary>
    private async Task<JsonElement> MutateOverrideImagesAsync(
        string characterId, string chapterGuid, string? sceneTitle, Action<List<EntityImage>> mutate)
    {
        var character = await LoadCharacterAsync(characterId);
        var ovr = FindOrCreateOverride(character, chapterGuid, sceneTitle);
        var images = ovr.Images
            ?? character.Images.Select(i => new EntityImage { Name = i.Name, Path = i.Path }).ToList();
        mutate(images);
        ovr.Images = images;
        await _entities.SaveCharacterAsync(character);
        return WithResolvedImages(character);
    }

    [JsonRpcMethod("entities/customProps")]
    public async Task<CustomPropDto[]> GetCustomPropsAsync(string type, string id)
    {
        var (entity, templateId) = await LoadWithTemplateAsync(type, id);
        var props = (Dictionary<string, string>)entity.GetType()
            .GetProperty("CustomProperties")!.GetValue(entity)!;
        var defs = ResolvePropertyDefs(type, templateId);
        return props
            .Select(kv =>
            {
                var def = defs.FirstOrDefault(d => d.Key == kv.Key);
                return new CustomPropDto(
                    kv.Key,
                    kv.Value,
                    (def?.Type ?? CustomPropertyType.String).ToString(),
                    def?.EnumOptions?.ToArray() ?? []);
            })
            .ToArray();
    }

    [JsonRpcMethod("entities/setCustomProp")]
    public async Task<CustomPropDto[]> SetCustomPropAsync(string type, string id, string key, string? value)
    {
        var (entity, _) = await LoadWithTemplateAsync(type, id);
        var props = (Dictionary<string, string>)entity.GetType()
            .GetProperty("CustomProperties")!.GetValue(entity)!;
        if (value == null) props.Remove(key);
        else props[key] = value;
        await SaveEntityAsync(entity);
        return await GetCustomPropsAsync(type, id);
    }

    private async Task<(object Entity, string? TemplateId)> LoadWithTemplateAsync(string type, string id)
    {
        object entity = type switch
        {
            "character" => (await _entities.LoadCharactersAsync()).FirstOrDefault(c => c.Id == id) as object,
            "location" => (await _entities.LoadLocationsAsync()).FirstOrDefault(l => l.Id == id),
            "item" => (await _entities.LoadItemsAsync()).FirstOrDefault(i => i.Id == id),
            "lore" => (await _entities.LoadLoreAsync()).FirstOrDefault(l => l.Id == id),
            _ => throw new InvalidOperationException($"Unknown entity type '{type}'.")
        } ?? throw Unknown(id);
        var templateId = entity.GetType().GetProperty("TemplateId")?.GetValue(entity) as string;
        return (entity, templateId);
    }

    private List<CustomPropertyDefinition> ResolvePropertyDefs(string type, string? templateId)
    {
        var book = _workspace.Projects.ActiveBook;
        if (book == null || templateId == null) return [];
        return type switch
        {
            "character" => book.CharacterTemplates.FirstOrDefault(t => t.Id == templateId)?.CustomPropertyDefs ?? [],
            "location" => book.LocationTemplates.FirstOrDefault(t => t.Id == templateId)?.CustomPropertyDefs ?? [],
            "item" => book.ItemTemplates.FirstOrDefault(t => t.Id == templateId)?.CustomPropertyDefs ?? [],
            _ => book.LoreTemplates.FirstOrDefault(t => t.Id == templateId)?.CustomPropertyDefs ?? []
        };
    }

    private async Task SaveEntityAsync(object entity)
    {
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
    }

    [JsonRpcMethod("entities/addImage")]
    public async Task<JsonElement> AddImageAsync(
        string type, string id, string path, bool import)
    {
        var relative = import ? await _entities.ImportImageAsync(path) : path;
        var name = Path.GetFileNameWithoutExtension(relative);
        return await MutateImagesAsync(type, id, images =>
            images.Add(new EntityImage { Name = name, Path = relative }));
    }

    [JsonRpcMethod("entities/removeImage")]
    public Task<JsonElement> RemoveImageAsync(string type, string id, string path) =>
        MutateImagesAsync(type, id, images =>
            images.RemoveAll(i => i.Path == path));

    /// <summary>Renames the stored image whose <c>Path</c> matches, leaving its
    /// path (and on-disk file) untouched so add/remove still match on it.</summary>
    /// <summary>
    /// Sets what the picture shows, for a reader who cannot see it. Separate
    /// from the display name: one names the image, the other describes it, and
    /// only the second is any use read aloud.
    /// </summary>
    [JsonRpcMethod("entities/setImageAlt")]
    public Task<JsonElement> SetImageAltAsync(string type, string id, string path, string alt) =>
        MutateImagesAsync(type, id, images =>
        {
            var image = images.FirstOrDefault(i => i.Path == path);
            if (image != null) image.Alt = alt ?? string.Empty;
        });

    [JsonRpcMethod("entities/renameImage")]
    public Task<JsonElement> RenameImageAsync(string type, string id, string path, string newName) =>
        MutateImagesAsync(type, id, images =>
        {
            var image = images.FirstOrDefault(i => i.Path == path);
            if (image != null) image.Name = newName;
        });

    /// <summary>Swaps the stored path of the image matching <paramref name="oldPath"/>
    /// for <paramref name="newPath"/> (a project-relative path already on disk),
    /// keeping the display name unless it was empty.</summary>
    [JsonRpcMethod("entities/replaceImage")]
    public Task<JsonElement> ReplaceImageAsync(string type, string id, string oldPath, string newPath) =>
        MutateImagesAsync(type, id, images =>
        {
            var image = images.FirstOrDefault(i => i.Path == oldPath);
            if (image != null)
            {
                image.Path = newPath;
                if (string.IsNullOrWhiteSpace(image.Name))
                    image.Name = Path.GetFileNameWithoutExtension(newPath);
            }
        });

    private static readonly string[] ImageFileExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg"];

    /// <summary>Downloads an image from a remote URL into the entity's image
    /// folder (via <see cref="EntityService.ImportImageAsync"/>) and attaches it.
    /// Download uses the injected <see cref="HttpClient"/> so tests stub the
    /// transport; a failed request surfaces as a clean error.</summary>
    [JsonRpcMethod("entities/addImageFromUrl")]
    public async Task<JsonElement> AddImageFromUrlAsync(string type, string id, string url)
    {
        var (relative, fileName) = await DownloadAndImportImageAsync(url);
        return await MutateImagesAsync(type, id, images =>
            images.Add(new EntityImage
            {
                Name = Path.GetFileNameWithoutExtension(fileName),
                Path = relative
            }));
    }

    /// <summary>Downloads an image from a remote URL (via the injected
    /// <see cref="HttpClient"/> so tests stub the transport) and imports it into
    /// the entity image folder, returning the project-relative path and the
    /// derived file name. A failed request surfaces as a clean error.</summary>
    private async Task<(string Relative, string FileName)> DownloadAndImportImageAsync(string url)
    {
        byte[] bytes;
        try
        {
            using var response = await _http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            bytes = await response.Content.ReadAsByteArrayAsync();
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("Could not download the image from the given URL.", ex);
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "nl-url-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var fileName = DeriveImageFileName(url);
        var tempPath = Path.Combine(tempDir, fileName);
        await File.WriteAllBytesAsync(tempPath, bytes);

        var relative = await _entities.ImportImageAsync(tempPath);
        Directory.Delete(tempDir, true);
        return (relative, fileName);
    }

    /// <summary>Derives a safe, image-extensioned file name from a download URL,
    /// falling back to <c>image.png</c> when the URL carries no usable segment.</summary>
    private static string DeriveImageFileName(string url)
    {
        var name = "image";
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var segment = uri.Segments.LastOrDefault(s => s.Trim('/').Length > 0)?.Trim('/');
            if (!string.IsNullOrWhiteSpace(segment))
                name = Uri.UnescapeDataString(segment);
        }
        return ImageFileExtensions.Contains(Path.GetExtension(name).ToLowerInvariant())
            ? name
            : name + ".png";
    }

    private async Task<JsonElement> MutateImagesAsync(
        string type, string id, Action<List<EntityImage>> mutate)
    {
        object entity = type switch
        {
            "character" => (await _entities.LoadCharactersAsync()).FirstOrDefault(c => c.Id == id) as object,
            "location" => (await _entities.LoadLocationsAsync()).FirstOrDefault(l => l.Id == id),
            "item" => (await _entities.LoadItemsAsync()).FirstOrDefault(i => i.Id == id),
            "lore" => (await _entities.LoadLoreAsync()).FirstOrDefault(l => l.Id == id),
            _ => throw new InvalidOperationException($"Unknown entity type '{type}'.")
        } ?? throw Unknown(id);

        var images = (List<EntityImage>)entity.GetType().GetProperty("Images")!.GetValue(entity)!;
        mutate(images);

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

    [JsonRpcMethod("entities/templates")]
    public EntityTemplateDto[] GetTemplates(string type)
    {
        var book = _workspace.Projects.ActiveBook
            ?? throw new InvalidOperationException("No project open.");
        return type switch
        {
            "character" => book.CharacterTemplates.Select(t => new EntityTemplateDto(t.Id, t.Name)).ToArray(),
            "location" => book.LocationTemplates.Select(t => new EntityTemplateDto(t.Id, t.Name)).ToArray(),
            "item" => book.ItemTemplates.Select(t => new EntityTemplateDto(t.Id, t.Name)).ToArray(),
            "lore" => book.LoreTemplates.Select(t => new EntityTemplateDto(t.Id, t.Name)).ToArray(),
            _ => book.CustomEntityTemplates
                .Where(t => t.EntityTypeKey == type)
                .Select(t => new EntityTemplateDto(t.Id, t.Name))
                .ToArray()
        };
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

    // Users-icon geometry ported verbatim from FocusPeekExtension.UsersIconPath
    // so the relationship-count pill renders the same glyph as the desktop card.
    private const string UsersIconPath = "M8 12A3 3 0 1 0 8 6A3 3 0 0 0 8 12ZM15.5 10A2.5 2.5 0 1 0 15.5 5A2.5 2.5 0 0 0 15.5 10ZM3.5 19C3.5 16.5147 5.51472 14.5 8 14.5C10.4853 14.5 12.5 16.5147 12.5 19V19.5H3.5V19ZM12.5 19.5V19C12.5 18.0739 12.2503 17.2061 11.8145 16.4601C12.4966 15.8667 13.3879 15.5 14.3654 15.5H14.6346C16.7743 15.5 18.5 17.2257 18.5 19.3654V19.5H12.5Z";

    /// <summary>
    /// Builds the rich focus-peek card payload for a single entity, faithful to
    /// the desktop FocusPeekExtension: type badge, ordered attribute pills,
    /// appearance (characters), custom properties, relationships with resolved
    /// navigate targets, description, sections, and linked map pins. AI findings
    /// are intentionally not returned (no analysis pipeline over RPC — the client
    /// shows the localized stub).
    ///
    /// When the caller passes the open editor's <paramref name="chapterGuid"/> /
    /// <paramref name="chapterTitle"/> / <paramref name="sceneTitle"/>, a matching
    /// character chapter/scene override is resolved and applied (name, surname,
    /// role, gender, age, appearance, relationships, custom properties, images) —
    /// a scene-specific override wins over a chapter-wide one, and each overridden
    /// non-blank field wins over the base value. This ports
    /// <c>FocusPeekExtension.ResolveCharacterOverride</c>. The resolved
    /// <see cref="EntityPeekDto.ScopeLabel"/> names the scope so the card can flag
    /// that it is showing overridden values.
    /// </summary>
    [JsonRpcMethod("entities/peek")]
    public async Task<EntityPeekDto> PeekAsync(
        string type, string id,
        string? chapterGuid = null, string? chapterTitle = null, string? sceneTitle = null)
    {
        var characters = await _entities.LoadCharactersAsync();
        var locations = await _entities.LoadLocationsAsync();
        var items = await _entities.LoadItemsAsync();
        var lore = await _entities.LoadLoreAsync();

        // name (normalized, lowercased) -> single resolvable entity. Names that
        // map to more than one entity are dropped (the desktop Count==1 rule) so
        // a relationship target never navigates to the wrong record.
        var resolveIndex = await BuildResolveIndexAsync(characters, locations, items, lore);

        var pins = await GetMapPinsForEntityAsync(id);

        if (IsCustomType(type))
        {
            var custom = (await _entities.LoadCustomEntitiesAsync(type)).FirstOrDefault(c => c.Id == id)
                ?? throw Unknown(id);
            return await WithAiFindingsAsync(
                BuildCustomPeek(custom, type, resolveIndex, pins),
                chapterGuid, [custom.Name, .. custom.Aliases]);
        }

        var peek = type switch
        {
            "character" => BuildCharacterPeek(
                characters.FirstOrDefault(c => c.Id == id) ?? throw Unknown(id), resolveIndex, pins,
                chapterGuid, chapterTitle, sceneTitle),
            "location" => BuildLocationPeek(
                locations.FirstOrDefault(l => l.Id == id) ?? throw Unknown(id), locations, pins),
            "item" => BuildItemPeek(items.FirstOrDefault(i => i.Id == id) ?? throw Unknown(id), pins),
            "lore" => BuildLorePeek(lore.FirstOrDefault(l => l.Id == id) ?? throw Unknown(id), pins),
            _ => throw new InvalidOperationException($"Unknown entity type '{type}'.")
        };

        return await WithAiFindingsAsync(
            peek, chapterGuid, PeekNames(type, id, characters, locations, items, lore));
    }

    /// <summary>Every name a cached finding might refer to this entity by.</summary>
    private static string[] PeekNames(
        string type, string id,
        IReadOnlyList<CharacterData> characters, IReadOnlyList<LocationData> locations,
        IReadOnlyList<ItemData> items, IReadOnlyList<LoreData> lore)
    {
        switch (type)
        {
            case "character":
                var c = characters.First(e => e.Id == id);
                return [Compose(c.Name, c.Surname), c.Name, .. c.Aliases];
            case "location":
                var l = locations.First(e => e.Id == id);
                return [l.Name, .. l.Aliases];
            case "item":
                var i = items.First(e => e.Id == id);
                return [i.Name, .. i.Aliases];
            default:
                var lo = lore.First(e => e.Id == id);
                return [lo.Name, .. lo.Aliases];
        }
    }

    /// <summary>
    /// Attaches the cached AI-analysis findings that name this entity within the
    /// open chapter. The host only <em>reads</em> what an extension's chapter
    /// analysis stored in <see cref="ProjectSettings.ChapterAnalysis"/> — it never
    /// generates findings itself, so this carries no AI dependency. Empty when no
    /// analysis has been run for the chapter.
    /// </summary>
    private async Task<EntityPeekDto> WithAiFindingsAsync(
        EntityPeekDto peek, string? chapterGuid, string[] names)
    {
        if (string.IsNullOrWhiteSpace(chapterGuid)) return peek;

        var wanted = names
            .Select(EntityResolveIndex.Normalize)
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0) return peek;

        var legacy = _workspace.Projects.ProjectSettings?.ChapterAnalysis is { } map
                     && map.TryGetValue(chapterGuid, out var stored)
            ? stored
            : null;

        // Walk the chapter's scenes in order. A scene analysed under the current
        // per-scene store wins; anything only present in the legacy settings blob
        // still shows, so existing projects keep their findings.
        var store = new SceneAnalysisStore(_workspace.Projects, _workspace.FileService);
        var chapter = _workspace.Projects.GetChaptersOrdered()
            .FirstOrDefault(c => string.Equals(c.Guid, chapterGuid, StringComparison.OrdinalIgnoreCase));
        var sceneIds = chapter == null
            ? (legacy?.Scenes.Keys.ToArray() ?? [])
            : _workspace.Projects.GetScenesForChapter(chapter.Guid).Select(s => s.Id).ToArray();

        var findings = new List<PeekFindingDto>();
        foreach (var sceneId in sceneIds)
        {
            var record = await store.ReadAsync(sceneId);
            IEnumerable<Sdk.Models.CachedAiFinding> sceneFindings =
                record?.Findings
                ?? (legacy != null && legacy.Scenes.TryGetValue(sceneId, out var legacyScene)
                    ? legacyScene.Findings
                    : []);

            foreach (var f in sceneFindings)
            {
                // "scene_stats" carries the per-scene POV/emotion numbers, not a
                // remark about an entity — the desktop card skipped it and so do we.
                if (string.Equals(f.Type, "scene_stats", StringComparison.Ordinal)) continue;
                if (!wanted.Contains(EntityResolveIndex.Normalize(f.EntityName))) continue;
                findings.Add(new PeekFindingDto(f.Type, f.Title, f.Description, f.Excerpt));
            }
        }

        return findings.Count == 0 ? peek : peek with { AiFindings = [.. findings] };
    }

    /// <summary>
    /// Resolves the character chapter/scene override that applies to the given
    /// editor context, matching the chapter by GUID or title. Scene-specific
    /// overrides win over chapter-wide ones. Ported from
    /// <c>FocusPeekExtension.ResolveCharacterOverride</c>.
    /// </summary>
    private static CharacterOverride? ResolveCharacterOverride(
        CharacterData character, string? chapterGuid, string? chapterTitle, string? sceneTitle)
    {
        if (string.IsNullOrWhiteSpace(chapterGuid) && string.IsNullOrWhiteSpace(chapterTitle))
            return null;

        bool ChapterMatches(CharacterOverride o) =>
            (!string.IsNullOrWhiteSpace(chapterGuid)
                && string.Equals(o.Chapter, chapterGuid, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(chapterTitle)
                && string.Equals(o.Chapter, chapterTitle, StringComparison.OrdinalIgnoreCase));

        var sceneMatch = character.ChapterOverrides.FirstOrDefault(o =>
            ChapterMatches(o)
            && !string.IsNullOrWhiteSpace(o.Scene)
            && string.Equals(o.Scene, sceneTitle, StringComparison.OrdinalIgnoreCase));
        if (sceneMatch != null)
            return sceneMatch;

        return character.ChapterOverrides.FirstOrDefault(o =>
            ChapterMatches(o) && string.IsNullOrWhiteSpace(o.Scene));
    }

    /// <summary>Builds the "Ch: X → Sc: Y" scope label for an applied override,
    /// preferring the friendly chapter title the client passed over the stored
    /// (often GUID) chapter key.</summary>
    private static string BuildOverrideScopeLabel(
        CharacterOverride ovr, string? chapterTitle)
    {
        var parts = new List<string>();
        var chapter = string.IsNullOrWhiteSpace(chapterTitle) ? ovr.Chapter : chapterTitle;
        if (!string.IsNullOrWhiteSpace(chapter)) parts.Add($"Ch: {chapter}");
        if (!string.IsNullOrWhiteSpace(ovr.Scene)) parts.Add($"Sc: {ovr.Scene}");
        return string.Join(" → ", parts);
    }

    private async Task<Dictionary<string, (string Id, string TypeKey)>> BuildResolveIndexAsync(
        IReadOnlyList<CharacterData> characters, IReadOnlyList<LocationData> locations,
        IReadOnlyList<ItemData> items, IReadOnlyList<LoreData> lore)
    {
        var customTypes = new List<(string TypeKey, IReadOnlyList<CustomEntityData> Entities)>();
        foreach (var typeDef in _entities.GetCustomEntityTypes())
            customTypes.Add((typeDef.TypeKey, await _entities.LoadCustomEntitiesAsync(typeDef.TypeKey)));

        return EntityResolveIndex.Build(characters, locations, items, lore, customTypes);
    }

    /// <summary>Indexes every map pin referencing <paramref name="entityId"/> so
    /// the peek card can list "PinLabel · MapName" links that jump to the pin.</summary>
    private async Task<PeekMapPinDto[]> GetMapPinsForEntityAsync(string entityId)
    {
        var result = new List<PeekMapPinDto>();
        var book = _workspace.Projects.ActiveBook;
        var service = new MapService(_workspace.Projects, _workspace.FileService);
        foreach (var mapRef in book?.Maps ?? Enumerable.Empty<MapReference>())
        {
            var map = await service.LoadMapAsync(mapRef.Id);
            if (map == null) continue;
            foreach (var pin in map.Pins)
            {
                if (!string.Equals(pin.EntityId, entityId, StringComparison.Ordinal)) continue;
                var mapName = string.IsNullOrWhiteSpace(mapRef.Name) ? map.Name : mapRef.Name;
                result.Add(new PeekMapPinDto(mapRef.Id, mapName, pin.Id, pin.Label ?? string.Empty));
            }
        }
        return result.ToArray();
    }

    private EntityPeekDto BuildCharacterPeek(
        CharacterData c, Dictionary<string, (string Id, string TypeKey)> resolve, PeekMapPinDto[] pins,
        string? chapterGuid = null, string? chapterTitle = null, string? sceneTitle = null)
    {
        var ovr = ResolveCharacterOverride(c, chapterGuid, chapterTitle, sceneTitle);

        // Each overridden non-blank field wins over the base value (desktop rule).
        string Pick(string? overridden, string @base) =>
            string.IsNullOrWhiteSpace(overridden) ? @base : overridden!;

        var name = Pick(ovr?.Name, c.Name);
        var surname = Pick(ovr?.Surname, c.Surname);
        var title = string.IsNullOrWhiteSpace(surname) ? name : $"{name} {surname}";
        var pills = new List<PeekPillDto>();
        AddPill(pills, Pick(ovr?.Role, c.Role), "#3B4466");
        AddPill(pills, Pick(ovr?.Gender, c.Gender), "#314355");
        AddLabelPill(pills, "focusPeek.agePill",
            ResolveCharacterAge(c, ovr, chapterGuid, sceneTitle), "#2E344D", dim: true);
        AddPill(pills, c.Group, "#2A3C38", dim: true);

        var relationships = ovr?.Relationships ?? c.Relationships;
        if (relationships.Count > 0)
            pills.Add(new PeekPillDto(relationships.Count.ToString(), null, null, true, "#2E344D", UsersIconPath));

        var appearance = new List<PeekPropDto>();
        AddProp(appearance, "focusPeek.eyes", Pick(ovr?.EyeColor, c.EyeColor));
        AddProp(appearance, "focusPeek.hair", Pick(ovr?.HairColor, c.HairColor));
        AddProp(appearance, "focusPeek.hairLength", Pick(ovr?.HairLength, c.HairLength));
        AddProp(appearance, "focusPeek.height", Pick(ovr?.Height, c.Height));
        AddProp(appearance, "focusPeek.build", Pick(ovr?.Build, c.Build));
        AddProp(appearance, "focusPeek.skin", Pick(ovr?.SkinTone, c.SkinTone));
        AddProp(appearance, "focusPeek.distinguishing", Pick(ovr?.DistinguishingFeatures, c.DistinguishingFeatures));

        // Overridden custom properties layer over the base set (blank-skipped by CustomProps).
        var customProperties = new Dictionary<string, string>(c.CustomProperties, StringComparer.OrdinalIgnoreCase);
        if (ovr?.CustomProperties != null)
            foreach (var pair in ovr.CustomProperties)
                customProperties[pair.Key] = pair.Value;

        // Override list semantics: null inherits the base list; a non-null list
        // (even empty) replaces it, mirroring the desktop editor's write-back.
        var images = ovr?.Images ?? c.Images;
        var sections = ovr?.Sections ?? c.Sections;
        var scopeLabel = ovr == null ? null : BuildOverrideScopeLabel(ovr, chapterTitle);

        return new EntityPeekDto(
            c.Id, "character", title, null, "#5B3F7A", string.Empty,
            ResolveImages(images), pills.ToArray(),
            appearance.ToArray(),
            CustomProps(customProperties),
            relationships.Select(r => BuildRelationship(r.Role, r.Target, resolve)).ToArray(),
            sections.Select(s => new EntitySectionDto(s.Title, s.Content)).ToArray(),
            pins, scopeLabel);
    }

    /// <summary>
    /// Resolves a character's displayed age. When <c>AgeMode == "date"</c> and a
    /// birth date is present, the age is computed from the birth date relative to
    /// the open scene's story date (else the chapter's date, else today) via
    /// <see cref="AgeComputation"/> — the <c>AgeIntervalUnit</c> (default Years)
    /// picks years/months/days. Otherwise the override age wins over the base age.
    /// Ported from <c>FocusPeekExtension.ResolveCharacterAge</c>.
    /// </summary>
    private string ResolveCharacterAge(
        CharacterData c, CharacterOverride? ovr, string? chapterGuid, string? sceneTitle)
    {
        if (string.Equals(c.AgeMode, "date", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(c.BirthDate))
        {
            var referenceDate = ResolveStoryReferenceDate(chapterGuid, sceneTitle);
            var computed = AgeComputation.ComputeAge(
                c.BirthDate, referenceDate, c.AgeIntervalUnit ?? IntervalUnit.Years);
            if (!string.IsNullOrWhiteSpace(computed))
                return computed;
        }

        return string.IsNullOrWhiteSpace(ovr?.Age) ? c.Age : ovr.Age!;
    }

    /// <summary>Resolves the story date to measure age against: the named scene's
    /// date, else the chapter's date, else null (→ today). Scenes are matched by
    /// title within the chapter, mirroring the peek's chapter/scene scope.</summary>
    private string? ResolveStoryReferenceDate(string? chapterGuid, string? sceneTitle)
    {
        if (string.IsNullOrWhiteSpace(chapterGuid))
            return null;

        var scenes = _workspace.Projects.ScenesManifest?.Chapters.GetValueOrDefault(chapterGuid);
        var scene = scenes?.FirstOrDefault(s =>
            !string.IsNullOrWhiteSpace(sceneTitle)
            && string.Equals(s.Title, sceneTitle, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(scene?.Date))
            return scene.Date;

        var chapter = _workspace.Projects.ActiveBook?.Chapters
            .FirstOrDefault(ch => string.Equals(ch.Guid, chapterGuid, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(chapter?.Date) ? null : chapter.Date;
    }

    private EntityPeekDto BuildLocationPeek(
        LocationData l, IReadOnlyList<LocationData> all, PeekMapPinDto[] pins)
    {
        var childCount = all.Count(other =>
            string.Equals(NormalizeReference(other.Parent), l.Name, StringComparison.OrdinalIgnoreCase));
        var pills = new List<PeekPillDto>();
        AddPill(pills, l.Type, "#314355");
        AddLabelPill(pills, "focusPeek.inPill",
            string.IsNullOrWhiteSpace(l.Parent) ? null : NormalizeReference(l.Parent), "#2E344D", dim: true);
        if (childCount > 0)
            pills.Add(new PeekPillDto(null, "focusPeek.sublocationsPill", childCount.ToString(), true, "#2E344D", null));

        return new EntityPeekDto(
            l.Id, "location", l.Name, null, "#355C7D", l.Description,
            ResolveImages(l.Images), pills.ToArray(), [], CustomProps(l.CustomProperties),
            [], l.Sections.Select(s => new EntitySectionDto(s.Title, s.Content)).ToArray(), pins);
    }

    private EntityPeekDto BuildItemPeek(ItemData i, PeekMapPinDto[] pins)
    {
        var pills = new List<PeekPillDto>();
        AddPill(pills, i.Type, "#5C4C2F");
        AddPill(pills, i.Origin, "#2E344D", dim: true);
        return new EntityPeekDto(
            i.Id, "item", i.Name, null, "#6A4D2F", i.Description,
            ResolveImages(i.Images), pills.ToArray(), [], CustomProps(i.CustomProperties),
            [], i.Sections.Select(s => new EntitySectionDto(s.Title, s.Content)).ToArray(), pins);
    }

    private EntityPeekDto BuildLorePeek(LoreData l, PeekMapPinDto[] pins)
    {
        var pills = new List<PeekPillDto>();
        AddPill(pills, l.Category, "#47506D");
        return new EntityPeekDto(
            l.Id, "lore", l.Name, null, "#4B5A73", l.Description,
            ResolveImages(l.Images), pills.ToArray(), [], CustomProps(l.CustomProperties),
            [], l.Sections.Select(s => new EntitySectionDto(s.Title, s.Content)).ToArray(), pins);
    }

    private EntityPeekDto BuildCustomPeek(
        CustomEntityData entity, string type,
        Dictionary<string, (string Id, string TypeKey)> resolve, PeekMapPinDto[] pins)
    {
        var typeDef = _entities.GetCustomEntityTypes()
            .FirstOrDefault(t => string.Equals(t.TypeKey, type, StringComparison.OrdinalIgnoreCase));
        var typeLabel = typeDef?.DisplayName ?? entity.EntityTypeKey;
        var fieldDefs = typeDef?.DefaultFields ?? [];

        var pills = new List<PeekPillDto>();
        if (entity.Relationships.Count > 0)
            pills.Add(new PeekPillDto(entity.Relationships.Count.ToString(), null, null, true, "#2E344D", UsersIconPath));

        var props = new List<PeekPropDto>();
        var entityRefRelationships = new List<PeekRelationshipDto>();
        foreach (var pair in entity.Fields)
        {
            if (string.IsNullOrWhiteSpace(pair.Value)) continue;
            var def = fieldDefs.FirstOrDefault(f => string.Equals(f.Key, pair.Key, StringComparison.OrdinalIgnoreCase));
            var label = def?.DisplayName ?? pair.Key;
            if (def?.Type == CustomPropertyType.EntityRef)
                entityRefRelationships.Add(BuildRelationship(label, pair.Value, resolve));
            else
                props.Add(new PeekPropDto(label, pair.Value));
        }
        foreach (var pair in entity.CustomProperties)
        {
            if (string.IsNullOrWhiteSpace(pair.Value)) continue;
            props.Add(new PeekPropDto(pair.Key, pair.Value));
        }

        var relationships = entity.Relationships
            .Select(r => BuildRelationship(r.Role, r.Target, resolve))
            .Concat(entityRefRelationships)
            .ToArray();

        return new EntityPeekDto(
            entity.Id, type, entity.Name, typeLabel, "#4A6A5A", string.Empty,
            ResolveImages(entity.Images), pills.ToArray(), [], props.ToArray(),
            relationships,
            entity.Sections.Select(s => new EntitySectionDto(s.Title, s.Content)).ToArray(), pins);
    }

    private PeekRelationshipDto BuildRelationship(
        string role, string target, Dictionary<string, (string Id, string TypeKey)> resolve)
    {
        var targets = target
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeReference)
            .Where(n => n.Length > 0)
            .Select(n => resolve.TryGetValue(n, out var hit)
                ? new PeekRelationshipTargetDto(n, hit.Id, hit.TypeKey)
                : new PeekRelationshipTargetDto(n, null, null))
            .ToArray();
        return new PeekRelationshipDto(role, targets);
    }

    private PeekImageDto[] ResolveImages(IReadOnlyList<EntityImage> images) =>
        images.Select(i => new PeekImageDto(i.Name, _entities.ResolveProjectRelativeImage(i.Path))).ToArray();

    private static PeekPropDto[] CustomProps(IReadOnlyDictionary<string, string> props) =>
        props.Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => new PeekPropDto(p.Key, p.Value)).ToArray();

    private static void AddPill(ICollection<PeekPillDto> target, string? text, string color, bool dim = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        target.Add(new PeekPillDto(text, null, null, dim, color, null));
    }

    private static void AddLabelPill(
        ICollection<PeekPillDto> target, string labelKey, string? arg, string color, bool dim)
    {
        if (string.IsNullOrWhiteSpace(arg)) return;
        target.Add(new PeekPillDto(null, labelKey, arg, dim, color, null));
    }

    private static void AddProp(ICollection<PeekPropDto> target, string keyLabel, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        target.Add(new PeekPropDto(keyLabel, value));
    }

    private static string NormalizeReference(string? value)
        => (value ?? string.Empty)
            .Replace("[[", string.Empty, StringComparison.Ordinal)
            .Replace("]]", string.Empty, StringComparison.Ordinal)
            .Trim();

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

    private void ApplyCustomEntityTemplate(CustomEntityData entity, string templateId)
    {
        var book = _workspace.Projects.ActiveBook;
        var template = book?.CustomEntityTemplates.FirstOrDefault(t =>
            t.Id == templateId && t.EntityTypeKey == entity.EntityTypeKey);
        if (template == null) return;

        entity.TemplateId = template.Id;
        foreach (var field in template.Fields)
        {
            if (!string.IsNullOrWhiteSpace(field.DefaultValue))
                entity.Fields[field.Key] = field.DefaultValue;
        }
        foreach (var def in template.CustomPropertyDefs)
        {
            if (!entity.CustomProperties.ContainsKey(def.Key))
                entity.CustomProperties[def.Key] = def.DefaultValue;
        }
        foreach (var section in template.Sections)
        {
            if (!entity.Sections.Any(s => string.Equals(s.Title, section.Title, StringComparison.OrdinalIgnoreCase)))
                entity.Sections.Add(new EntitySection { Title = section.Title, Content = section.DefaultContent });
        }
    }

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Applies a book template: known fields by name, custom-property
    /// defaults without overwriting, and section seeds - mirroring the
    /// Avalonia EntityPanelViewModel.Apply*Template behavior.</summary>
    private void ApplyTemplate(object entity, string type, string templateId)
    {
        var defs = ResolvePropertyDefs(type, templateId);
        entity.GetType().GetProperty("TemplateId")?.SetValue(entity, templateId);

        var book = _workspace.Projects.ActiveBook!;
        (List<TemplateField> Fields, List<TemplateSection> Sections) parts = type switch
        {
            "character" => Pick(book.CharacterTemplates.FirstOrDefault(t => t.Id == templateId)),
            "location" => Pick(book.LocationTemplates.FirstOrDefault(t => t.Id == templateId)),
            "item" => Pick(book.ItemTemplates.FirstOrDefault(t => t.Id == templateId)),
            _ => Pick(book.LoreTemplates.FirstOrDefault(t => t.Id == templateId))
        };

        foreach (var field in parts.Fields)
        {
            var property = entity.GetType().GetProperty(
                char.ToUpperInvariant(field.Key[0]) + field.Key[1..]);
            if (property?.CanWrite == true && property.PropertyType == typeof(string))
            {
                property.SetValue(entity, field.DefaultValue);
            }
        }

        var props = (Dictionary<string, string>)entity.GetType()
            .GetProperty("CustomProperties")!.GetValue(entity)!;
        foreach (var def in defs)
        {
            props.TryAdd(def.Key, def.DefaultValue);
        }

        var sections = (List<EntitySection>)entity.GetType()
            .GetProperty("Sections")!.GetValue(entity)!;
        foreach (var section in parts.Sections)
        {
            if (sections.All(s => s.Title != section.Title))
            {
                sections.Add(new EntitySection { Title = section.Title, Content = section.DefaultContent });
            }
        }
    }

    private static (List<TemplateField>, List<TemplateSection>) Pick(object? template) =>
        template == null
            ? ([], [])
            : (((dynamic)template).Fields, ((dynamic)template).Sections);

    private static InvalidOperationException Unknown(string id) => new($"Unknown entity '{id}'.");

    private static string Compose(string name, string surname) =>
        surname.Length == 0 ? name : $"{name} {surname}";

    private EntitySummaryDto Summary(
        string id, string name, string detail, bool isWorldBible, EntityImage? image,
        IReadOnlyList<string> aliases,
        string? group = null, string? gender = null, string? parent = null, string? firstName = null,
        EntityMatchSettings? match = null, bool isWorld = false, bool locked = false) =>
        new(id, name, detail, isWorldBible,
            image == null ? null : _entities.ResolveProjectRelativeImage(image.Path),
            aliases,
            NullIfEmpty(group), NullIfEmpty(gender), NullIfEmpty(parent),
            // The bare first name is an extra hover/mention target ("Liam" for
            // "Liam Calder"); null when it equals the composed display name.
            NullIfEmpty(firstName) is { } fn && !string.Equals(fn, name, StringComparison.Ordinal) ? fn : null,
            MatchDto(match, name, aliases, firstName),
            isWorld,
            locked);

    /// <summary>Projects the stored match settings, precomputing the plural forms
    /// of every matchable text so the client never has to know English plural
    /// rules. Null when nothing is customised, which keeps the common payload
    /// exactly the size it was.</summary>
    private static EntityMatchDto? MatchDto(
        EntityMatchSettings? match, string name, IReadOnlyList<string> aliases, string? firstName)
    {
        if (match == null) return null;
        if (!match.CaseSensitive && !match.MatchPlurals
            && match.Exclusions.Count == 0 && match.IgnoredSceneIds.Count == 0) return null;

        var plurals = new List<string>();
        foreach (var text in new[] { name, firstName }.Concat(aliases))
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

    /// <summary>
    /// Serializes an entity and annotates each image with a <c>url</c> field
    /// (project-root-relative) for display, leaving <c>path</c> (the stored
    /// value) intact so add/remove still match on it.
    /// </summary>
    private JsonElement WithResolvedImages(object entity)
    {
        var node = JsonSerializer.SerializeToNode(entity, JsonOptions);
        if (node is System.Text.Json.Nodes.JsonObject obj)
        {
            ResolveImageUrls(obj["images"] as System.Text.Json.Nodes.JsonArray);
            // Per-scope character overrides carry their own image lists; annotate
            // each so the inline overrides editor can render them by resolved url.
            if (obj["chapterOverrides"] is System.Text.Json.Nodes.JsonArray overrides)
            {
                foreach (var ovr in overrides)
                {
                    if (ovr is System.Text.Json.Nodes.JsonObject ovrObj)
                        ResolveImageUrls(ovrObj["images"] as System.Text.Json.Nodes.JsonArray);
                }
            }
        }
        return JsonSerializer.SerializeToElement(node, JsonOptions);
    }

    /// <summary>Annotates each image object in the array with a project-root-relative
    /// <c>url</c> for the novalist-project:// protocol, leaving <c>path</c> intact.</summary>
    private void ResolveImageUrls(System.Text.Json.Nodes.JsonArray? images)
    {
        if (images == null) return;
        foreach (var image in images)
        {
            if (image is System.Text.Json.Nodes.JsonObject imageObj
                && imageObj["path"]?.GetValue<string>() is { } path)
            {
                imageObj["url"] = _entities.ResolveProjectRelativeImage(path);
            }
        }
    }
}

public sealed record EntityTemplateDto(string Id, string Name);

public sealed record CustomTypeSpecDto(
    string? TypeKey,
    string DisplayName,
    string? DisplayNamePlural,
    CustomFieldSpecDto[]? Fields,
    bool IncludeImages,
    bool IncludeRelationships,
    bool IncludeSections);

public sealed record CustomFieldSpecDto(
    string? Key,
    string DisplayName,
    string Type,
    string? DefaultValue,
    string[]? EnumOptions,
    bool Required,
    /// <summary>A question saying what belongs in this field, shown under it on
    /// the entry. Optional so a caller written before it existed still works.</summary>
    string? Prompt = null);

public sealed record CustomPropDto(string Key, string Value, string PropType, IReadOnlyList<string> EnumOptions);

public sealed record EntitySectionDto(string Title, string Content);

/// <summary>A stored entity image (display name + project-relative path) as sent
/// by the inline overrides editor when replacing a scope's image list.</summary>
public sealed record EntityImageDto(string Name, string Path);

public sealed record RelationshipRowDto(string Role, string Target);

public sealed record RelationshipEditRowDto(
    string Role, string Target, string? InverseRole,
    /// <summary>What kind of tie it is, for the graph's colour. May be empty.</summary>
    string? Category = null);

public sealed record RelationshipSuggestionsDto(
    IReadOnlyList<string> CharacterNames,
    IReadOnlyList<string> Roles);

public sealed record EntitySummaryDto(
    string Id,
    string Name,
    string Detail,
    bool IsWorldBible,
    string? ImagePath,
    IReadOnlyList<string> Aliases,
    string? Group = null,
    string? Gender = null,
    string? Parent = null,
    string? FirstName = null,
    EntityMatchDto? Match = null,
    /// <summary>True for a place that is a world: drawn at the top of the tree,
    /// and never given a parent of its own.</summary>
    bool IsWorld = false,
    /// <summary>True when this entry is settled and the save path refuses it.</summary>
    bool Locked = false);

/// <summary>How this entry's name is recognised in prose. Rides along with the
/// summary so the editor can apply the rules without a second round trip per
/// entity. Null when the entry uses the defaults, which is the common case.</summary>
/// <summary>One time-scoped restatement of an entry.</summary>
public sealed record StateOverrideDto(
    string? Act,
    string? Chapter,
    string? Scene,
    string? Name,
    string? Description,
    Dictionary<string, string>? Fields,
    string? Note,
    /// <summary>From here the entry is out of the story: dead, departed,
    /// destroyed. Read by the continuity gates.</summary>
    bool Gone = false);

/// <summary>What an entry is like in one context. <c>IsOverridden</c> false
/// means the entry reads as itself and nothing else here is meaningful.</summary>
public sealed record ResolvedStateDto(
    string? Name,
    string? Description,
    Dictionary<string, string> Fields,
    string? Note,
    string ScopeLabel,
    bool IsOverridden);

/// <summary>An entry's AI-inclusion setting plus which of its sections are
/// withheld from a model.</summary>
public sealed record AiPolicyDto(string Inclusion, AiSectionDto[] Sections);

/// <summary>One section of an entry, and whether it is withheld. The index is
/// its position in the entry's section list, which is what the setter takes.</summary>
public sealed record AiSectionDto(int Index, string Title, bool Hidden);

public sealed record EntityMatchDto(
    bool CaseSensitive,
    bool MatchPlurals,
    IReadOnlyList<string> Exclusions,
    IReadOnlyList<string> IgnoredSceneIds,
    IReadOnlyList<string> Plurals);

/// <summary>Rich focus-peek card payload. <c>TypeKey</c> is the built-in key
/// (character/location/item/lore) or a custom type key; <c>CustomTypeLabel</c>
/// carries the display name for custom types (null for built-ins, which the
/// client localizes). Colors are hex strings straight from the desktop card.</summary>
public sealed record EntityPeekDto(
    string Id,
    string TypeKey,
    string Title,
    string? CustomTypeLabel,
    string BadgeColor,
    string Description,
    PeekImageDto[] Images,
    PeekPillDto[] Pills,
    PeekPropDto[] AppearanceProps,
    PeekPropDto[] CustomProps,
    PeekRelationshipDto[] Relationships,
    EntitySectionDto[] Sections,
    PeekMapPinDto[] MapPins,
    string? ScopeLabel = null,
    PeekFindingDto[]? AiFindings = null);

/// <summary>One cached AI analysis finding about this entity in the open chapter.
/// Read-only: the host never generates these, it only surfaces what an extension's
/// chapter analysis previously stored in the project. <see cref="Type"/> is the
/// finding kind ("reference", "inconsistency", "suggestion"), which the renderer
/// turns into a marker.</summary>
public sealed record PeekFindingDto(string Type, string Title, string Description, string Excerpt);

/// <summary>A single framed image; <c>Url</c> is project-root-relative for the
/// novalist-project:// protocol.</summary>
public sealed record PeekImageDto(string Name, string Url);

/// <summary>An attribute pill. Exactly one of <c>Text</c> (literal) or
/// <c>LabelKey</c>+<c>Arg</c> (client-localized template, e.g. "Age {0}") is set.
/// <c>Icon</c> is an SVG path geometry (the users glyph) or null.</summary>
public sealed record PeekPillDto(
    string? Text, string? LabelKey, string? Arg, bool Dim, string Color, string? Icon);

/// <summary>A key/value property. For appearance props the <c>Key</c> is a
/// localization key; for custom props it is a literal field label.</summary>
public sealed record PeekPropDto(string Key, string Value);

public sealed record PeekRelationshipDto(string Role, PeekRelationshipTargetDto[] Targets);

/// <summary><c>EntityId</c>/<c>TypeKey</c> are null when the name resolves to no
/// (or an ambiguous) entity — the client renders those as plain disabled text.</summary>
public sealed record PeekRelationshipTargetDto(string Name, string? EntityId, string? TypeKey);

public sealed record PeekMapPinDto(string MapId, string MapName, string PinId, string PinLabel);

/// <summary>A proposed Codex entry from an extension's entity extractor. Nothing
/// is written until the writer accepts it.</summary>
public sealed record EntityProposalDto(string TypeKey, string Name, string Detail);

/// <summary>The result of a scene scan: the proposals that survived filtering, or
/// a short error when the extractor failed.</summary>
public sealed record EntityProposalsDto(EntityProposalDto[] Proposals, string? Error);

public sealed record MatchSettingsDto(
    bool CaseSensitive, bool MatchPlurals, string[] Exclusions, string[] IgnoredSceneIds);

/// <summary>One earlier state of a Codex entry, for the history list.</summary>
public sealed record EntityRevisionDto(string Id, string SavedAt, long SizeBytes);

/// <summary>One section, and whether readers are kept from it.</summary>
public sealed record ReaderSectionDto(int Index, string Title, bool Hidden);

/// <summary>What a reader may see of one entry.</summary>
public sealed record ReaderPolicyDto(bool Hidden, IReadOnlyList<ReaderSectionDto> Sections);
