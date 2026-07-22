using Novalist.Backend.Extensions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Read-only, Wikipedia-style projection of the Codex. <c>wiki/index</c> lists
/// every entity grouped by scope and type; <c>wiki/article</c> assembles a
/// single browsable page: a lead descriptor, an infobox and image gallery,
/// authored sections, resolved relationships, and — derived from the scenes that
/// mention the entity — a stats strip, "referenced by" and "appears with"
/// cross-links, map pins, plotlines, and an Appearances timeline. Purely
/// deterministic — no AI. All cross-links resolve through the shared
/// <see cref="EntityResolveIndex"/> so the Wiki and the Codex peek agree.
/// </summary>
public sealed class WikiRpc
{
    private const int MaxCoAppearances = 12;

    private readonly Workspace _workspace;
    private readonly EntityService _entities;

    public WikiRpc(Workspace workspace)
    {
        _workspace = workspace;
        _entities = new EntityService(workspace.Projects);
    }

    [JsonRpcMethod("wiki/index")]
    public async Task<WikiIndexDto> IndexAsync()
    {
        var characters = await _entities.LoadCharactersAsync();
        var locations = await _entities.LoadLocationsAsync();
        var items = await _entities.LoadItemsAsync();
        var lore = await _entities.LoadLoreAsync();

        // typeKey (in canonical order) -> entries, split into the two scopes.
        var typed = new List<(string TypeKey, string? CustomLabel, IEnumerable<WikiEntryDto> Entries)>
        {
            ("character", null, characters.Select(c => Entry(
                c.Id, "character", EntityResolveIndex.Compose(c.Name, c.Surname), c.Role,
                c.Images, c.IsWorldBible, c.Aliases))),
            ("location", null, locations.Select(l => Entry(
                l.Id, "location", l.Name, l.Type, l.Images, l.IsWorldBible, l.Aliases))),
            ("item", null, items.Select(i => Entry(
                i.Id, "item", i.Name, i.Type, i.Images, i.IsWorldBible, i.Aliases))),
            ("lore", null, lore.Select(l => Entry(
                l.Id, "lore", l.Name, l.Category, l.Images, l.IsWorldBible, l.Aliases)))
        };

        foreach (var typeDef in _entities.GetCustomEntityTypes())
        {
            var custom = await _entities.LoadCustomEntitiesAsync(typeDef.TypeKey);
            typed.Add((typeDef.TypeKey, typeDef.DisplayName, custom.Select(e => Entry(
                e.Id, typeDef.TypeKey, e.Name,
                e.Fields.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)),
                e.Images, e.IsWorldBible, e.Aliases))));
        }

        var scopes = new[] { false, true }
            .Select(isWb => new WikiScopeGroupDto(
                isWb,
                typed
                    .Select(t => new WikiTypeGroupDto(
                        t.TypeKey,
                        t.CustomLabel,
                        t.Entries
                            .Where(e => e.IsWorldBible == isWb)
                            .OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase)
                            .ToArray()))
                    .Where(g => g.Entries.Length > 0)
                    .ToArray()))
            .Where(s => s.Types.Length > 0)
            .ToArray();

        return new WikiIndexDto(scopes);
    }

    [JsonRpcMethod("wiki/article")]
    public async Task<WikiArticleDto> ArticleAsync(string type, string id)
    {
        var characters = await _entities.LoadCharactersAsync();
        var locations = await _entities.LoadLocationsAsync();
        var items = await _entities.LoadItemsAsync();
        var lore = await _entities.LoadLoreAsync();

        var customTypes = new List<(string TypeKey, IReadOnlyList<CustomEntityData> Entities)>();
        foreach (var typeDef in _entities.GetCustomEntityTypes())
            customTypes.Add((typeDef.TypeKey, await _entities.LoadCustomEntitiesAsync(typeDef.TypeKey)));

        var resolve = EntityResolveIndex.Build(characters, locations, items, lore, customTypes);
        var appearanceIndex = await new AppearanceIndexService(_workspace.Projects).BuildAsync(characters);
        var displayMap = BuildDisplayMap(characters, locations, items, lore, customTypes);

        var core = BuildCore(type, id, characters, locations, items, lore, customTypes, resolve);

        var rawAppearances = appearanceIndex.TryGetValue(id, out var raw) ? raw : [];
        var appearances = SortAppearances(rawAppearances);
        var character = core.Character;

        var stats = BuildStats(character, rawAppearances, appearances);
        var referencedBy = BuildReferencedBy(id, characters, customTypes, resolve);
        var appearsWith = BuildAppearsWith(id, rawAppearances, displayMap);
        var mapPins = await BuildMapPinsAsync(id);
        var plotlines = BuildPlotlines(rawAppearances);
        var overrides = BuildOverrides(character, resolve);

        Log.Info(
            $"wiki/article type={type} id={id} appearances={appearances.Length} " +
            $"refBy={referencedBy.Length} coApp={appearsWith.Length} pins={mapPins.Length} " +
            $"plots={plotlines.Length} overrides={overrides.Length}.");

        return new WikiArticleDto(
            core.Id, core.TypeKey, core.CustomTypeLabel, core.Title, core.IsWorldBible,
            core.Aliases, core.Lead, core.Description,
            core.Infobox, stats, core.Sections, core.Relationships,
            referencedBy, appearsWith, mapPins, plotlines, overrides, appearances);
    }

    // ── Type-specific core ──────────────────────────────────────────

    /// <summary>The per-type parts of an article; the shared derived sections
    /// (stats, cross-links, appearances) are assembled around it.</summary>
    private sealed record ArticleCore(
        string Id, string TypeKey, string? CustomTypeLabel, string Title, bool IsWorldBible,
        string[] Aliases, WikiLeadDto Lead, string? Description,
        WikiInfoboxDto Infobox, WikiSectionDto[] Sections, WikiRelationshipDto[] Relationships,
        CharacterData? Character);

    private ArticleCore BuildCore(
        string type, string id,
        IReadOnlyList<CharacterData> characters, IReadOnlyList<LocationData> locations,
        IReadOnlyList<ItemData> items, IReadOnlyList<LoreData> lore,
        IReadOnlyList<(string TypeKey, IReadOnlyList<CustomEntityData> Entities)> customTypes,
        Dictionary<string, (string Id, string TypeKey)> resolve)
    {
        var customPair = customTypes.FirstOrDefault(t =>
            string.Equals(t.TypeKey, type, StringComparison.OrdinalIgnoreCase));
        if (customPair.TypeKey != null)
        {
            var entity = customPair.Entities.FirstOrDefault(e => e.Id == id) ?? throw Unknown(id);
            return BuildCustomCore(entity, type, resolve);
        }

        return type switch
        {
            "character" => BuildCharacterCore(
                characters.FirstOrDefault(c => c.Id == id) ?? throw Unknown(id), resolve),
            "location" => BuildLocationCore(
                locations.FirstOrDefault(l => l.Id == id) ?? throw Unknown(id), resolve),
            "item" => BuildItemCore(items.FirstOrDefault(i => i.Id == id) ?? throw Unknown(id)),
            "lore" => BuildLoreCore(lore.FirstOrDefault(l => l.Id == id) ?? throw Unknown(id)),
            _ => throw new InvalidOperationException($"Unknown entity type '{type}'.")
        };
    }

    private ArticleCore BuildCharacterCore(
        CharacterData c, Dictionary<string, (string Id, string TypeKey)> resolve)
    {
        var fields = new List<WikiFieldDto>();
        AddField(fields, "entityEditor.surname", c.Surname);
        AddField(fields, "entityEditor.gender", c.Gender);
        AddField(fields, "entityEditor.age", c.Age);
        AddField(fields, "entityEditor.rolePlaceholder", c.Role);
        AddField(fields, "entityEditor.groupPlaceholder", c.Group);
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

        var lead = new WikiLeadDto(NullIfBlank(c.Role), NullIfBlank(c.Group), "dot");
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
            Infobox(l.Images, fields), Sections(l.Sections), [], null);
    }

    private ArticleCore BuildItemCore(ItemData i)
    {
        var fields = new List<WikiFieldDto>();
        AddField(fields, "entityEditor.itemType", i.Type);
        AddField(fields, "entityEditor.origin", i.Origin);
        AddCustomProps(fields, i.CustomProperties);

        var lead = new WikiLeadDto(NullIfBlank(i.Type), NullIfBlank(i.Origin), "from");
        return new ArticleCore(
            i.Id, "item", null, i.Name, i.IsWorldBible,
            i.Aliases.ToArray(), lead, NullIfBlank(i.Description),
            Infobox(i.Images, fields), Sections(i.Sections), [], null);
    }

    private ArticleCore BuildLoreCore(LoreData l)
    {
        var fields = new List<WikiFieldDto>();
        AddField(fields, "entityEditor.category", l.Category);
        AddCustomProps(fields, l.CustomProperties);

        var lead = new WikiLeadDto(NullIfBlank(l.Category), null, "");
        return new ArticleCore(
            l.Id, "lore", null, l.Name, l.IsWorldBible,
            l.Aliases.ToArray(), lead, NullIfBlank(l.Description),
            Infobox(l.Images, fields), Sections(l.Sections), [], null);
    }

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

    // ── Appearances + stats ─────────────────────────────────────────

    private static WikiAppearanceDto[] SortAppearances(IReadOnlyList<SceneAppearance> appearances)
        // Chronological by resolved date (undated last), then manuscript order.
        => appearances
            .OrderBy(a => a.IsoDate == null ? 1 : 0)
            .ThenBy(a => a.IsoDate, StringComparer.Ordinal)
            .ThenBy(a => a.ChapterOrder)
            .ThenBy(a => a.SceneOrder)
            .Select(a => new WikiAppearanceDto(
                a.ChapterGuid, a.SceneId, a.ChapterOrder, a.SceneOrder,
                a.ChapterTitle, a.SceneTitle, a.Synopsis, a.StoryDate, a.IsoDate))
            .ToArray();

    private static WikiStatsDto? BuildStats(
        CharacterData? character, IReadOnlyList<SceneAppearance> raw, WikiAppearanceDto[] sorted)
    {
        if (sorted.Length == 0)
            return null;

        var chapterCount = sorted.Select(a => a.ChapterGuid).Distinct(StringComparer.Ordinal).Count();
        int? povScenes = character != null ? CountPovScenes(character, raw) : null;
        return new WikiStatsDto(sorted.Length, chapterCount, povScenes, sorted[0], sorted[^1]);
    }

    private static int CountPovScenes(CharacterData c, IReadOnlyList<SceneAppearance> raw)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            EntityResolveIndex.Normalize(EntityResolveIndex.Compose(c.Name, c.Surname)),
            EntityResolveIndex.Normalize(c.Name)
        };
        foreach (var alias in c.Aliases)
            names.Add(EntityResolveIndex.Normalize(alias));
        names.Remove(string.Empty);
        return raw.Count(a => names.Contains(EntityResolveIndex.Normalize(a.Pov)));
    }

    // ── Cross-entity derivations ────────────────────────────────────

    private WikiReferenceDto[] BuildReferencedBy(
        string id,
        IReadOnlyList<CharacterData> characters,
        IReadOnlyList<(string TypeKey, IReadOnlyList<CustomEntityData> Entities)> customTypes,
        Dictionary<string, (string Id, string TypeKey)> resolve)
    {
        var refs = new List<WikiReferenceDto>();

        foreach (var c in characters)
        {
            if (c.Id == id) continue;
            foreach (var rel in c.Relationships)
                if (TargetsInclude(rel.Target, id, resolve))
                    refs.Add(new WikiReferenceDto(
                        EntityResolveIndex.Compose(c.Name, c.Surname), c.Id, "character", rel.Role));
        }

        foreach (var (typeKey, entities) in customTypes)
        {
            var fieldDefs = _entities.GetCustomEntityTypes()
                .FirstOrDefault(t => string.Equals(t.TypeKey, typeKey, StringComparison.OrdinalIgnoreCase))
                ?.DefaultFields ?? [];
            foreach (var e in entities)
            {
                if (e.Id == id) continue;
                foreach (var rel in e.Relationships)
                    if (TargetsInclude(rel.Target, id, resolve))
                        refs.Add(new WikiReferenceDto(e.Name, e.Id, typeKey, rel.Role));
                foreach (var pair in e.Fields)
                {
                    if (string.IsNullOrWhiteSpace(pair.Value)) continue;
                    var def = fieldDefs.FirstOrDefault(f =>
                        string.Equals(f.Key, pair.Key, StringComparison.OrdinalIgnoreCase));
                    if (def?.Type == CustomPropertyType.EntityRef && TargetsInclude(pair.Value, id, resolve))
                        refs.Add(new WikiReferenceDto(e.Name, e.Id, typeKey, def.DisplayName));
                }
            }
        }

        return refs.ToArray();
    }

    private static bool TargetsInclude(
        string target, string id, Dictionary<string, (string Id, string TypeKey)> resolve)
        => target
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(EntityResolveIndex.Normalize)
            .Any(n => n.Length > 0 && resolve.TryGetValue(n, out var hit) && hit.Id == id);

    private static WikiCoAppearanceDto[] BuildAppearsWith(
        string id, IReadOnlyList<SceneAppearance> raw,
        Dictionary<string, (string Title, string TypeKey)> display)
    {
        var tally = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var appearance in raw)
            foreach (var other in appearance.EntityIds)
            {
                if (string.Equals(other, id, StringComparison.Ordinal) || !display.ContainsKey(other))
                    continue;
                tally[other] = tally.GetValueOrDefault(other) + 1;
            }

        return tally
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => display[kv.Key].Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxCoAppearances)
            .Select(kv => new WikiCoAppearanceDto(
                display[kv.Key].Title, kv.Key, display[kv.Key].TypeKey, kv.Value))
            .ToArray();
    }

    private async Task<WikiMapPinDto[]> BuildMapPinsAsync(string id)
    {
        var result = new List<WikiMapPinDto>();
        var book = _workspace.Projects.ActiveBook;
        var service = new MapService(_workspace.Projects, _workspace.FileService);
        foreach (var mapRef in book?.Maps ?? Enumerable.Empty<MapReference>())
        {
            var map = await service.LoadMapAsync(mapRef.Id);
            if (map == null) continue;
            foreach (var pin in map.Pins)
            {
                if (!string.Equals(pin.EntityId, id, StringComparison.Ordinal)) continue;
                var mapName = string.IsNullOrWhiteSpace(mapRef.Name) ? map.Name : mapRef.Name;
                result.Add(new WikiMapPinDto(mapRef.Id, mapName, pin.Id, pin.Label));
            }
        }
        return result.ToArray();
    }

    private WikiPlotlineDto[] BuildPlotlines(IReadOnlyList<SceneAppearance> raw)
    {
        var ids = raw.SelectMany(a => a.PlotlineIds).ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0)
            return [];
        return (_workspace.Projects.ActiveBook?.Plotlines ?? [])
            .Where(p => ids.Contains(p.Id))
            .OrderBy(p => p.Order)
            .Select(p => new WikiPlotlineDto(p.Id, p.Name, p.Color))
            .ToArray();
    }

    /// <summary>Surfaces a character's per-act/chapter/scene overrides as a
    /// "changes over time" list: each scope and everything it changes from the
    /// base — scalar/custom fields, images, relationships, aliases, and the
    /// titles of overridden sections. Non-characters have none.</summary>
    private WikiOverrideDto[] BuildOverrides(
        CharacterData? c, Dictionary<string, (string Id, string TypeKey)> resolve)
    {
        if (c == null || c.ChapterOverrides.Count == 0)
            return [];

        var chapters = _workspace.Projects.GetChaptersOrdered()
            .ToDictionary(ch => ch.Guid, ch => (ch.Title, ch.Order), StringComparer.OrdinalIgnoreCase);

        var rows = new List<(int Order, string Scene, WikiOverrideDto Dto)>();
        foreach (var o in c.ChapterOverrides)
        {
            var changes = new List<WikiFieldDto>();
            AddField(changes, "entityEditor.name", o.Name);
            AddField(changes, "entityEditor.surname", o.Surname);
            AddField(changes, "entityEditor.gender", o.Gender);
            AddField(changes, "entityEditor.age", o.Age);
            AddField(changes, "entityEditor.rolePlaceholder", o.Role);
            AddField(changes, "entityEditor.eyeColor", o.EyeColor);
            AddField(changes, "entityEditor.hairColor", o.HairColor);
            AddField(changes, "entityEditor.hairLength", o.HairLength);
            AddField(changes, "entityEditor.height", o.Height);
            AddField(changes, "entityEditor.build", o.Build);
            AddField(changes, "entityEditor.skinTone", o.SkinTone);
            AddField(changes, "entityEditor.distinguishingFeatures", o.DistinguishingFeatures);
            if (o.CustomProperties != null)
                AddCustomProps(changes, o.CustomProperties);

            var images = (o.Images ?? [])
                .Select(img => new WikiImageDto(img.Name, _entities.ResolveProjectRelativeImage(img.Path)))
                .ToArray();
            var relationships = (o.Relationships ?? [])
                .Select(r => BuildRelationship(r.Role, r.Target, resolve))
                .ToArray();
            var aliases = (o.Aliases ?? [])
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a.Trim())
                .ToArray();
            var sectionTitles = (o.Sections ?? [])
                .Select(s => s.Title)
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .ToArray();

            if (changes.Count == 0 && images.Length == 0 && relationships.Length == 0 &&
                aliases.Length == 0 && sectionTitles.Length == 0)
                continue;

            var (scope, order) = ResolveOverrideScope(o, chapters);
            rows.Add((order, o.Scene ?? string.Empty,
                new WikiOverrideDto(scope, changes.ToArray(), images, relationships, aliases, sectionTitles)));
        }

        return rows
            .OrderBy(r => r.Order)
            .ThenBy(r => r.Scene, StringComparer.CurrentCultureIgnoreCase)
            .Select(r => r.Dto)
            .ToArray();
    }

    /// <summary>Composes a friendly "Act · Chapter · Scene" scope label (chapter
    /// GUIDs resolved to titles) and the manuscript order used to sort overrides.</summary>
    private static (string Scope, int Order) ResolveOverrideScope(
        CharacterOverride o, IReadOnlyDictionary<string, (string Title, int Order)> chapters)
    {
        var parts = new List<string>();
        var order = int.MaxValue;
        if (!string.IsNullOrEmpty(o.Act)) parts.Add(o.Act);
        if (!string.IsNullOrEmpty(o.Chapter))
        {
            if (chapters.TryGetValue(o.Chapter, out var ch))
            {
                parts.Add(ch.Title);
                order = ch.Order;
            }
            else
            {
                parts.Add(o.Chapter);
            }
        }
        if (!string.IsNullOrEmpty(o.Scene)) parts.Add(o.Scene);
        return (string.Join(" · ", parts), order);
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
    {
        var name = EntityResolveIndex.Normalize(parent);
        if (name.Length == 0) return;
        var link = resolve.TryGetValue(name, out var hit) ? hit : (Id: (string?)null, TypeKey: (string?)null);
        target.Add(new WikiFieldDto("entityEditor.parentLocation", null, name, link.Id, link.TypeKey));
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

public sealed record WikiIndexDto(WikiScopeGroupDto[] Scopes);

public sealed record WikiScopeGroupDto(bool IsWorldBible, WikiTypeGroupDto[] Types);

public sealed record WikiTypeGroupDto(string TypeKey, string? CustomTypeLabel, WikiEntryDto[] Entries);

public sealed record WikiEntryDto(
    string Id, string TypeKey, string Title, string? Subtitle,
    string? ImageUrl, bool IsWorldBible, string[] Aliases);

public sealed record WikiArticleDto(
    string Id, string TypeKey, string? CustomTypeLabel, string Title, bool IsWorldBible,
    string[] Aliases, WikiLeadDto Lead, string? Description,
    WikiInfoboxDto Infobox, WikiStatsDto? Stats, WikiSectionDto[] Sections,
    WikiRelationshipDto[] Relationships, WikiReferenceDto[] ReferencedBy,
    WikiCoAppearanceDto[] AppearsWith, WikiMapPinDto[] MapPins, WikiPlotlineDto[] Plotlines,
    WikiOverrideDto[] Overrides, WikiAppearanceDto[] Appearances);

/// <summary>A character's overridden fields for one act/chapter/scene scope —
/// the "as of chapter X" values shown as a change over time. Any of the parts
/// may be empty; the scope is included only when at least one is non-empty.</summary>
public sealed record WikiOverrideDto(
    string Scope, WikiFieldDto[] Changes, WikiImageDto[] Images,
    WikiRelationshipDto[] Relationships, string[] Aliases, string[] SectionTitles);

/// <summary>The lead descriptor phrase parts. The renderer composes a localized
/// one-liner: primary, optionally joined to secondary by the connector
/// ("dot" -> " · ", "in" -> "in {x}", "from" -> "from {x}", "" -> none).</summary>
public sealed record WikiLeadDto(string? Primary, string? Secondary, string SecondaryConnector);

public sealed record WikiStatsDto(
    int AppearanceCount, int ChapterCount, int? PovSceneCount,
    WikiAppearanceDto? First, WikiAppearanceDto? Last);

public sealed record WikiInfoboxDto(string? PrimaryImageUrl, WikiImageDto[] Images, WikiFieldDto[] Fields);

public sealed record WikiImageDto(string Name, string Url);

/// <summary>An infobox row. <see cref="LabelKey"/> is an i18n key for built-in
/// fields; <see cref="LiteralLabel"/> is a verbatim label for custom fields.
/// Exactly one is non-null. A non-null <see cref="LinkEntityId"/> makes the
/// value a cross-link to another article.</summary>
public sealed record WikiFieldDto(
    string? LabelKey, string? LiteralLabel, string Value, string? LinkEntityId, string? LinkTypeKey);

public sealed record WikiSectionDto(string Title, string Content);

public sealed record WikiRelationshipDto(string Role, WikiLinkTargetDto[] Targets);

/// <summary>A relationship/link target. A null <see cref="EntityId"/> means the
/// name did not resolve to a single entity, so it renders as plain text.</summary>
public sealed record WikiLinkTargetDto(string Name, string? EntityId, string? TypeKey);

/// <summary>An incoming reference: another entity whose relationship or entity-ref
/// field points at this one.</summary>
public sealed record WikiReferenceDto(string Name, string? EntityId, string? TypeKey, string Role);

/// <summary>An entity that co-occurs with this one, with the shared-scene count.</summary>
public sealed record WikiCoAppearanceDto(string Name, string EntityId, string TypeKey, int SharedScenes);

public sealed record WikiMapPinDto(string MapId, string MapName, string PinId, string PinLabel);

public sealed record WikiPlotlineDto(string Id, string Name, string Color);

public sealed record WikiAppearanceDto(
    string ChapterGuid, string SceneId, int ChapterOrder, int SceneOrder,
    string ChapterTitle, string SceneTitle, string? Synopsis, string StoryDate, string? IsoDate);
