using Novalist.Core.Services;
using Novalist.Core.Utilities;
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

    /// <summary>Line-level side-by-side diff between two snapshots' plain-text
    /// content. A missing snapshot id is treated as empty content.</summary>
    [JsonRpcMethod("snapshots/diff")]
    public async Task<SnapshotDiffRowDto[]> DiffAsync(
        string chapterGuid, string sceneId, string snapshotIdA, string snapshotIdB)
    {
        var (_, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        var service = Service;
        var a = await service.LoadAsync(scene, snapshotIdA);
        var b = await service.LoadAsync(scene, snapshotIdB);

        var left = TextDiff.StripHtml(a?.Content ?? string.Empty);
        var right = TextDiff.StripHtml(b?.Content ?? string.Empty);

        return TextDiff.ComputePaired(left, right)
            .Select(row => new SnapshotDiffRowDto(
                row.LeftText,
                row.RightText,
                row.IsEqual ? "equal"
                    : row.IsChanged ? "changed"
                    : row.IsLeftOnly ? "left"
                    : "right"))
            .ToArray();
    }
}

public sealed record SnapshotDto(string Id, string Label, string TakenAt, int WordCount);

public sealed record SnapshotDiffRowDto(string? Left, string? Right, string State);
