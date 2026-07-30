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

    /// <summary>
    /// Every snapshot in the book, newest first. The per-scene list is only
    /// reachable from the scene it belongs to, which is no help at all when
    /// the question is "how much is this project carrying".
    /// </summary>
    [JsonRpcMethod("snapshots/all")]
    public async Task<ProjectSnapshotDto[]> AllAsync()
        => [.. (await Service.ListAllAsync()).Select(row => new ProjectSnapshotDto(
            row.Snapshot.Id,
            row.Snapshot.Label,
            row.Snapshot.CreatedAt.ToString("o"),
            row.Snapshot.WordCount,
            row.ChapterGuid,
            row.ChapterTitle,
            row.SceneId,
            row.SceneTitle))];

    /// <summary>Renames one snapshot. A label is how a writer finds it again.</summary>
    [JsonRpcMethod("snapshots/rename")]
    public async Task<bool> RenameAsync(string chapterGuid, string sceneId, string snapshotId, string label)
    {
        var (_, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        return await Service.RenameAsync(scene, snapshotId, label);
    }

    /// <summary>
    /// Deletes snapshots past the newest few per scene, older than a cutoff,
    /// or left behind by deleted scenes. Returns how many went.
    /// </summary>
    [JsonRpcMethod("snapshots/prune")]
    public async Task<int> PruneAsync(int keepPerScene, int olderThanDays, bool dropOrphans)
        => await Service.PruneAsync(keepPerScene, olderThanDays, dropOrphans);

    /// <summary>
    /// Deletes every snapshot carrying an exact label, and returns how many
    /// went. One Replace All run labels all of its snapshots the same way, so
    /// this clears exactly that run and nothing else.
    /// </summary>
    [JsonRpcMethod("snapshots/deleteByLabel")]
    public async Task<int> DeleteByLabelAsync(string label)
        => await Service.DeleteByLabelAsync(label);

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

/// <summary>One snapshot in the project-wide list, with the scene it belongs to.</summary>
public sealed record ProjectSnapshotDto(
    string Id,
    string Label,
    string TakenAt,
    int WordCount,
    string ChapterGuid,
    string ChapterTitle,
    string SceneId,
    string SceneTitle);

public sealed record SnapshotDiffRowDto(string? Left, string? Right, string State);
