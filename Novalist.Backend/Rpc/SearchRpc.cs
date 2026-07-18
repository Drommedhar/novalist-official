using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Project-wide find and replace over scene files.</summary>
public sealed class SearchRpc
{
    private readonly Workspace _workspace;

    public SearchRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private FindReplaceService Service => new(_workspace.Projects);

    [JsonRpcMethod("search/find")]
    public async Task<FindMatchDto[]> FindAsync(
        string pattern,
        bool matchCase,
        bool wholeWord,
        bool useRegex,
        string scope,
        string? anchorChapterGuid,
        string? anchorSceneId,
        CancellationToken cancellationToken)
    {
        var matches = await Service.FindAsync(new FindOptions
        {
            Pattern = pattern,
            MatchCase = matchCase,
            WholeWord = wholeWord,
            UseRegex = useRegex,
            Scope = Enum.Parse<FindScope>(scope),
            AnchorChapterGuid = anchorChapterGuid,
            AnchorSceneId = anchorSceneId
        }, cancellationToken);
        return matches
            .Select(m => new FindMatchDto(
                m.ChapterGuid, m.ChapterTitle, m.SceneId, m.SceneTitle,
                m.Index, m.Length, m.Before, m.MatchedText, m.After))
            .ToArray();
    }

    [JsonRpcMethod("search/replaceAll")]
    public async Task<int> ReplaceAllAsync(
        string pattern,
        string replacement,
        bool matchCase,
        bool wholeWord,
        bool useRegex,
        string scope,
        string? anchorChapterGuid,
        string? anchorSceneId,
        CancellationToken cancellationToken)
    {
        var snapshots = new SnapshotService(_workspace.Projects, _workspace.FileService);
        return await Service.ReplaceAllAsync(new FindOptions
        {
            Pattern = pattern,
            Replacement = replacement,
            MatchCase = matchCase,
            WholeWord = wholeWord,
            UseRegex = useRegex,
            Scope = Enum.Parse<FindScope>(scope),
            AnchorChapterGuid = anchorChapterGuid,
            AnchorSceneId = anchorSceneId
        }, snapshots, cancellationToken);
    }
}

public sealed record FindMatchDto(
    string ChapterGuid,
    string ChapterTitle,
    string SceneId,
    string SceneTitle,
    int Index,
    int Length,
    string Before,
    string MatchedText,
    string After);
