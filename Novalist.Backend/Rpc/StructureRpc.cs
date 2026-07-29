using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// The story structure the book is written against, and where the manuscript
/// actually puts each of its beats.
/// </summary>
public sealed class StructureRpc
{
    private readonly Workspace _workspace;

    public StructureRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private StoryStructureService Service => new(_workspace.Projects);

    /// <summary>The chosen structure's id, or empty for none.</summary>
    [JsonRpcMethod("structure/get")]
    public string Get() => _workspace.Projects.ActiveBook?.StructureTemplateId ?? string.Empty;

    [JsonRpcMethod("structure/set")]
    public async Task<StructureBeatDto[]> SetAsync(string? templateId)
    {
        await Service.SetTemplateAsync(templateId);
        return Beats();
    }

    /// <summary>Every beat with the scene bound to it and how far off its
    /// intended position that scene sits.</summary>
    [JsonRpcMethod("structure/beats")]
    public StructureBeatDto[] Beats()
        => [.. Service.Beats().Select(b => new StructureBeatDto(
            b.Key, b.Title, b.Description, b.TargetPercent,
            b.SceneId, b.SceneTitle, b.ChapterGuid,
            b.ActualPercent, b.IsFilled, b.DriftPercent))];

    [JsonRpcMethod("structure/bindScene")]
    public async Task<StructureBeatDto[]> BindSceneAsync(
        string chapterGuid, string sceneId, string? beatKey)
    {
        await Service.SetSceneBeatAsync(chapterGuid, sceneId, beatKey);
        return Beats();
    }

    /// <summary>Creates a placeholder scene for every unfilled beat, and returns
    /// the fresh project state so the binder shows them.</summary>
    [JsonRpcMethod("structure/fillGaps")]
    public async Task<FillGapsResultDto> FillGapsAsync()
    {
        var created = await Service.FillGapsAsync();
        return new FillGapsResultDto(created, Beats(), _workspace.BuildState());
    }

    /// <summary>The structures on offer, with the beats each defines.</summary>
    [JsonRpcMethod("structure/templates")]
    public StructureTemplateBeatsDto[] Templates()
        => [.. StoryStructureTemplates.All.Select(t => new StructureTemplateBeatsDto(
            t.Id, t.DisplayName, t.Description, t.Beats.Count))];
}

/// <summary>
/// One beat and where the manuscript puts it. <c>ActualPercent</c> is -1 when
/// nothing is bound, which is not the same as a beat at the very start.
/// </summary>
public sealed record StructureBeatDto(
    string Key,
    string Title,
    string Description,
    int TargetPercent,
    string? SceneId,
    string? SceneTitle,
    string? ChapterGuid,
    int ActualPercent,
    bool IsFilled,
    int DriftPercent);

public sealed record StructureTemplateBeatsDto(
    string Id, string DisplayName, string Description, int BeatCount);

public sealed record FillGapsResultDto(
    int Created, StructureBeatDto[] Beats, ProjectStateDto State);
