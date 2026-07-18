using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Per-scene version history.</summary>
public sealed class SnapshotsRpc
{
    private readonly Workspace _workspace;

    public SnapshotsRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private SnapshotService Service => new(_workspace.Projects, _workspace.FileService);

    [JsonRpcMethod("snapshots/take")]
    public async Task<SnapshotDto[]> TakeAsync(string chapterGuid, string sceneId, string label)
    {
        var (chapter, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        await Service.TakeAsync(chapter, scene, label);
        return await ListAsync(chapterGuid, sceneId);
    }

    [JsonRpcMethod("snapshots/list")]
    public async Task<SnapshotDto[]> ListAsync(string chapterGuid, string sceneId)
    {
        var (_, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        var snapshots = await Service.ListAsync(scene);
        return snapshots
            .Select(s => new SnapshotDto(s.Id, s.Label, s.CreatedAt.ToString("o"), s.WordCount))
            .ToArray();
    }

    [JsonRpcMethod("snapshots/load")]
    public async Task<string?> LoadAsync(string chapterGuid, string sceneId, string snapshotId)
    {
        var (_, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        var snapshot = await Service.LoadAsync(scene, snapshotId);
        return snapshot?.Content;
    }

    [JsonRpcMethod("snapshots/restore")]
    public async Task<bool> RestoreAsync(string chapterGuid, string sceneId, string snapshotId)
    {
        var (chapter, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        return await Service.RestoreAsync(chapter, scene, snapshotId);
    }

    [JsonRpcMethod("snapshots/delete")]
    public async Task<SnapshotDto[]> DeleteAsync(string chapterGuid, string sceneId, string snapshotId)
    {
        var (_, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        await Service.DeleteAsync(scene, snapshotId);
        return await ListAsync(chapterGuid, sceneId);
    }
}

public sealed record SnapshotDto(string Id, string Label, string TakenAt, int WordCount);
