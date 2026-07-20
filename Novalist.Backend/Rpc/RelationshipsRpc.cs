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

    [JsonRpcMethod("relationships/graph")]
    public async Task<RelationshipCharacterDto[]> GetGraphAsync()
    {
        var characters = await _entities.LoadCharactersAsync();
        return characters
            .Select(c => new RelationshipCharacterDto(
                c.Id,
                c.Name,
                c.DisplayName,
                c.Surname,
                c.Group,
                c.Role,
                c.IsWorldBible,
                (c.Relationships ?? [])
                    .Select(r => new RelationshipEdgeDto(r.Role, r.Target))
                    .ToArray()))
            .ToArray();
    }
}

public sealed record RelationshipCharacterDto(
    string Id,
    string Name,
    string DisplayName,
    string Surname,
    string Group,
    string Role,
    bool IsWorldBible,
    IReadOnlyList<RelationshipEdgeDto> Relationships);

public sealed record RelationshipEdgeDto(string Role, string Target);
