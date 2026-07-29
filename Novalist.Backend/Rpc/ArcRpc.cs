using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// A character's arc: where they start, where they end, and the scenes that
/// turn them.
/// </summary>
public sealed class ArcRpc
{
    private readonly Workspace _workspace;
    private readonly EntityService _entities;

    public ArcRpc(Workspace workspace)
    {
        _workspace = workspace;
        _entities = new EntityService(workspace.Projects);
    }

    [JsonRpcMethod("arcs/get")]
    public async Task<ArcDto> GetAsync(string characterId)
    {
        var character = await FindAsync(characterId);
        return ToDto(character.Arc ?? new CharacterArc());
    }

    [JsonRpcMethod("arcs/save")]
    public async Task<ArcDto> SaveAsync(
        string characterId, string? start, string? end, ArcPointDto[]? points)
    {
        var character = await FindAsync(characterId);
        var clean = (points ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p.Label))
            .Select(p => new ArcPoint
            {
                Id = string.IsNullOrWhiteSpace(p.Id) ? Guid.NewGuid().ToString() : p.Id,
                // A point can exist before the writer knows which scene it
                // happens in; that is half the use of writing it down.
                SceneId = p.SceneId ?? string.Empty,
                Label = p.Label!.Trim()
            })
            .ToList();

        var arc = new CharacterArc
        {
            Start = (start ?? string.Empty).Trim(),
            End = (end ?? string.Empty).Trim(),
            Points = clean
        };
        // An arc with nothing in it is no arc, rather than an empty object that
        // every reader has to check three fields of.
        character.Arc = arc.Start.Length == 0 && arc.End.Length == 0 && clean.Count == 0
            ? null
            : arc;
        await _entities.SaveCharacterAsync(character);
        return ToDto(character.Arc ?? new CharacterArc());
    }

    /// <summary>
    /// Every character with an arc, and their points placed in reading order,
    /// so a view can lay arcs against the book rather than against each other.
    /// </summary>
    [JsonRpcMethod("arcs/all")]
    public async Task<CharacterArcDto[]> AllAsync()
    {
        var position = new Dictionary<string, int>(StringComparer.Ordinal);
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var chapter in _workspace.Projects.GetChaptersOrdered())
            foreach (var scene in _workspace.Projects.GetScenesForChapter(chapter.Guid))
            {
                position[scene.Id] = index++;
                titles[scene.Id] = scene.Title;
            }

        return [.. (await _entities.LoadCharactersAsync())
            .Where(c => c.Arc != null)
            .Select(c => new CharacterArcDto(
                c.Id, c.Name, c.Arc!.Start, c.Arc.End,
                [.. c.Arc.Points
                    .Select(p => new ArcPointPlacedDto(
                        p.Id, p.SceneId, titles.GetValueOrDefault(p.SceneId, string.Empty),
                        p.Label, position.TryGetValue(p.SceneId, out var at) ? at : -1))
                    .OrderBy(p => p.ReadingIndex < 0 ? int.MaxValue : p.ReadingIndex)]))];
    }

    private async Task<CharacterData> FindAsync(string characterId)
        => (await _entities.LoadCharactersAsync()).FirstOrDefault(c => c.Id == characterId)
           ?? throw new InvalidOperationException($"Unknown character '{characterId}'.");

    private static ArcDto ToDto(CharacterArc arc) => new(
        arc.Start, arc.End,
        [.. arc.Points.Select(p => new ArcPointDto(p.Id, p.SceneId, p.Label))]);
}

public sealed record ArcPointDto(string? Id, string? SceneId, string? Label);

public sealed record ArcDto(string Start, string End, ArcPointDto[] Points);

/// <summary>An arc point with the scene it sits in resolved.</summary>
public sealed record ArcPointPlacedDto(
    string Id, string SceneId, string SceneTitle, string Label, int ReadingIndex);

public sealed record CharacterArcDto(
    string CharacterId, string Name, string Start, string End, ArcPointPlacedDto[] Points);
