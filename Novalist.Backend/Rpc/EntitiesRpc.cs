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

    [JsonRpcMethod("entities/customTypes")]
    public CustomEntityTypeDefinition[] GetCustomTypes() =>
        _entities.GetCustomEntityTypes().ToArray();

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
                Required = f.Required
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
                    c.Images.FirstOrDefault()))
                .ToArray();
        }
        return type switch
        {
            "character" => (await _entities.LoadCharactersAsync())
                .Select(c => Summary(c.Id, Compose(c.Name, c.Surname), c.Role, c.IsWorldBible, c.Images.FirstOrDefault(), group: c.Group, gender: c.Gender))
                .ToArray(),
            "location" => (await _entities.LoadLocationsAsync())
                .Select(l => Summary(l.Id, l.Name, l.Description, l.IsWorldBible, l.Images.FirstOrDefault(), parent: l.Parent))
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

    [JsonRpcMethod("entities/update")]
    public async Task<JsonElement> UpdateAsync(string type, string id, Dictionary<string, string> fields)
    {
        if (IsCustomType(type))
        {
            var custom = (await _entities.LoadCustomEntitiesAsync(type)).FirstOrDefault(c => c.Id == id)
                ?? throw Unknown(id);
            foreach (var (key, value) in fields)
            {
                if (key == "name") custom.Name = value;
                else custom.Fields[key] = value;
            }
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
        return WithResolvedImages(entity);
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
        return WithResolvedImages(entity);
    }

    [JsonRpcMethod("entities/relationshipSuggestions")]
    public async Task<RelationshipSuggestionsDto> RelationshipSuggestionsAsync()
    {
        var characters = await _entities.LoadCharactersAsync();
        var names = characters.Select(c => Compose(c.Name, c.Surname))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var roles = characters.SelectMany(c => c.Relationships.Select(r => r.Role))
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
    [JsonRpcMethod("entities/setRelationships")]
    public async Task<JsonElement> SetRelationshipsAsync(string id, RelationshipEditRowDto[] rows)
    {
        var characters = await _entities.LoadCharactersAsync();
        var character = characters.FirstOrDefault(c => c.Id == id) ?? throw Unknown(id);
        var selfName = Compose(character.Name, character.Surname);

        character.Relationships = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Role) || !string.IsNullOrWhiteSpace(r.Target))
            .Select(r => new EntityRelationship { Role = r.Role.Trim(), Target = r.Target.Trim() })
            .ToList();
        await _entities.SaveCharacterAsync(character);

        var settingsChanged = false;
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Role) || string.IsNullOrWhiteSpace(row.Target)
                || string.IsNullOrWhiteSpace(row.InverseRole))
                continue;
            var target = characters.FirstOrDefault(c =>
                string.Equals(Compose(c.Name, c.Surname), row.Target.Trim(), StringComparison.OrdinalIgnoreCase));
            if (target == null || target.Id == character.Id) continue;

            var already = target.Relationships.Any(r =>
                string.Equals(r.Role, row.InverseRole.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.Target, selfName, StringComparison.OrdinalIgnoreCase));
            if (!already)
            {
                target.Relationships.Add(new EntityRelationship
                {
                    Role = row.InverseRole.Trim(),
                    Target = selfName
                });
                await _entities.SaveCharacterAsync(target);
            }
            settingsChanged |= _workspace.Settings.Settings.LearnRelationshipPair(row.Role.Trim(), row.InverseRole.Trim());
        }
        if (settingsChanged) await _workspace.Settings.SaveAsync();

        return WithResolvedImages(character);
    }

    [JsonRpcMethod("entities/setOverride")]
    public async Task<JsonElement> SetOverrideAsync(
        string characterId,
        string chapterGuid,
        string? sceneTitle,
        Dictionary<string, string> fields)
    {
        var character = (await _entities.LoadCharactersAsync()).FirstOrDefault(c => c.Id == characterId)
            ?? throw Unknown(characterId);
        var overrides = character.ChapterOverrides;
        var existing = overrides.FirstOrDefault(o =>
            o.Chapter == chapterGuid && (o.Scene ?? string.Empty) == (sceneTitle ?? string.Empty));
        if (existing == null)
        {
            existing = new CharacterOverride { Chapter = chapterGuid, Scene = sceneTitle };
            overrides.Add(existing);
        }
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
        string? group = null, string? gender = null, string? parent = null) =>
        new(id, name, detail, isWorldBible,
            image == null ? null : _entities.ResolveProjectRelativeImage(image.Path),
            NullIfEmpty(group), NullIfEmpty(gender), NullIfEmpty(parent));

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
        if (node is System.Text.Json.Nodes.JsonObject obj
            && obj["images"] is System.Text.Json.Nodes.JsonArray images)
        {
            foreach (var image in images)
            {
                if (image is System.Text.Json.Nodes.JsonObject imageObj
                    && imageObj["path"]?.GetValue<string>() is { } path)
                {
                    imageObj["url"] = _entities.ResolveProjectRelativeImage(path);
                }
            }
        }
        return JsonSerializer.SerializeToElement(node, JsonOptions);
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
    bool Required);

public sealed record CustomPropDto(string Key, string Value, string PropType, IReadOnlyList<string> EnumOptions);

public sealed record EntitySectionDto(string Title, string Content);

public sealed record RelationshipRowDto(string Role, string Target);

public sealed record RelationshipEditRowDto(string Role, string Target, string? InverseRole);

public sealed record RelationshipSuggestionsDto(
    IReadOnlyList<string> CharacterNames,
    IReadOnlyList<string> Roles);

public sealed record EntitySummaryDto(
    string Id,
    string Name,
    string Detail,
    bool IsWorldBible,
    string? ImagePath,
    string? Group = null,
    string? Gender = null,
    string? Parent = null);
