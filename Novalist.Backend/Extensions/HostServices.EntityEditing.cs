using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;
using Novalist.Sdk.Services;

namespace Novalist.Backend.Extensions;

public sealed partial class HostServices
{
    // ── Entity writing (IExtensionEntityService) ───────────────────

    async Task<bool> IExtensionEntityService.SaveEntityAsync(
        string typeKey,
        string entityId,
        string? name,
        string? description,
        IReadOnlyList<Sdk.Services.CustomEntitySectionInfo>? sections)
    {
        if (string.IsNullOrWhiteSpace(entityId)) return false;

        // Per type rather than through the shared interface: name, description
        // and sections are not on IEntityData, and a character's name is two
        // fields where every other kind has one.
        switch ((typeKey ?? string.Empty).ToLowerInvariant())
        {
            case "character":
                {
                    var entity = (await _entityService.LoadCharactersAsync())
                        .FirstOrDefault(e => e.Id == entityId);
                    if (entity == null) return false;
                    if (!string.IsNullOrWhiteSpace(name)) ApplyPersonName(entity, name);
                    // A character has no description field, so it becomes a Notes
                    // section - the same place CreateEntityAsync puts one, or the
                    // two calls would disagree about where a description lives.
                    entity.Sections = Merge(entity.Sections, WithDescription(sections, description));
                    await _entityService.SaveCharacterAsync(entity);
                    break;
                }
            case "location":
                {
                    var entity = (await _entityService.LoadLocationsAsync())
                        .FirstOrDefault(e => e.Id == entityId);
                    if (entity == null) return false;
                    if (!string.IsNullOrWhiteSpace(name)) entity.Name = name;
                    if (description != null) entity.Description = description;
                    entity.Sections = Merge(entity.Sections, sections);
                    await _entityService.SaveLocationAsync(entity);
                    break;
                }
            case "item":
                {
                    var entity = (await _entityService.LoadItemsAsync())
                        .FirstOrDefault(e => e.Id == entityId);
                    if (entity == null) return false;
                    if (!string.IsNullOrWhiteSpace(name)) entity.Name = name;
                    if (description != null) entity.Description = description;
                    entity.Sections = Merge(entity.Sections, sections);
                    await _entityService.SaveItemAsync(entity);
                    break;
                }
            case "lore":
                {
                    var entity = (await _entityService.LoadLoreAsync())
                        .FirstOrDefault(e => e.Id == entityId);
                    if (entity == null) return false;
                    if (!string.IsNullOrWhiteSpace(name)) entity.Name = name;
                    if (description != null) entity.Description = description;
                    entity.Sections = Merge(entity.Sections, sections);
                    await _entityService.SaveLoreAsync(entity);
                    break;
                }
            default:
                {
                    // An unregistered type throws inside the entity service. That is
                    // right for core, where the type list is known, and wrong across
                    // the SDK boundary: an extension passing a stale key should be
                    // told no, not handed an exception.
                    if (!_entityService.GetCustomEntityTypes()
                            .Any(t => string.Equals(t.TypeKey, typeKey, StringComparison.OrdinalIgnoreCase)))
                        return false;

                    var entity = (await _entityService.LoadCustomEntitiesAsync(typeKey ?? string.Empty))
                        .FirstOrDefault(e => e.Id == entityId);
                    if (entity == null) return false;
                    if (!string.IsNullOrWhiteSpace(name)) entity.Name = name;
                    // Custom entities have no description field either; their shape
                    // is whatever the type that registered them defined.
                    entity.Sections = Merge(entity.Sections, WithDescription(sections, description));
                    await _entityService.SaveCustomEntityAsync(entity);
                    break;
                }
        }

        EntityRefreshRequested?.Invoke();
        return true;
    }

    async Task<IReadOnlyList<string>> IExtensionEntityService.SetEntityFieldsAsync(
        string typeKey, string entityId, IReadOnlyDictionary<string, string> fields)
    {
        if (fields == null || fields.Count == 0) return [];

        var entity = await LoadAnyAsync(typeKey, entityId);
        // Nothing to write on means nothing was written: every name comes back
        // rather than a bare false, so the caller can report which.
        if (entity == null) return [.. fields.Keys];

        var rejected = new List<string>();
        foreach (var (key, value) in fields)
        {
            // A custom entity's fields are a dictionary the registering type
            // defined, so there is no property to look for.
            if (entity is CustomEntityData custom && !string.Equals(key, "name", StringComparison.OrdinalIgnoreCase))
            {
                custom.Fields[key] = value;
                continue;
            }

            var property = entity.GetType().GetProperty(
                key, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.IgnoreCase);
            // Only strings: a field taking a list or a date has its own call,
            // and coercing text into one here would fail at read time instead.
            if (property == null || !property.CanWrite || property.PropertyType != typeof(string))
            {
                rejected.Add(key);
                continue;
            }
            property.SetValue(entity, value ?? string.Empty);
        }

        await SaveAnyAsync(entity);
        EntityRefreshRequested?.Invoke();
        return rejected;
    }

