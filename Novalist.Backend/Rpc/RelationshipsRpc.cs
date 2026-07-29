using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Character relationship graph data (layout happens in the renderer).</summary>
public sealed class RelationshipsRpc
{
    private readonly EntityService _entities;

    public RelationshipsRpc(Workspace workspace)
    {
        _entities = new EntityService(workspace.Projects);
    }

    /// <summary>
    /// Every entry that can be tied to another, not only the characters. A
    /// graph that shows people and nothing else cannot answer "who holds this
    /// city" or "who has the sword", which is the same question about a
    /// different kind of node.
    /// </summary>
    [JsonRpcMethod("relationships/graph")]
    public async Task<RelationshipCharacterDto[]> GetGraphAsync()
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

        return [.. nodes];
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
    /// <summary>character, location, item, lore, or a custom type key.</summary>
    string EntityType);

/// <summary>
/// One tie. <c>Category</c> is what kind it is, and is empty on ties written
/// before edges could be typed - those draw in the neutral colour rather than
/// being guessed at.
/// </summary>
public sealed record RelationshipEdgeDto(string Role, string Target, string Category);
