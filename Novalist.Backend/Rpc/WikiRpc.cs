using System.Text;
using Novalist.Backend.Extensions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using Novalist.Sdk.Hooks;
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
    private readonly ResearchService _research;
    private readonly WikiArticleCache _cache;

    public WikiRpc(Workspace workspace)
    {
        _workspace = workspace;
        _entities = new EntityService(workspace.Projects);
        _research = new ResearchService(workspace.Projects, workspace.FileService);
        _cache = new WikiArticleCache(workspace.Projects, workspace.FileService);
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
        // Entities already shown in this article's own Relationships — exclude them
        // from "referenced by" so it only surfaces links not already visible.
        var relatedIds = core.Relationships
            .SelectMany(r => r.Targets)
            .Select(t => t.EntityId)
            .Where(eid => eid != null)
            .ToHashSet(StringComparer.Ordinal)!;
        var referencedBy = BuildReferencedBy(
            id, characters, locations, items, lore, customTypes, resolve, relatedIds);
        var contains = BuildContains(id, locations, resolve);
        var research = BuildResearch(id);
        var events = BuildEvents(core, resolve);
        var appearsWith = BuildAppearsWith(id, rawAppearances, displayMap);
        var mapPins = await BuildMapPinsAsync(id);
        var plotlines = BuildPlotlines(rawAppearances);
        var overrides = BuildOverrides(character, resolve);

        // AI summary layer: whether a generator is available, plus any cached
        // summary flagged stale when the entity's data has changed since.
        var generatorAvailable = _workspace.ExtensionHostOrNull?.IsArticleGeneratorAvailable ?? false;
        var dossier = BuildDossier(core, appearances);
        var cached = await _cache.ReadAsync(id);
        var generated = cached == null
            ? null
            : new WikiGeneratedDto(
                cached.Summary,
                cached.InputHash != WikiArticleCache.ComputeInputHash(dossier),
                cached.GeneratedAt);

        Log.Info(
            $"wiki/article type={type} id={id} appearances={appearances.Length} " +
            $"refBy={referencedBy.Length} coApp={appearsWith.Length} pins={mapPins.Length} " +
            $"plots={plotlines.Length} overrides={overrides.Length} " +
            $"genAvail={generatorAvailable} cached={cached != null}.");

        // Cross-link entity references inside authored section prose for the
        // reader. The AI dossier deliberately keeps the raw (unlinked) sections.
        var linkedSections = core.Sections
            .Select(s => new WikiSectionDto(s.Title, WikiProseLinker.Linkify(s.Content, resolve, id)))
            .ToArray();

        return new WikiArticleDto(
            core.Id, core.TypeKey, core.CustomTypeLabel, core.Title, core.IsWorldBible,
            core.Aliases, core.Lead, core.Description,
            core.Infobox, stats, linkedSections, core.Relationships,
            referencedBy, contains, appearsWith, mapPins, plotlines, research, events,
            overrides, appearances, _workspace.Projects.ActiveBook?.Name ?? string.Empty,
            (_workspace.Projects.CurrentProject?.Books.Count ?? 0) > 1,
            generatorAvailable, generated);
    }

    [JsonRpcMethod("wiki/generatorAvailable")]
    public bool GeneratorAvailable()
        => _workspace.ExtensionHostOrNull?.IsArticleGeneratorAvailable ?? false;

    /// <summary>Generates (via the first enabled extension article generator) and
    /// caches an AI summary for the entity, returning it. Null when no generator
    /// is available; an error field when generation failed.</summary>
    [JsonRpcMethod("wiki/regenerate")]
    public async Task<WikiRegenerateResultDto?> RegenerateAsync(
        string type, string id, CancellationToken cancellationToken)
    {
        var host = _workspace.ExtensionHostOrNull;
        if (host == null || !host.IsArticleGeneratorAvailable)
            return null;

        var (core, _, dossier) = await BuildCoreAndDossierAsync(type, id);
        // Non-null: availability (an enabled generator) was just checked above.
        var result = (await host.GenerateArticleAsync(
            new ArticleGenerationRequest
            {
                TypeKey = core.TypeKey,
                EntityId = core.Id,
                EntityName = core.Title,
                Context = dossier
            },
            cancellationToken))!;

        if (!string.IsNullOrEmpty(result.Error))
            return new WikiRegenerateResultDto(null, result.Error, null);

        var generatedAt = DateTime.UtcNow.ToString("o");
        await _cache.WriteAsync(id, new WikiArticleCacheEntry
        {
            Summary = result.Summary,
            GeneratedAt = generatedAt,
            InputHash = WikiArticleCache.ComputeInputHash(dossier)
        });
        Log.Info($"wiki/regenerate type={type} id={id} len={result.Summary.Length}.");
        return new WikiRegenerateResultDto(result.Summary, null, generatedAt);
    }

    /// <summary>
    /// Writes one section of a Codex entry rather than the whole summary.
    ///
    /// The Wiki summary is regenerated whole or not at all, which is the wrong
    /// unit for the way an entry actually gets filled in: the writer is happy
    /// with the history and wants another go at the appearance. Sections have
    /// been ordered, titled blocks in the data model all along - the title is
    /// the writer's own words and the best statement there is of what belongs
    /// in it.
    ///
    /// Returns the prose without writing it. Generated text is wrong a fair
    /// amount of the time, and a section overwritten in place is found out
    /// about later, by which point the thing it replaced is gone.
    /// </summary>
    [JsonRpcMethod("entities/generateSection")]
    public async Task<WikiRegenerateResultDto?> GenerateSectionAsync(
        string type, string id, string sectionTitle, string currentContent,
        CancellationToken cancellationToken)
    {
        var host = _workspace.ExtensionHostOrNull;
        if (host == null || !host.IsArticleGeneratorAvailable) return null;
        if (string.IsNullOrWhiteSpace(sectionTitle)) return null;

        var (core, _, dossier) = await BuildCoreAndDossierAsync(type, id);
        var result = (await host.GenerateArticleAsync(
            new ArticleGenerationRequest
            {
                TypeKey = core.TypeKey,
                EntityId = core.Id,
                EntityName = core.Title,
                Context = dossier,
                SectionTitle = sectionTitle,
                SectionContent = currentContent ?? string.Empty,
            },
            cancellationToken))!;

        return string.IsNullOrEmpty(result.Error)
            ? new WikiRegenerateResultDto(result.Summary, null, null)
            : new WikiRegenerateResultDto(null, result.Error, null);
    }

    /// <summary>Loads and builds just what the AI generator needs: the entity
    /// core, its ordered appearances, and the plain-text dossier prompt context.</summary>
    private async Task<(ArticleCore Core, WikiAppearanceDto[] Appearances, string Dossier)>
        BuildCoreAndDossierAsync(string type, string id)
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
        var core = BuildCore(type, id, characters, locations, items, lore, customTypes, resolve);
        var appearances = SortAppearances(appearanceIndex.TryGetValue(id, out var raw) ? raw : []);
        return (core, appearances, BuildDossier(core, appearances));
    }

    /// <summary>Flattens the deterministic article into a plain-text dossier used
    /// as the AI generator's prompt context. Content-only; never logged.</summary>
    private static string BuildDossier(ArticleCore core, WikiAppearanceDto[] appearances)
    {
        var sb = new StringBuilder();
        // Spell out given vs. family name so the model refers to the subject
        // correctly and never mistakes a shared surname for a separate person.
        if (core.Character != null && core.Character.Surname.Length > 0)
            sb.AppendLine($"Name: {core.Title} (given name: {core.Character.Name}; family name: {core.Character.Surname})");
        else
            sb.AppendLine($"Name: {core.Title}");
        sb.AppendLine($"Type: {core.CustomTypeLabel ?? core.TypeKey}");
        if (core.Aliases.Length > 0)
            sb.AppendLine($"Also known as: {string.Join(", ", core.Aliases)}");
        if (core.Description != null)
            sb.AppendLine($"Description: {core.Description}");

        foreach (var f in core.Infobox.Fields)
            sb.AppendLine($"- {f.LiteralLabel ?? Humanize(f.LabelKey!)}: {f.Value}");

        foreach (var s in core.Sections)
        {
            var text = TextDiff.StripHtml(s.Content).Trim();
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(s.Title))
                sb.AppendLine($"## {s.Title}");
            if (text.Length > 0)
                sb.AppendLine(text);
        }

        if (core.Relationships.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Relationships:");
            foreach (var r in core.Relationships)
                sb.AppendLine($"- {r.Role}: {string.Join(", ", r.Targets.Select(t => t.Name))}");
        }

        if (appearances.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Appearances (in story order):");
            foreach (var a in appearances)
            {
                var date = a.StoryDate.Length > 0 ? $"{a.StoryDate} — " : string.Empty;
                var synopsis = a.Synopsis != null ? $": {a.Synopsis}" : string.Empty;
                sb.AppendLine($"- {date}{a.ChapterTitle} / {a.SceneTitle}{synopsis}");
            }
        }

        return sb.ToString();
    }

    /// <summary>Turns an i18n field key ("entityEditor.eyeColor") into a readable
    /// dossier label ("Eye Color"), dropping the UI-only "Placeholder"/"Plain"
    /// suffixes some keys carry (e.g. "rolePlaceholder" -> "Role").</summary>
    private static string Humanize(string labelKey)
    {
        var seg = labelKey.Contains('.') ? labelKey[(labelKey.LastIndexOf('.') + 1)..] : labelKey;
        if (seg.EndsWith("Placeholder", StringComparison.Ordinal))
            seg = seg[..^"Placeholder".Length];
        if (seg.EndsWith("Plain", StringComparison.Ordinal))
            seg = seg[..^"Plain".Length];
        var sb = new StringBuilder();
        foreach (var ch in seg)
        {
            if (char.IsUpper(ch) && sb.Length > 0) sb.Append(' ');
            sb.Append(ch);
        }
        var text = sb.ToString();
        return char.ToUpperInvariant(text[0]) + text[1..];
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
            "item" => BuildItemCore(items.FirstOrDefault(i => i.Id == id) ?? throw Unknown(id), resolve),
            "lore" => BuildLoreCore(lore.FirstOrDefault(l => l.Id == id) ?? throw Unknown(id), resolve),
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
    WikiLinkTargetDto[] Contains,
    WikiCoAppearanceDto[] AppearsWith, WikiMapPinDto[] MapPins, WikiPlotlineDto[] Plotlines,
    WikiResearchDto[] Research, WikiEventDto[] Events,
    WikiOverrideDto[] Overrides, WikiAppearanceDto[] Appearances,
    string BookName, bool MultipleBooks,
    bool GeneratorAvailable, WikiGeneratedDto? Generated);

/// <summary>A research item the writer linked to this entity.</summary>
public sealed record WikiResearchDto(string Id, string Title, string Type);

/// <summary>A manual timeline event that names this entity.</summary>
public sealed record WikiEventDto(string Id, string Title, string Date, string? Description);

/// <summary>A cached AI-generated summary shown at the top of an article.
/// <see cref="Stale"/> is true when the entity's data changed since it was made.</summary>
public sealed record WikiGeneratedDto(string Summary, bool Stale, string GeneratedAt);

/// <summary>Result of <c>wiki/regenerate</c>: the fresh summary, or an error.</summary>
public sealed record WikiRegenerateResultDto(string? Summary, string? Error, string? GeneratedAt);

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
