using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Splitting a scene in two and merging two into one.
///
/// Both return the fresh project state so the binder repaints without a
/// follow-up fetch - a split changes the order of everything after it, and a
/// merge removes a row.
/// </summary>
public sealed class SceneSplitRpc
{
    private readonly Workspace _workspace;

    public SceneSplitRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private SceneSplitService Service => new(_workspace.Projects);

    /// <summary>
    /// Splits a scene at the caret. The editor supplies both halves, because it
    /// is the only thing that knows where the caret is and how to divide the
    /// markup without leaving either side malformed.
    /// </summary>
    [JsonRpcMethod("sceneSplit/split")]
    public async Task<SplitResultDto> SplitAsync(
        string chapterGuid, string sceneId, string beforeHtml, string afterHtml, string? newTitle = null)
    {
        var created = await Service.SplitAsync(
            chapterGuid, sceneId, beforeHtml, afterHtml, newTitle ?? string.Empty);

        if (created == null) return new SplitResultDto(null, _workspace.BuildState());

        // The word counts of both halves have to catch up with what was written.
        await RecountAsync(chapterGuid, sceneId);
        await RecountAsync(chapterGuid, created.Id);
        return new SplitResultDto(created.Id, _workspace.BuildState());
    }

    /// <summary>Merges a scene into the one before or after it.</summary>
    [JsonRpcMethod("sceneSplit/merge")]
    public async Task<SplitResultDto> MergeAsync(
        string chapterGuid, string firstSceneId, string secondSceneId)
    {
        var merged = await Service.MergeAsync(chapterGuid, firstSceneId, secondSceneId);
        if (merged) await RecountAsync(chapterGuid, firstSceneId);
        return new SplitResultDto(merged ? firstSceneId : null, _workspace.BuildState());
    }

    /// <summary>Re-derives a scene's word count from what is on disk.</summary>
    private async Task RecountAsync(string chapterGuid, string sceneId)
    {
        var (chapter, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        var html = await _workspace.Projects.ReadSceneContentAsync(chapter, scene);
        scene.WordCount = Workspace.CountWords(Core.Utilities.TextDiff.StripHtml(html));
        await _workspace.Projects.SaveScenesAsync();
    }
}

/// <summary>The scene the operation produced or kept, plus the state to adopt.
/// A null id means nothing happened - the caret was at an edge, or a scene was
/// already gone.</summary>
public sealed record SplitResultDto(string? SceneId, ProjectStateDto State);
