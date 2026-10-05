using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class EntitiesRpc
{
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

    private async Task<(IEntityData Entity, string? TemplateId)> LoadWithTemplateAsync(string type, string id)
    {
        var entity = await FindEntityAsync(type, id) ?? throw Unknown(id);
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
            "lore" => book.LoreTemplates.FirstOrDefault(t => t.Id == templateId)?.CustomPropertyDefs ?? [],
            _ => book.CustomEntityTemplates
                .FirstOrDefault(t => t.Id == templateId && t.EntityTypeKey == type)?.CustomPropertyDefs ?? []
        };
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
            else
                entity.Fields.TryAdd(field.Key, field.DefaultValue);
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

        var props = (Dictionary<string, string>)entity.GetType()
            .GetProperty("CustomProperties")!.GetValue(entity)!;
        foreach (var field in parts.Fields)
        {
            var property = entity.GetType().GetProperty(
                char.ToUpperInvariant(field.Key[0]) + field.Key[1..]);
            if (property?.CanWrite == true && property.PropertyType == typeof(string))
            {
                property.SetValue(entity, field.DefaultValue);
            }
            else if (property == null)
            {
                // Template-authored fields use the same editable property bag
                // as fields added on the entry, including empty defaults.
                props.TryAdd(field.Key, field.DefaultValue);
            }
        }

        if (entity is CharacterData character &&
            book.CharacterTemplates.FirstOrDefault(t => t.Id == templateId) is { AgeMode: "date" } ageTemplate)
        {
            character.AgeMode = "date";
            character.AgeIntervalUnit = ageTemplate.AgeIntervalUnit ?? IntervalUnit.Years;
            character.BirthDate = character.Age;
            character.Age = string.Empty;
        }

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
}
