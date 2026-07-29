using Novalist.Core.Models;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Every open note in the book, in one place.
///
/// A comment could only be found by reopening the scene it was left in: there
/// was no aggregate query over them at all. A writer's note to themselves and
/// an editor's question are the same shape of thing, and both are lost the
/// moment the scene is closed.
/// </summary>
public sealed class InboxRpc
{
    private readonly Workspace _workspace;

    public InboxRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    /// <summary>
    /// Open notes across the book, in reading order. Resolved ones are left
    /// out unless asked for: they are a record of a finished conversation.
    /// </summary>
    [JsonRpcMethod("inbox/list")]
    public InboxItemDto[] List(bool includeResolved = false)
    {
        var items = new List<InboxItemDto>();
        foreach (var chapter in _workspace.Projects.GetChaptersOrdered())
            foreach (var scene in _workspace.Projects.GetScenesForChapter(chapter.Guid))
                foreach (var comment in scene.Comments ?? [])
                {
                    if (comment.Resolved && !includeResolved) continue;
                    items.Add(new InboxItemDto(
                        chapter.Guid, chapter.Title, scene.Id, scene.Title,
                        comment.Id, comment.AnchorText, comment.Text,
                        comment.Author ?? string.Empty, comment.IsTodo, comment.Resolved,
                        comment.CreatedAt.ToString("o"),
                        [.. (comment.Replies ?? []).Select(r => new InboxReplyDto(
                            r.Id, r.Author, r.Text, r.CreatedAt.ToString("o")))]));
                }
        return [.. items];
    }

    /// <summary>Marks a note done, or reopens it.</summary>
    [JsonRpcMethod("inbox/setResolved")]
    public async Task<InboxItemDto[]> SetResolvedAsync(string sceneId, string commentId, bool resolved)
    {
        var comment = Find(sceneId, commentId);
        comment.Resolved = resolved;
        await _workspace.Projects.SaveScenesAsync();
        return List();
    }

    /// <summary>Turns a remark into a job, or back again.</summary>
    [JsonRpcMethod("inbox/setTodo")]
    public async Task<InboxItemDto[]> SetTodoAsync(string sceneId, string commentId, bool isTodo)
    {
        var comment = Find(sceneId, commentId);
        comment.IsTodo = isTodo;
        await _workspace.Projects.SaveScenesAsync();
        return List();
    }

    /// <summary>Answers a note, which is how an editorial exchange happens.</summary>
    [JsonRpcMethod("inbox/reply")]
    public async Task<InboxItemDto[]> ReplyAsync(string sceneId, string commentId, string text)
    {
        var body = (text ?? string.Empty).Trim();
        if (body.Length == 0) throw new InvalidOperationException("A reply needs text.");

        var comment = Find(sceneId, commentId);
        (comment.Replies ??= []).Add(new CommentReply
        {
            Author = _workspace.Projects.ProjectSettings.Author,
            Text = body
        });
        await _workspace.Projects.SaveScenesAsync();
        return List();
    }

    private SceneComment Find(string sceneId, string commentId)
        => _workspace.Projects.GetChaptersOrdered()
            .SelectMany(c => _workspace.Projects.GetScenesForChapter(c.Guid))
            .Where(s => s.Id == sceneId)
            .SelectMany(s => s.Comments ?? [])
            .FirstOrDefault(c => c.Id == commentId)
           ?? throw new InvalidOperationException($"Unknown comment '{commentId}'.");
}

public sealed record InboxReplyDto(string Id, string Author, string Text, string CreatedAt);

/// <summary>One open note, with where it is and who left it.</summary>
public sealed record InboxItemDto(
    string ChapterGuid,
    string ChapterTitle,
    string SceneId,
    string SceneTitle,
    string CommentId,
    string AnchorText,
    string Text,
    string Author,
    bool IsTodo,
    bool Resolved,
    string CreatedAt,
    InboxReplyDto[] Replies);
