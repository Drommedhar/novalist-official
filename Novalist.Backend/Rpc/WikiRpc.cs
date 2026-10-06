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
public sealed partial class WikiRpc
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

        var core = BuildCore(type, id, new ArticleEntities(characters, locations, items, lore, customTypes), resolve);

        var rawAppearances = appearanceIndex.TryGetValue(id, out var raw) ? raw : [];
        var appearances = SortAppearances(rawAppearances);
        var character = core.Character;

        var stats = BuildStats(character, rawAppearances, appearances);
        // Entities already shown in this article's own Relationships — exclude them
        // from "referenced by" so it only surfaces links not already visible.
        var relatedIds = core.Relationships
            .SelectMany(r => r.Targets)
            .Select(t => t.EntityId)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
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

/// <summary>Research item whose explicit entity references include this article's entity id; prose matches alone do not create this link.</summary>
public sealed record WikiResearchDto(string Id, string Title, string Type);

/// <summary>Manual timeline event whose character or location names resolve to this entity; ambiguous names do not match.</summary>
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
