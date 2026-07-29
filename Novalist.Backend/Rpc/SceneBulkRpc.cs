using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Operations over a set of selected scenes. Each one is a single round trip so
/// the renderer cannot interleave another edit halfway through a bulk change,
/// and so a selection is applied or not at all rather than partly.
/// </summary>
public sealed class SceneBulkRpc
{
    private readonly Workspace _workspace;

    public SceneBulkRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private SceneBulkService Service
        => new(_workspace.Projects, new InWorldCalendarService());

    [JsonRpcMethod("sceneBulk/delete")]
    public async Task<BulkResultDto> DeleteAsync(string[] sceneIds)
    {
        var count = await Service.DeleteAsync(sceneIds);
        return new BulkResultDto(count, _workspace.BuildState());
    }

    [JsonRpcMethod("sceneBulk/archive")]
    public async Task<BulkResultDto> ArchiveAsync(string[] sceneIds)
    {
        var count = await Service.ArchiveAsync(sceneIds);
        return new BulkResultDto(count, _workspace.BuildState());
    }

    /// <summary>Adds tags to every selected scene, or replaces their tags when
    /// <paramref name="replace"/> is set.</summary>
    /// <summary>Includes these scenes in exports, or holds them back.</summary>
    [JsonRpcMethod("sceneBulk/setExportInclusion")]
    public async Task<BulkResultDto> SetExportInclusionAsync(string[] sceneIds, bool included)
    {
        var count = await Service.SetExportInclusionAsync(sceneIds, included);
        return new BulkResultDto(count, _workspace.BuildState());
    }

    [JsonRpcMethod("sceneBulk/setTags")]
    public async Task<BulkResultDto> SetTagsAsync(string[] sceneIds, string[] tags, bool replace)
    {
        var count = await Service.SetTagsAsync(sceneIds, tags, replace);
        return new BulkResultDto(count, _workspace.BuildState());
    }

    /// <summary>What a shift would do. Read-only, so the dialog can show a
    /// before/after list before the writer commits to moving anything.</summary>
    [JsonRpcMethod("sceneBulk/previewDateShift")]
    public SceneDateShiftDto[] PreviewDateShift(string[] sceneIds, long days)
        => [.. Service.PreviewDateShift(sceneIds, days)
            .Select(r => new SceneDateShiftDto(r.SceneId, r.Title, r.Before, r.After))];

    [JsonRpcMethod("sceneBulk/shiftDates")]
    public async Task<BulkResultDto> ShiftDatesAsync(string[] sceneIds, long days)
    {
        var count = await Service.ShiftDatesAsync(sceneIds, days);
        return new BulkResultDto(count, _workspace.BuildState());
    }

    /// <summary>Moves the selection into a chapter, ending at
    /// <paramref name="targetIndex"/>. Wraps the existing move so the bulk bar
    /// has one consistent shape to call.</summary>
    [JsonRpcMethod("sceneBulk/moveToChapter")]
    public async Task<BulkResultDto> MoveToChapterAsync(
        string[] sceneIds, string targetChapterGuid, int targetIndex)
    {
        var count = Service.Resolve(sceneIds).Count;
        await _workspace.Projects.MoveScenesAsync(sceneIds, targetChapterGuid, targetIndex);
        return new BulkResultDto(count, _workspace.BuildState());
    }
}

/// <summary>How many scenes the operation touched, plus the project state the
/// renderer should adopt — one round trip instead of a follow-up refresh.</summary>
public sealed record BulkResultDto(int Count, ProjectStateDto State);

/// <summary>One row of a date-shift preview. Equal Before and After means the
/// scene does not move.</summary>
public sealed record SceneDateShiftDto(string SceneId, string Title, string Before, string After);
