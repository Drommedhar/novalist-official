using Novalist.Core.Services;
using Novalist.Core.Utilities;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Comparing two drafts of the open book, and taking a scene from one.</summary>
public sealed class DraftCompareRpc
{
    private readonly Workspace _workspace;

    public DraftCompareRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private DraftCompareService Service => new(
        _workspace.Projects,
        _workspace.FileService,
        new SnapshotService(_workspace.Projects, _workspace.FileService));

    /// <summary>The open book's drafts, so the compare dialog can offer both sides.</summary>
    [JsonRpcMethod("draftCompare/drafts")]
    public Task<DraftChoiceDto[]> DraftsAsync()
    {
        var book = _workspace.Projects.ActiveBook;
        if (book == null) return Task.FromResult(Array.Empty<DraftChoiceDto>());

        return Task.FromResult(book.Drafts
            .Select(d => new DraftChoiceDto(
                d.Id, d.Name, d.Id == book.ActiveDraft?.Id, d.ParentDraftId ?? string.Empty))
            .ToArray());
    }

    [JsonRpcMethod("draftCompare/compare")]
    public async Task<DraftComparisonDto?> CompareAsync(string leftDraftId, string rightDraftId)
    {
        var result = await Service.CompareAsync(leftDraftId, rightDraftId);
        if (result == null) return null;

        return new DraftComparisonDto(
            result.LeftDraftId, result.LeftName, result.RightDraftId, result.RightName,
            result.Scenes.Select(s => new DraftSceneDto(
                s.SceneId, s.Title, s.ChapterGuid, s.ChapterTitle,
                s.State.ToString().ToLowerInvariant(), s.LeftWords, s.RightWords)).ToArray(),
            result.SameCount, result.ChangedCount, result.AddedCount, result.RemovedCount,
            result.LeftWords, result.RightWords);
    }

    /// <summary>
    /// One scene as a side-by-side line diff between the two drafts. Same row
    /// shape as the snapshot diff, so the two read identically - a writer
    /// comparing drafts sees what they already know from comparing snapshots.
    /// </summary>
    [JsonRpcMethod("draftCompare/scene")]
    public async Task<SnapshotDiffRowDto[]> SceneAsync(
        string leftDraftId, string rightDraftId, string sceneId)
    {
        var service = Service;
        var left = await service.ReadSceneTextAsync(leftDraftId, sceneId);
        var right = await service.ReadSceneTextAsync(rightDraftId, sceneId);

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

    /// <summary>
    /// Copies one scene's prose from another draft into the active one,
    /// snapshotting what it lands on first.
    /// </summary>
    [JsonRpcMethod("draftCompare/take")]
    public Task<bool> TakeAsync(string fromDraftId, string sceneId) =>
        Service.TakeSceneAsync(fromDraftId, sceneId);
}

public sealed record DraftChoiceDto(string Id, string Name, bool IsActive, string ParentDraftId);

public sealed record DraftSceneDto(
    string SceneId, string Title, string ChapterGuid, string ChapterTitle,
    string State, int LeftWords, int RightWords);

public sealed record DraftComparisonDto(
    string LeftDraftId, string LeftName, string RightDraftId, string RightName,
    DraftSceneDto[] Scenes,
    int SameCount, int ChangedCount, int AddedCount, int RemovedCount,
    int LeftWords, int RightWords);
