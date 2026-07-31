using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Character relationship graph data (layout happens in the renderer).</summary>
public sealed class RelationshipsRpc
{
    private readonly EntityService _entities;
    private readonly Workspace _workspace;

    public RelationshipsRpc(Workspace workspace)
    {
        _workspace = workspace;
        _entities = new EntityService(workspace.Projects);
    }

    /// <summary>
    /// Every entry that can be tied to another, not only the characters. A
    /// graph that shows people and nothing else cannot answer "who holds this
    /// city" or "who has the sword", which is the same question about a
    /// different kind of node.
    /// </summary>
    /// <param name="rootId">
    /// When given, only this entry and what is within <paramref name="depth"/>
    /// hops of it come back.
    ///
    /// A whole-world graph past a few dozen entries is a hairball: it proves the
    /// links exist and answers nothing. The question a writer actually has is
    /// "what is this one connected to", which is a neighbourhood.
    /// </param>
    /// <param name="depth">
    /// Hops from the root, 1-4. One is the immediate neighbours; two is usually
    /// where a family or a faction becomes visible as a shape.
    /// </param>
    /// <param name="includeScenes">
    /// Adds a node per scene with an edge to everything in it. Novalist already
    /// knew which entities appear in which scene and never drew that edge, so
    /// "where do these two actually meet" had no answer on the graph.
    /// </param>
    [JsonRpcMethod("relationships/graph")]
    public async Task<RelationshipCharacterDto[]> GetGraphAsync(
        string? rootId = null, int depth = 2, bool includeScenes = false)
    {
        var nodes = new List<RelationshipCharacterDto>();

        foreach (var c in await _entities.LoadCharactersAsync())
        {
            nodes.Add(new RelationshipCharacterDto(
                c.Id, c.Name, c.DisplayName, c.Surname, c.Group, c.Role, c.IsWorldBible,
                Edges(c.Relationships), "character"));
        }
        foreach (var l in await _entities.LoadLocationsAsync())
        {
            nodes.Add(new RelationshipCharacterDto(
                l.Id, l.Name, l.Name, string.Empty, l.Type, string.Empty, l.IsWorldBible,
                Edges(l.Relationships), "location"));
        }
        foreach (var i in await _entities.LoadItemsAsync())
        {
            nodes.Add(new RelationshipCharacterDto(
                i.Id, i.Name, i.Name, string.Empty, i.Type, string.Empty, i.IsWorldBible,
                Edges(i.Relationships), "item"));
        }
        foreach (var lo in await _entities.LoadLoreAsync())
        {
            nodes.Add(new RelationshipCharacterDto(
                lo.Id, lo.Name, lo.Name, string.Empty, lo.Category, string.Empty, lo.IsWorldBible,
                Edges(lo.Relationships), "lore"));
        }
        foreach (var type in _entities.GetCustomEntityTypes())
        {
            foreach (var e in await _entities.LoadCustomEntitiesAsync(type.TypeKey))
            {
                nodes.Add(new RelationshipCharacterDto(
                    e.Id, e.Name, e.Name, string.Empty, type.DisplayName, string.Empty,
                    e.IsWorldBible, Edges(e.Relationships), type.TypeKey));
            }
        }

        if (includeScenes) nodes.AddRange(await SceneNodesAsync());

        return string.IsNullOrWhiteSpace(rootId)
            ? [.. nodes]
            : [.. Neighbourhood(nodes, rootId!, Math.Clamp(depth, 1, 4))];
    }

