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
        IReadOnlyList<LocationData> locations,
        IReadOnlyList<ItemData> items,
        IReadOnlyList<LoreData> lore,
        IReadOnlyList<(string TypeKey, IReadOnlyList<CustomEntityData> Entities)> customTypes,
        Dictionary<string, (string Id, string TypeKey)> resolve,
        IReadOnlySet<string> alreadyRelated)
    {
        var refs = new List<WikiReferenceDto>();

        // Every type that carries relationships contributes reverse links.
        void Scan(string entityId, string name, string typeKey, IReadOnlyList<EntityRelationship> rels)
        {
            // Skip self and entities already shown in this article's Relationships.
            if (entityId == id || alreadyRelated.Contains(entityId)) return;
            foreach (var rel in rels)
                if (TargetsInclude(rel.Target, id, resolve))
                    refs.Add(new WikiReferenceDto(name, entityId, typeKey, rel.Role));
        }

        foreach (var c in characters)
            Scan(c.Id, EntityResolveIndex.Compose(c.Name, c.Surname), "character", c.Relationships);
        foreach (var l in locations)
            Scan(l.Id, l.Name, "location", l.Relationships);
        foreach (var i in items)
            Scan(i.Id, i.Name, "item", i.Relationships);
        foreach (var l in lore)
            Scan(l.Id, l.Name, "lore", l.Relationships);

        foreach (var (typeKey, entities) in customTypes)
        {
            var fieldDefs = _entities.GetCustomEntityTypes()
                .FirstOrDefault(t => string.Equals(t.TypeKey, typeKey, StringComparison.OrdinalIgnoreCase))
                ?.DefaultFields ?? [];
            foreach (var e in entities)
            {
                if (e.Id == id || alreadyRelated.Contains(e.Id)) continue;
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

    /// <summary>The locations directly inside this one — the reverse of the
    /// "parent location" field, so a region's article lists its cities. Empty for
    /// every non-location entity.</summary>
    private static WikiLinkTargetDto[] BuildContains(
        string id, IReadOnlyList<LocationData> locations,
        Dictionary<string, (string Id, string TypeKey)> resolve)
        => locations
            .Where(l => l.Id != id)
            .Where(l =>
            {
                var parent = EntityResolveIndex.Normalize(l.Parent);
                return parent.Length > 0
                    && resolve.TryGetValue(parent, out var hit)
                    && hit.Id == id;
            })
            .OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(l => new WikiLinkTargetDto(l.Name, l.Id, "location"))
            .ToArray();

    /// <summary>
    /// Manual timeline events that name this entity among their characters or
    /// locations. The event list stores plain names, so each is resolved through
    /// the shared index — an ambiguous name matches nothing, exactly as elsewhere.
    /// Chronological, undated events last.
    /// </summary>
    private WikiEventDto[] BuildEvents(
        ArticleCore core, Dictionary<string, (string Id, string TypeKey)> resolve)
    {
        var events = _workspace.Projects.ProjectSettings?.Timeline?.ManualEvents;
        if (events == null || events.Count == 0)
            return [];

        bool Mentions(TimelineManualEvent e)
            => e.Characters.Concat(e.Locations)
                .Select(EntityResolveIndex.Normalize)
                .Any(n => n.Length > 0
                          && resolve.TryGetValue(n, out var hit)
                          && hit.Id == core.Id);

        return events
            .Where(Mentions)
            .Select(e => new
            {
                Event = e,
                Iso = StoryDateFormatter.ExtractLeadingDate(e.Date)
            })
            .OrderBy(x => x.Iso == null ? 1 : 0)
            .ThenBy(x => x.Iso, StringComparer.Ordinal)
            .ThenBy(x => x.Event.Order)
            .Select(x => new WikiEventDto(
                x.Event.Id, x.Event.Title, x.Event.Date, NullIfBlank(x.Event.Description)))
            .ToArray();
    }

    /// <summary>The research items the writer linked to this entity, so material
    /// they collected shows up where they are reading about it.</summary>
    private WikiResearchDto[] BuildResearch(string id)
        => _research.GetAll()
            .Where(r => r.EntityRefs.Contains(id, StringComparer.Ordinal))
            .OrderBy(r => r.Order)
            .Select(r => new WikiResearchDto(r.Id, r.Title, r.Type.ToString()))
            .ToArray();

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
}
