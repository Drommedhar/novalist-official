using Novalist.Backend.Extensions;
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

    private FindReplaceService Service
        => new(_workspace.Projects, new EntityService(_workspace.Projects));

    /// <summary>
    /// One query across scenes (titles, prose, synopses, notes, comments and
    /// footnotes), every Codex entry, research items, and manual timeline events.
    /// Find &amp; Replace only searches scene prose; this powers the quick-open box.
    /// </summary>
    [JsonRpcMethod("search/global")]
    public async Task<GlobalSearchHitDto[]> GlobalAsync(
        string query, int limit, CancellationToken cancellationToken)
    {
        var service = new GlobalSearchService(
            _workspace.Projects,
            new EntityService(_workspace.Projects),
            new ResearchService(_workspace.Projects, _workspace.FileService));
        var hits = await service.SearchAsync(query, limit <= 0 ? 20 : limit, cancellationToken);
        Log.Info($"search/global len={query?.Length ?? 0} hits={hits.Count}.");
        return hits
            .Select(h => new GlobalSearchHitDto(
                h.Kind, h.Title, h.Subtitle, h.Snippet,
                h.ChapterGuid, h.SceneId, h.EntityTypeKey, h.EntityId, h.ResearchId))
            .ToArray();
    }

    [JsonRpcMethod("search/find")]
    public async Task<FindMatchDto[]> FindAsync(
        string pattern,
        bool matchCase,
        bool wholeWord,
        bool useRegex,
        string scope,
        string? anchorChapterGuid,
        string? anchorSceneId,
        bool includeSceneNotes,
        bool includeCodex,
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
            AnchorSceneId = anchorSceneId,
            IncludeSceneNotes = includeSceneNotes,
            IncludeCodex = includeCodex
        }, cancellationToken);
        return matches
            .Select(m => new FindMatchDto(
                m.ChapterGuid, m.ChapterTitle, m.SceneId, m.SceneTitle,
                m.Index, m.Length, m.Before, m.MatchedText, m.After, m.Field))
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
        bool includeSceneNotes,
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
            AnchorSceneId = anchorSceneId,
            IncludeSceneNotes = includeSceneNotes
        }, snapshots, cancellationToken);
    }
}

/// <summary>A global-search result. Which id fields are set says how to open it:
/// chapter+scene opens the editor, entity opens its Wiki article, research opens
/// the Research view; a timeline hit carries none and just reports the event.</summary>
public sealed record GlobalSearchHitDto(
    string Kind,
    string Title,
    string? Subtitle,
    string? Snippet,
    string? ChapterGuid,
    string? SceneId,
    string? EntityTypeKey,
    string? EntityId,
    string? ResearchId);

/// <summary>One match. <c>Field</c> says where it was found - prose, synopsis,
/// notes, comment or codex - so the writer knows what they are looking at, and
/// a Codex hit can be shown without pretending it opens a scene.</summary>
public sealed record FindMatchDto(
    string ChapterGuid,
    string ChapterTitle,
    string SceneId,
    string SceneTitle,
    int Index,
    int Length,
    string Before,
    string MatchedText,
    string After,
    string Field);
