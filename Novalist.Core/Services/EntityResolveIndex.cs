using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>
/// Builds the map from a normalized entity reference (a name, bare first name,
/// or alias — <c>[[wiki-link]]</c> brackets stripped) to the single entity it
/// designates. Names that resolve to more than one entity are dropped, so an
/// ambiguous reference never navigates. Shared by the Codex peek card and the
/// Wiki view so both resolve cross-links identically.
/// </summary>
public static class EntityResolveIndex
{
    /// <summary>Strips wiki-link brackets and trims, mirroring the reference
    /// syntax used in relationship targets and section prose.</summary>
    public static string Normalize(string? value)
        => (value ?? string.Empty)
            .Replace("[[", string.Empty, StringComparison.Ordinal)
            .Replace("]]", string.Empty, StringComparison.Ordinal)
            .Trim();

    /// <summary>Composes a character's display name from first + surname.</summary>
    public static string Compose(string name, string surname)
        => surname.Length == 0 ? name : $"{name} {surname}";

    /// <summary>
    /// Builds the resolve map over already-loaded entity collections. Custom
    /// entities are supplied as (typeKey, entities) pairs so callers control
    /// loading. Keys are compared case-insensitively.
    /// </summary>
    public static Dictionary<string, (string Id, string TypeKey)> Build(
        IReadOnlyList<CharacterData> characters,
        IReadOnlyList<LocationData> locations,
        IReadOnlyList<ItemData> items,
        IReadOnlyList<LoreData> lore,
        IReadOnlyList<(string TypeKey, IReadOnlyList<CustomEntityData> Entities)> customTypes)
    {
        var candidates = new Dictionary<string, List<(string Id, string TypeKey)>>(StringComparer.OrdinalIgnoreCase);
        void Add(string? name, string entityId, string typeKey, EntityMatchSettings? match = null)
        {
            var key = Normalize(name);
            if (key.Length == 0) return;
            if (!candidates.TryGetValue(key, out var list))
            {
                list = [];
                candidates[key] = list;
            }
            list.Add((entityId, typeKey));

            // A plural is another way of writing the same reference, so it
            // becomes its own key rather than changing how the base name
            // matches. Case sensitivity and exclusions are applied where the
            // surrounding text is available, not here.
            if (match == null)
                return;

            foreach (var plural in match.PluralFormsOf(key))
                Add(plural, entityId, typeKey);
        }

        foreach (var c in characters)
        {
            var display = Compose(c.Name, c.Surname);
            Add(display, c.Id, "character", c.Match);
            // The bare first name is an extra target, but only when it differs
            // from the composed name — else a surnameless character would map its
            // one name twice and be wrongly treated as ambiguous (desktop parity).
            if (!string.Equals(c.Name, display, StringComparison.OrdinalIgnoreCase))
                Add(c.Name, c.Id, "character", c.Match);
            foreach (var alias in c.Aliases) Add(alias, c.Id, "character", c.Match);
        }
        foreach (var l in locations)
        {
            Add(l.Name, l.Id, "location", l.Match);
            foreach (var alias in l.Aliases) Add(alias, l.Id, "location", l.Match);
        }
        foreach (var i in items)
        {
            Add(i.Name, i.Id, "item", i.Match);
            foreach (var alias in i.Aliases) Add(alias, i.Id, "item", i.Match);
        }
        foreach (var l in lore)
        {
            Add(l.Name, l.Id, "lore", l.Match);
            foreach (var alias in l.Aliases) Add(alias, l.Id, "lore", l.Match);
        }
        foreach (var (typeKey, entities) in customTypes)
        {
            foreach (var entity in entities)
            {
                Add(entity.Name, entity.Id, typeKey, entity.Match);
                foreach (var alias in entity.Aliases) Add(alias, entity.Id, typeKey, entity.Match);
            }
        }

        return candidates
            .Where(pair => pair.Value.Count == 1)
            .ToDictionary(pair => pair.Key, pair => pair.Value[0], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Loads every entity (built-in + custom) from the service and
    /// builds the resolve map.</summary>
    public static async Task<Dictionary<string, (string Id, string TypeKey)>> BuildAsync(IEntityService entities)
    {
        var characters = await entities.LoadCharactersAsync();
        var locations = await entities.LoadLocationsAsync();
        var items = await entities.LoadItemsAsync();
        var lore = await entities.LoadLoreAsync();
        var customTypes = new List<(string TypeKey, IReadOnlyList<CustomEntityData> Entities)>();
        foreach (var typeDef in entities.GetCustomEntityTypes())
            customTypes.Add((typeDef.TypeKey, await entities.LoadCustomEntitiesAsync(typeDef.TypeKey)));

        return Build(characters, locations, items, lore, customTypes);
    }
}
