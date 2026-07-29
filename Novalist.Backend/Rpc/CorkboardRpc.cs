using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Where the writer put each card on the freeform corkboard.</summary>
public sealed class CorkboardRpc
{
    private readonly Workspace _workspace;

    public CorkboardRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private CorkboardService Service => new(_workspace.Projects);

    [JsonRpcMethod("corkboard/placements")]
    public CardPlacementDto[] Placements() =>
        [.. Service.Placements().Select(p => new CardPlacementDto(p.SceneId, p.X, p.Y))];

    [JsonRpcMethod("corkboard/setPosition")]
    public Task<bool> SetPositionAsync(string sceneId, int x, int y) =>
        Service.SetPositionAsync(sceneId, x, y);

    /// <summary>Forgets every placement, so the board falls back to reading order.</summary>
    [JsonRpcMethod("corkboard/reset")]
    public async Task<CardPlacementDto[]> ResetAsync()
    {
        await Service.ResetAsync();
        return Placements();
    }
}

public sealed record CardPlacementDto(string SceneId, int X, int Y);