    async Task<bool> IExtensionEntityService.SetEntityCustomPropertyAsync(
        string typeKey, string entityId, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var entity = await LoadAnyAsync(typeKey, entityId);
        if (entity == null) return false;

        var properties = entity.CustomProperties;
        if (value == null) properties.Remove(key);
        else properties[key] = value;

        await SaveAnyAsync(entity);
        EntityRefreshRequested?.Invoke();
        return true;
    }

    async Task<bool> IExtensionEntityService.SetEntityRelationshipsAsync(
        string typeKey, string entityId, IReadOnlyList<Sdk.Services.EntityRelationshipInfo> relationships)
    {
        var entity = await LoadAnyAsync(typeKey, entityId);
        if (entity == null) return false;

        var result = Core.Services.RelationshipWriter.Apply(
            entity,
            entity.DisplayName,
            [.. (relationships ?? []).Select(r => new Core.Services.RelationshipRow(
                r.Role, r.Target, r.Category, r.InverseRole))],
            await AllEntitiesAsync());

        await SaveAnyAsync(entity);
        // The far side can be any kind, so each one is saved as what it is
        // rather than as the type the call named.
        foreach (var target in result.Changed)
            await SaveAnyAsync(target);

        EntityRefreshRequested?.Invoke();
        return true;
    }

    /// <summary>An entry of any kind, by type key and id, or null.</summary>
    private async Task<IEntityData?> LoadAnyAsync(string typeKey, string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId)) return null;
        return (typeKey ?? string.Empty).ToLowerInvariant() switch
        {
            "character" => (await _entityService.LoadCharactersAsync()).FirstOrDefault(e => e.Id == entityId),
            "location" => (await _entityService.LoadLocationsAsync()).FirstOrDefault(e => e.Id == entityId),
            "item" => (await _entityService.LoadItemsAsync()).FirstOrDefault(e => e.Id == entityId),
            "lore" => (await _entityService.LoadLoreAsync()).FirstOrDefault(e => e.Id == entityId),
            _ => _entityService.GetCustomEntityTypes()
                    .Any(t => string.Equals(t.TypeKey, typeKey, StringComparison.OrdinalIgnoreCase))
                ? (await _entityService.LoadCustomEntitiesAsync(typeKey ?? string.Empty))
                    .FirstOrDefault(e => e.Id == entityId)
                : null
        };
    }

    private Task SaveAnyAsync(IEntityData entity) => entity switch
    {
        CharacterData c => _entityService.SaveCharacterAsync(c),
        LocationData l => _entityService.SaveLocationAsync(l),
        ItemData i => _entityService.SaveItemAsync(i),
        LoreData l => _entityService.SaveLoreAsync(l),
        // Nothing else implements IEntityData, so a custom entry is what is
        // left rather than an arm that can never run.
        _ => _entityService.SaveCustomEntityAsync((CustomEntityData)entity)
    };

    /// <summary>Every entry in the project, of every type.</summary>
    private async Task<List<IEntityData>> AllEntitiesAsync()
    {
        var all = new List<IEntityData>();
        all.AddRange(await _entityService.LoadCharactersAsync());
        all.AddRange(await _entityService.LoadLocationsAsync());
        all.AddRange(await _entityService.LoadItemsAsync());
        all.AddRange(await _entityService.LoadLoreAsync());
        foreach (var typeDef in _entityService.GetCustomEntityTypes())
            all.AddRange(await _entityService.LoadCustomEntitiesAsync(typeDef.TypeKey));
        return all;
    }

    /// <summary>
    /// Folds a description into the section list for the two kinds that have no
    /// description field of their own.
    /// </summary>
    private static IReadOnlyList<Sdk.Services.CustomEntitySectionInfo> WithDescription(
        IReadOnlyList<Sdk.Services.CustomEntitySectionInfo>? sections, string? description)
    {
        var all = new List<Sdk.Services.CustomEntitySectionInfo>(sections ?? []);
        if (!string.IsNullOrWhiteSpace(description))
            all.Add(new Sdk.Services.CustomEntitySectionInfo
            {
                Title = "Notes",
                Content = description
            });
        return all;
    }

    /// <summary>
    /// A section already there is replaced, anything else appended. Filling in
    /// one part of an entry must not wipe the parts the caller said nothing
    /// about - a questionnaire covering childhood should not erase appearance.
    /// </summary>
    private static List<EntitySection> Merge(
        List<EntitySection>? existing,
        IReadOnlyList<Sdk.Services.CustomEntitySectionInfo>? incoming)
    {
        var merged = existing ?? [];
        foreach (var section in incoming ?? [])
        {
            var match = merged.FirstOrDefault(
                s => string.Equals(s.Title, section.Title, StringComparison.OrdinalIgnoreCase));
            if (match != null) match.Content = section.Content;
            else merged.Add(new EntitySection { Title = section.Title, Content = section.Content });
        }
        return merged;
    }

    /// <summary>
    /// A character's name is two fields. Splitting on the last space is what the
    /// Codex does itself, so a name written back reads as it would if typed.
    /// </summary>
    private static void ApplyPersonName(CharacterData character, string name)
    {
        var trimmed = name.Trim();
        var split = trimmed.LastIndexOf(' ');
        if (split <= 0)
        {
            character.Name = trimmed;
            character.Surname = string.Empty;
            return;
        }
        character.Name = trimmed[..split];
        character.Surname = trimmed[(split + 1)..];
    }
}
