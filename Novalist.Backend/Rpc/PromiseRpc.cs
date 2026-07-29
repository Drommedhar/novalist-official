using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Setups and their payoffs, and what the book is still promising.
/// </summary>
public sealed class PromiseRpc
{
    private readonly Workspace _workspace;

    public PromiseRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private PromiseService Service => new(_workspace.Projects);

    /// <summary>Every promise in the book, in reading order, with its verdict.</summary>
    [JsonRpcMethod("promises/report")]
    public PromiseDto[] Report() =>
        [.. Service.Report().Select(r => new PromiseDto(
            r.SceneId, r.SceneTitle, r.ChapterGuid, r.ChapterTitle,
            r.PromiseId, r.Label, r.PayoffSceneId, r.PayoffSceneTitle, r.State.ToString()))];

    [JsonRpcMethod("promises/save")]
    public async Task<PromiseDto[]> SaveAsync(
        string sceneId, string? promiseId, string label, string? payoffSceneId)
    {
        await Service.SaveAsync(sceneId, promiseId, label, payoffSceneId);
        return Report();
    }

    [JsonRpcMethod("promises/delete")]
    public async Task<PromiseDto[]> DeleteAsync(string sceneId, string promiseId)
    {
        await Service.DeleteAsync(sceneId, promiseId);
        return Report();
    }
}

/// <summary>One promise and where it stands.</summary>
public sealed record PromiseDto(
    string SceneId,
    string SceneTitle,
    string ChapterGuid,
    string ChapterTitle,
    string PromiseId,
    string Label,
    string? PayoffSceneId,
    string? PayoffSceneTitle,
    string State);
