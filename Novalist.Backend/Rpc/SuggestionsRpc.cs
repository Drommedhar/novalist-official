using Novalist.Core.Utilities;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Suggested edits in the prose: what is pending, and taking or turning down
/// one of them.
///
/// The edits live in the scene's own HTML, so there is no separate store to
/// keep in step and nothing to go stale when a scene is copied into another
/// draft.
/// </summary>
public sealed class SuggestionsRpc
{
    private readonly Workspace _workspace;

    public SuggestionsRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    /// <summary>Suggested edits in one scene, in the order they appear.</summary>
    [JsonRpcMethod("suggestions/forScene")]
    public async Task<SuggestionDto[]> ForSceneAsync(string chapterGuid, string sceneId)
    {
        var (chapter, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        var html = await _workspace.Projects.ReadSceneContentAsync(chapter, scene);
        return [.. TrackedChanges.Pending(html).Select(ToDto)];
    }

    /// <summary>
    /// Every scene in the book that has somebody waiting on it, with a count.
    /// A suggestion nobody can find is a suggestion nobody answers, and the
    /// scene it sits in is the only place it shows otherwise.
    /// </summary>
    [JsonRpcMethod("suggestions/inbox")]
    public async Task<SuggestionSceneDto[]> InboxAsync()
    {
        var rows = new List<SuggestionSceneDto>();
        foreach (var chapter in _workspace.Projects.GetChaptersOrdered())
        {
            foreach (var scene in _workspace.Projects.GetScenesForChapter(chapter.Guid))
            {
                var html = await _workspace.Projects.ReadSceneContentAsync(chapter, scene);
                var count = TrackedChanges.Count(html);
                if (count == 0) continue;
                rows.Add(new SuggestionSceneDto(
                    chapter.Guid, chapter.Title, scene.Id, scene.Title, count));
            }
        }
        return [.. rows];
    }

    /// <summary>Takes one suggestion. Returns what is still pending in the scene.</summary>
    [JsonRpcMethod("suggestions/accept")]
    public Task<SuggestionDto[]> AcceptAsync(string chapterGuid, string sceneId, string changeId)
        => ResolveAsync(chapterGuid, sceneId, html => TrackedChanges.Accept(html, changeId));

    /// <summary>Turns one suggestion down.</summary>
    [JsonRpcMethod("suggestions/reject")]
    public Task<SuggestionDto[]> RejectAsync(string chapterGuid, string sceneId, string changeId)
        => ResolveAsync(chapterGuid, sceneId, html => TrackedChanges.Reject(html, changeId));

    [JsonRpcMethod("suggestions/acceptAll")]
    public Task<SuggestionDto[]> AcceptAllAsync(string chapterGuid, string sceneId)
        => ResolveAsync(chapterGuid, sceneId, TrackedChanges.AcceptAll);

    [JsonRpcMethod("suggestions/rejectAll")]
    public Task<SuggestionDto[]> RejectAllAsync(string chapterGuid, string sceneId)
        => ResolveAsync(chapterGuid, sceneId, TrackedChanges.RejectAll);

    /// <summary>
    /// Rewrites the scene through <paramref name="resolve"/> and saves it. The
    /// word count is recomputed because taking or refusing an edit changes how
    /// long the scene is, and a stale count would be visible on the dashboard
    /// before it was visible anywhere else.
    /// </summary>
    private async Task<SuggestionDto[]> ResolveAsync(
        string chapterGuid, string sceneId, Func<string, string> resolve)
    {
        var (chapter, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        var html = await _workspace.Projects.ReadSceneContentAsync(chapter, scene);
        var resolved = resolve(html);

        await _workspace.WriteSceneAsync(
            chapterGuid, sceneId, resolved, TextDiff.StripHtml(resolved));

        return [.. TrackedChanges.Pending(resolved).Select(ToDto)];
    }

    private static SuggestionDto ToDto(TrackedChange change) => new(
        change.Id,
        change.Kind == ChangeKind.Deletion ? "deletion" : "insertion",
        change.Text,
        change.Author,
        change.At);
}

/// <summary>One suggested edit, as the review panel sees it.</summary>
public sealed record SuggestionDto(
    string Id, string Kind, string Text, string Author, string At);

/// <summary>A scene with suggestions waiting on it.</summary>
public sealed record SuggestionSceneDto(
    string ChapterGuid, string ChapterTitle, string SceneId, string SceneTitle, int Count);
