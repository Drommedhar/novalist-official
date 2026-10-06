using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class EntitiesRpc
{
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
    /// <summary>
    /// A coined word, in the shape the peek card already draws.
    ///
    /// A dictionary the writer has to go and open is a dictionary they stop
    /// opening, and a language module whose words never appear while drafting
    /// is a list. Answering here rather than adding a second card means the
    /// hover, the pinning and the keyboard handling are the ones that already
    /// work.
    /// </summary>
    internal EntityPeekDto? PeekConlangWord(string id)
    {
        var hit = new Core.Services.ConlangService(_workspace.Projects)
            .GetAll()
            .SelectMany(language => language.Words.Select(word => (language, word)))
            .FirstOrDefault(x => x.word.Id == id);
        if (hit.word == null) return null;

        var pills = new List<PeekPillDto> { new(hit.language.Name, null, null, false, "accent", null) };
        if (!string.IsNullOrWhiteSpace(hit.word.PartOfSpeech))
            pills.Add(new PeekPillDto(hit.word.PartOfSpeech, null, null, true, "neutral", null));
        if (!string.IsNullOrWhiteSpace(hit.word.Pronunciation))
            pills.Add(new PeekPillDto(hit.word.Pronunciation, null, null, true, "neutral", null));

        return new EntityPeekDto(
            hit.word.Id, ConlangTypeKey, hit.word.Word, hit.language.Name, "accent",
            // The meaning is the description: it is what the writer wants when
            // they hover a word they coined three months ago.
            hit.word.Meaning ?? string.Empty,
            [], [.. pills], [], [], [], [], [],
            null, null);
    }

    /// <summary>The type key a coined word peeks under. Not an entity type: no
    /// Codex list holds these, and nothing should try to open one.</summary>
    internal const string ConlangTypeKey = "conlang";

    [JsonRpcMethod("entities/peek")]
    public async Task<EntityPeekDto> PeekAsync(
        string type, string id,
        string? chapterGuid = null, string? chapterTitle = null, string? sceneTitle = null)
    {
        if (string.Equals(type, ConlangTypeKey, StringComparison.OrdinalIgnoreCase))
        {
            return PeekConlangWord(id)
                ?? new EntityPeekDto(id, ConlangTypeKey, string.Empty, null, "accent",
                    string.Empty, [], [], [], [], [], [], [], null, null);
        }

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
                // The surname belongs here for the same reason the given name
                // does: it is one of the ways the prose refers to them.
                return [Compose(c.Name, c.Surname), c.Name, c.Surname, .. c.Aliases];
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
}
