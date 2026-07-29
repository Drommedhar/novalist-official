using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Scene stages: the writer's own revision states, and where the book
/// stands across them.</summary>
public sealed class SceneStageRpc
{
    private readonly Workspace _workspace;

    public SceneStageRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private SceneStageService Service => new(_workspace.Projects);

    [JsonRpcMethod("stages/list")]
    public SceneStageDto[] List()
        => [.. Service.Stages().Select(ToDto)];

    [JsonRpcMethod("stages/set")]
    public async Task<SceneStageDto[]> SetAsync(SceneStageDto[] stages)
    {
        var saved = await Service.SetStagesAsync(stages.Select(s => new SceneStage
        {
            Key = s.Key,
            Label = s.Label,
            Color = s.Color,
            CountsAsWritten = s.CountsAsWritten
        }));
        return [.. saved.Select(ToDto)];
    }

    /// <summary>Sets one scene's stage and returns the fresh project state, so
    /// the binder repaints without a follow-up fetch.</summary>
    [JsonRpcMethod("stages/setSceneStage")]
    public async Task<ProjectStateDto> SetSceneStageAsync(
        string chapterGuid, string sceneId, string? stageKey)
    {
        await Service.SetSceneStageAsync(chapterGuid, sceneId, stageKey);
        return _workspace.BuildState();
    }

    /// <summary>Scenes and words per stage, for the Dashboard.</summary>
    [JsonRpcMethod("stages/breakdown")]
    public SceneStageTallyDto[] Breakdown()
        => [.. Service.Breakdown().Select(t => new SceneStageTallyDto(
            t.Key, t.Label, t.Color, t.CountsAsWritten, t.SceneCount, t.WordCount))];

    private static SceneStageDto ToDto(SceneStage stage)
        => new(stage.Key, stage.Label, stage.Color, stage.CountsAsWritten);
}

public sealed record SceneStageDto(string Key, string Label, string Color, bool CountsAsWritten);

/// <summary>One row of the stage breakdown. An empty <c>Key</c> is the scenes
/// with no stage set.</summary>
public sealed record SceneStageTallyDto(
    string Key, string Label, string Color, bool CountsAsWritten, int SceneCount, int WordCount);
