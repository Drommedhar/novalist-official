using System.Text;
using Novalist.Backend.Extensions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using Novalist.Sdk.Hooks;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class WikiRpc
{
    // ── Type-specific core ──────────────────────────────────────────

    /// <summary>The per-type parts of an article; the shared derived sections
    /// (stats, cross-links, appearances) are assembled around it.</summary>
    private sealed record ArticleCore(
        string Id, string TypeKey, string? CustomTypeLabel, string Title, bool IsWorldBible,
        string[] Aliases, WikiLeadDto Lead, string? Description,
        WikiInfoboxDto Infobox, WikiSectionDto[] Sections, WikiRelationshipDto[] Relationships,
        CharacterData? Character);

    private sealed record ArticleEntities(
        IReadOnlyList<CharacterData> Characters, IReadOnlyList<LocationData> Locations,
        IReadOnlyList<ItemData> Items, IReadOnlyList<LoreData> Lore,
        IReadOnlyList<(string TypeKey, IReadOnlyList<CustomEntityData> Entities)> CustomTypes);

    private ArticleCore BuildCore(string type, string id, ArticleEntities entities,
        Dictionary<string, (string Id, string TypeKey)> resolve)
    {
        var customPair = entities.CustomTypes.FirstOrDefault(t =>
            string.Equals(t.TypeKey, type, StringComparison.OrdinalIgnoreCase));
        if (customPair.TypeKey != null)
        {
            var entity = customPair.Entities.FirstOrDefault(e => e.Id == id) ?? throw Unknown(id);
            return BuildCustomCore(entity, type, resolve);
        }

        return type switch
        {
            "character" => BuildCharacterCore(
                entities.Characters.FirstOrDefault(c => c.Id == id) ?? throw Unknown(id), resolve),
            "location" => BuildLocationCore(
                entities.Locations.FirstOrDefault(l => l.Id == id) ?? throw Unknown(id), resolve),
            "item" => BuildItemCore(entities.Items.FirstOrDefault(i => i.Id == id) ?? throw Unknown(id), resolve),
            "lore" => BuildLoreCore(entities.Lore.FirstOrDefault(l => l.Id == id) ?? throw Unknown(id), resolve),
            _ => throw new InvalidOperationException($"Unknown entity type '{type}'.")
        };
    }

    private ArticleCore BuildCharacterCore(
        CharacterData c, Dictionary<string, (string Id, string TypeKey)> resolve)
    {
        // The Wiki has no scene reference to compute an age against, so a character
        // kept as a birth date shows the date itself, labelled as such. Prefer the
        // structured BirthDate/AgeMode fields; older records stored the date in the
        // free-text Age field, so fall back to sniffing that.
        var hasStructuredBirthDate =
            string.Equals(c.AgeMode, "date", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(c.BirthDate);
        var ageIsBirthDate =
            hasStructuredBirthDate || StoryDateFormatter.ExtractLeadingDate(c.Age) != null;
        // A family/group name equal to the surname is redundant with it — omit it.
        var group = string.Equals(c.Group.Trim(), c.Surname.Trim(), StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : c.Group;

        var fields = new List<WikiFieldDto>();
        AddField(fields, "entityEditor.surname", c.Surname);
        AddField(fields, "entityEditor.gender", c.Gender);
        AddField(
            fields,
            ageIsBirthDate ? "entityEditor.birthDate" : "entityEditor.age",
            hasStructuredBirthDate ? c.BirthDate : c.Age);
        AddField(fields, "entityEditor.rolePlaceholder", c.Role);
        AddField(fields, "entityEditor.groupPlaceholder", group);
        AddField(fields, "entityEditor.eyeColor", c.EyeColor);
        AddField(fields, "entityEditor.hairColor", c.HairColor);
        AddField(fields, "entityEditor.hairLength", c.HairLength);
        AddField(fields, "entityEditor.height", c.Height);
        AddField(fields, "entityEditor.build", c.Build);
        AddField(fields, "entityEditor.skinTone", c.SkinTone);
        AddField(fields, "entityEditor.distinguishingFeatures", c.DistinguishingFeatures);
        AddCustomProps(fields, c.CustomProperties);

        var relationships = c.Relationships
            .Select(r => BuildRelationship(r.Role, r.Target, resolve))
            .ToArray();

        var lead = new WikiLeadDto(NullIfBlank(c.Role), NullIfBlank(group), "dot");
        return new ArticleCore(
            c.Id, "character", null, EntityResolveIndex.Compose(c.Name, c.Surname), c.IsWorldBible,
            c.Aliases.ToArray(), lead, null, Infobox(c.Images, fields), Sections(c.Sections), relationships, c);
    }

    private ArticleCore BuildLocationCore(
        LocationData l, Dictionary<string, (string Id, string TypeKey)> resolve)
    {
        var fields = new List<WikiFieldDto>();
        AddField(fields, "entityEditor.locationTypePlain", l.Type);
        AddParentField(fields, l.Parent, resolve);
        AddCustomProps(fields, l.CustomProperties);

        var lead = new WikiLeadDto(NullIfBlank(l.Type), NullIfBlank(EntityResolveIndex.Normalize(l.Parent)), "in");
        return new ArticleCore(
            l.Id, "location", null, l.Name, l.IsWorldBible,
            l.Aliases.ToArray(), lead, NullIfBlank(l.Description),
            Infobox(l.Images, fields), Sections(l.Sections),
            BuildRelationships(l.Relationships, resolve), null);
    }

    private ArticleCore BuildItemCore(
        ItemData i, Dictionary<string, (string Id, string TypeKey)> resolve)
    {
        var fields = new List<WikiFieldDto>();
        AddField(fields, "entityEditor.itemType", i.Type);
        // An origin naming a known place links to it, like a location's parent does.
        AddLinkedField(fields, "entityEditor.origin", i.Origin, resolve);
        AddCustomProps(fields, i.CustomProperties);

        var lead = new WikiLeadDto(NullIfBlank(i.Type), NullIfBlank(i.Origin), "from");
        return new ArticleCore(
            i.Id, "item", null, i.Name, i.IsWorldBible,
            i.Aliases.ToArray(), lead, NullIfBlank(i.Description),
            Infobox(i.Images, fields), Sections(i.Sections),
            BuildRelationships(i.Relationships, resolve), null);
    }

    private ArticleCore BuildLoreCore(
        LoreData l, Dictionary<string, (string Id, string TypeKey)> resolve)
    {
        var fields = new List<WikiFieldDto>();
        AddField(fields, "entityEditor.category", l.Category);
        AddCustomProps(fields, l.CustomProperties);

        var lead = new WikiLeadDto(NullIfBlank(l.Category), null, "");
        return new ArticleCore(
            l.Id, "lore", null, l.Name, l.IsWorldBible,
            l.Aliases.ToArray(), lead, NullIfBlank(l.Description),
            Infobox(l.Images, fields), Sections(l.Sections),
            BuildRelationships(l.Relationships, resolve), null);
    }

    private WikiRelationshipDto[] BuildRelationships(
        IReadOnlyList<EntityRelationship> relationships,
        Dictionary<string, (string Id, string TypeKey)> resolve)
        => relationships
            .Where(r => !string.IsNullOrWhiteSpace(r.Role) || !string.IsNullOrWhiteSpace(r.Target))
            .Select(r => BuildRelationship(r.Role, r.Target, resolve))
            .ToArray();

    private ArticleCore BuildCustomCore(
        CustomEntityData entity, string type, Dictionary<string, (string Id, string TypeKey)> resolve)
    {
        var typeDef = _entities.GetCustomEntityTypes()
            .FirstOrDefault(t => string.Equals(t.TypeKey, type, StringComparison.OrdinalIgnoreCase));
        var typeLabel = typeDef?.DisplayName ?? entity.EntityTypeKey;
        var fieldDefs = typeDef?.DefaultFields ?? [];

        var fields = new List<WikiFieldDto>();
        var refRelationships = new List<WikiRelationshipDto>();
        foreach (var pair in entity.Fields)
        {
            if (string.IsNullOrWhiteSpace(pair.Value)) continue;
            var def = fieldDefs.FirstOrDefault(f => string.Equals(f.Key, pair.Key, StringComparison.OrdinalIgnoreCase));
            var label = def?.DisplayName ?? pair.Key;
            if (def?.Type == CustomPropertyType.EntityRef)
                refRelationships.Add(BuildRelationship(label, pair.Value, resolve));
            else
                fields.Add(new WikiFieldDto(null, label, pair.Value, null, null));
        }
        AddCustomProps(fields, entity.CustomProperties);

        var relationships = entity.Relationships
            .Select(r => BuildRelationship(r.Role, r.Target, resolve))
            .Concat(refRelationships)
            .ToArray();

        var lead = new WikiLeadDto(typeLabel, null, "");
        return new ArticleCore(
            entity.Id, type, typeLabel, entity.Name, entity.IsWorldBible,
            entity.Aliases.ToArray(), lead, null,
            Infobox(entity.Images, fields), Sections(entity.Sections), relationships, null);
    }

    private Dictionary<string, (string Title, string TypeKey)> BuildDisplayMap(
        IReadOnlyList<CharacterData> characters, IReadOnlyList<LocationData> locations,
        IReadOnlyList<ItemData> items, IReadOnlyList<LoreData> lore,
        IReadOnlyList<(string TypeKey, IReadOnlyList<CustomEntityData> Entities)> customTypes)
    {
        var map = new Dictionary<string, (string Title, string TypeKey)>(StringComparer.Ordinal);
        foreach (var c in characters) map[c.Id] = (EntityResolveIndex.Compose(c.Name, c.Surname), "character");
        foreach (var l in locations) map[l.Id] = (l.Name, "location");
        foreach (var i in items) map[i.Id] = (i.Name, "item");
        foreach (var l in lore) map[l.Id] = (l.Name, "lore");
        foreach (var (typeKey, entities) in customTypes)
            foreach (var e in entities) map[e.Id] = (e.Name, typeKey);
        return map;
    }

    // ── Shared builders ─────────────────────────────────────────────

    private WikiRelationshipDto BuildRelationship(
        string role, string target, Dictionary<string, (string Id, string TypeKey)> resolve)
    {
        var targets = target
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(EntityResolveIndex.Normalize)
            .Where(n => n.Length > 0)
            .Select(n => resolve.TryGetValue(n, out var hit)
                ? new WikiLinkTargetDto(n, hit.Id, hit.TypeKey)
                : new WikiLinkTargetDto(n, null, null))
            .ToArray();
        return new WikiRelationshipDto(role, targets);
    }

    private WikiEntryDto Entry(
        string id, string typeKey, string title, string? subtitle,
        IReadOnlyList<EntityImage> images, bool isWorldBible, IReadOnlyList<string> aliases)
        => new(
            id, typeKey, title,
            NullIfBlank(subtitle),
            images.Count > 0 ? _entities.ResolveProjectRelativeImage(images[0].Path) : null,
            isWorldBible,
            aliases.ToArray());

    private WikiInfoboxDto Infobox(IReadOnlyList<EntityImage> images, IReadOnlyList<WikiFieldDto> fields)
    {
        var resolved = images
            .Select(i => new WikiImageDto(i.Name, _entities.ResolveProjectRelativeImage(i.Path)))
            .ToArray();
        return new WikiInfoboxDto(
            resolved.Length > 0 ? resolved[0].Url : null, resolved, fields.ToArray());
    }

    private static WikiSectionDto[] Sections(IReadOnlyList<EntitySection> sections)
        => sections
            .Where(s => !string.IsNullOrWhiteSpace(s.Title) || !string.IsNullOrWhiteSpace(s.Content))
            .Select(s => new WikiSectionDto(s.Title, s.Content))
            .ToArray();

    private static void AddField(ICollection<WikiFieldDto> target, string labelKey, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        target.Add(new WikiFieldDto(labelKey, null, value.Trim(), null, null));
    }

    private void AddParentField(
        ICollection<WikiFieldDto> target, string parent, Dictionary<string, (string Id, string TypeKey)> resolve)
        => AddLinkedField(target, "entityEditor.parentLocation", parent, resolve);

    /// <summary>Adds a field whose value cross-links when it names exactly one
    /// entity; an ambiguous or unknown name stays plain text.</summary>
    private static void AddLinkedField(
        ICollection<WikiFieldDto> target, string labelKey, string? value,
        Dictionary<string, (string Id, string TypeKey)> resolve)
    {
        var name = EntityResolveIndex.Normalize(value);
        if (name.Length == 0) return;
        var link = resolve.TryGetValue(name, out var hit) ? hit : (Id: (string?)null, TypeKey: (string?)null);
        target.Add(new WikiFieldDto(labelKey, null, name, link.Id, link.TypeKey));
    }

    private static void AddCustomProps(ICollection<WikiFieldDto> target, IReadOnlyDictionary<string, string> props)
    {
        foreach (var pair in props)
        {
            if (string.IsNullOrWhiteSpace(pair.Value)) continue;
            target.Add(new WikiFieldDto(null, pair.Key, pair.Value, null, null));
        }
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static InvalidOperationException Unknown(string id)
        => new($"Unknown entity '{id}'.");
}