    /// <summary>
    /// How everybody is related to one person, worked out from parentage.
    ///
    /// Novalist stores a tie as a role and a target - "mother", "Mira" - and
    /// draws the lines, and nothing could say what they add up to. A writer
    /// records who somebody's parents are; that they are therefore a great-aunt
    /// is arithmetic, and doing it by hand across a large cast is how family
    /// trees end up contradicting the prose.
    ///
    /// The parent map comes from the caller because deciding that "Mutter" is a
    /// parent is a question about language, which the interface knows and the
    /// backend does not. What comes back is shape rather than words, so the
    /// answer reads correctly in every language Novalist speaks.
    /// </summary>
    [JsonRpcMethod("relationships/kinship")]
    public KinshipDto[] Kinship(Dictionary<string, string[]> parents, string rootId)
    {
        if (string.IsNullOrWhiteSpace(rootId)) return [];

        var lookup = parents.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyCollection<string>)pair.Value,
            StringComparer.Ordinal);

        // Everyone the map mentions, on either side of a line - somebody who is
        // only ever named as a parent still has a place in the tree.
        var everyone = new HashSet<string>(parents.Keys, StringComparer.Ordinal);
        foreach (var above in parents.Values)
            foreach (var parent in above)
                if (!string.IsNullOrWhiteSpace(parent)) everyone.Add(parent);

        var results = new List<KinshipDto>();
        foreach (var id in everyone)
        {
            var kinship = Core.Services.Kinship.Describe(lookup, id, rootId);
            // The root and anybody off its family tree have nothing to say.
            // Filtered here rather than in the caller, so there is one rule.
            if (kinship.Kind is KinshipKind.Unrelated or KinshipKind.Self) continue;
            results.Add(new KinshipDto(id, kinship.Kind.ToString(), kinship.Degree, kinship.Removed));
        }
        return [.. results];
    }

    /// <summary>
    /// A node per scene, edged to everything the scene contains.
    ///
    /// The link layer has been there all along - assigned casts and confirmed
    /// mentions - and nothing ever drew it, so the graph could show that two
    /// characters are siblings but not that they are in nine scenes together.
    /// </summary>
    private async Task<List<RelationshipCharacterDto>> SceneNodesAsync()
    {
        var characters = await _entities.LoadCharactersAsync();
        var index = await new AppearanceIndexService(_workspace.Projects).BuildAsync(characters);

        // The index is keyed by entity; the graph wants it keyed by scene.
        var byScene = new Dictionary<string, (SceneAppearance Scene, List<string> Names)>(
            StringComparer.Ordinal);
        var names = await NamesByIdAsync();

        foreach (var (entityId, appearances) in index)
        {
            if (!names.TryGetValue(entityId, out var name)) continue;
            foreach (var appearance in appearances)
            {
                if (!byScene.TryGetValue(appearance.SceneId, out var entry))
                    byScene[appearance.SceneId] = entry = (appearance, []);
                entry.Names.Add(name);
            }
        }

        return [.. byScene.Values.Select(entry => new RelationshipCharacterDto(
            entry.Scene.SceneId,
            entry.Scene.SceneTitle,
            entry.Scene.SceneTitle,
            string.Empty,
            entry.Scene.ChapterTitle,
            string.Empty,
            false,
            [.. entry.Names.Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(n => new RelationshipEdgeDto("in scene", n, string.Empty))],
            "scene",
            entry.Scene.ChapterGuid))];
    }

    /// <summary>Display name of every Codex entry, by id.</summary>
    private async Task<Dictionary<string, string>> NamesByIdAsync()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in await _entities.LoadCharactersAsync()) names[c.Id] = c.DisplayName;
        foreach (var l in await _entities.LoadLocationsAsync()) names[l.Id] = l.Name;
        foreach (var i in await _entities.LoadItemsAsync()) names[i.Id] = i.Name;
        foreach (var lo in await _entities.LoadLoreAsync()) names[lo.Id] = lo.Name;
        foreach (var type in _entities.GetCustomEntityTypes())
            foreach (var e in await _entities.LoadCustomEntitiesAsync(type.TypeKey))
                names[e.Id] = e.Name;
        return names;
    }

    /// <summary>
    /// The root and everything within <paramref name="depth"/> hops of it.
    ///
    /// Edges name their target by display name rather than by id - that is how
    /// relationships have always been stored - so the walk resolves names back
    /// to nodes, and an edge naming something that is not in the Codex simply
    /// leads nowhere, which is what it does on the canvas too.
    /// </summary>
    internal static List<RelationshipCharacterDto> Neighbourhood(
        List<RelationshipCharacterDto> nodes, string rootId, int depth)
    {
        var byId = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        if (!byId.ContainsKey(rootId)) return [];

        var byName = new Dictionary<string, List<RelationshipCharacterDto>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.DisplayName)) continue;
            if (!byName.TryGetValue(node.DisplayName, out var list))
                byName[node.DisplayName] = list = [];
            list.Add(node);
        }

        // Edges run both ways for reachability: a node nothing points at but
        // which points at the root is still one hop from it.
        var pointsAt = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void Connect(string from, string to)
        {
            if (!pointsAt.TryGetValue(from, out var list)) pointsAt[from] = list = [];
            if (!list.Contains(to, StringComparer.Ordinal)) list.Add(to);
        }

        foreach (var node in nodes)
            foreach (var edge in node.Relationships)
                if (byName.TryGetValue(edge.Target ?? string.Empty, out var targets))
                    foreach (var target in targets)
                    {
                        Connect(node.Id, target.Id);
                        Connect(target.Id, node.Id);
                    }

        var reached = new HashSet<string>(StringComparer.Ordinal) { rootId };
        var frontier = new List<string> { rootId };
        for (var hop = 0; hop < depth && frontier.Count > 0; hop++)
        {
            var next = new List<string>();
            foreach (var id in frontier)
                foreach (var neighbour in pointsAt.GetValueOrDefault(id) ?? [])
                    if (reached.Add(neighbour)) next.Add(neighbour);
            frontier = next;
        }

        return [.. nodes.Where(n => reached.Contains(n.Id))];
    }

    private static RelationshipEdgeDto[] Edges(List<EntityRelationship>? relationships)
        => [.. (relationships ?? []).Select(r => new RelationshipEdgeDto(r.Role, r.Target, r.Category))];
}

/// <summary>
/// One node in the graph. Named for characters because that is all it used to
/// hold; <c>EntityType</c> says what it actually is.
/// </summary>
public sealed record RelationshipCharacterDto(
    string Id,
    string Name,
    string DisplayName,
    string Surname,
    string Group,
    string Role,
    bool IsWorldBible,
    IReadOnlyList<RelationshipEdgeDto> Relationships,
    /// <summary>character, location, item, lore, "scene", or a custom type key.</summary>
    string EntityType,
    /// <summary>For a scene node, the chapter it is in, so clicking through
    /// opens it. Null for every other kind, which has no chapter.</summary>
    string? ChapterGuid = null);

/// <summary>
/// One tie. <c>Category</c> is what kind it is, and is empty on ties written
/// before edges could be typed - those draw in the neutral colour rather than
/// being guessed at.
/// </summary>
public sealed record RelationshipEdgeDto(string Role, string Target, string Category);

/// <summary>
/// One person's relationship to the one the view is centred on. Shape rather
/// than words: "Cousin, degree 2, removed 1" reads as "second cousin once
/// removed" in English and correctly in every other language too.
/// </summary>
public sealed record KinshipDto(string EntityId, string Kind, int Degree, int Removed);
