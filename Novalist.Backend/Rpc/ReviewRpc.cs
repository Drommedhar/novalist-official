using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Reading an editor's marked-up Word document back in.</summary>
public sealed class ReviewRpc
{
    private readonly Workspace _workspace;

    public ReviewRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    /// <summary>
    /// Comments and tracked changes from a .docx. A file that cannot be read
    /// comes back empty rather than as an error: the writer did not author it,
    /// so "nothing to import" is the useful answer.
    /// </summary>
    [JsonRpcMethod("review/read")]
    public Task<ReviewDto> ReadAsync(string path)
    {
        var review = DocxReviewReader.Read(path);
        return Task.FromResult(new ReviewDto(
            review.Comments
                .Select(c => new ReviewCommentDto(c.Id, c.Author, c.Date, c.Text, c.AnchorText))
                .ToArray(),
            review.Revisions
                .Select(r => new ReviewRevisionDto(r.Kind, r.Author, r.Date, r.Text))
                .ToArray()));
    }

    /// <summary>
    /// Turns imported comments into scene comments on the open scene, so an
    /// editor's notes land where the writer works instead of staying in a list.
    /// Returns how many were added.
    /// </summary>
    [JsonRpcMethod("review/applyComments")]
    public async Task<int> ApplyCommentsAsync(
        string chapterGuid, string sceneId, ReviewCommentDto[] comments)
    {
        var (_, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        scene.Comments ??= [];

        var added = 0;
        foreach (var comment in comments)
        {
            if (string.IsNullOrWhiteSpace(comment.Text))
                continue;

            scene.Comments.Add(new Core.Models.SceneComment
            {
                AnchorText = comment.AnchorText,
                // The editor's name rides along in the text: SceneComment has no
                // author field, and inventing one would change the on-disk shape
                // for every project.
                Text = string.IsNullOrWhiteSpace(comment.Author)
                    ? comment.Text
                    : $"{comment.Author}: {comment.Text}"
            });
            added++;
        }

        if (added > 0)
            await _workspace.Projects.SaveScenesAsync();

        return added;
    }
}

public sealed record ReviewCommentDto(
    string Id, string Author, string Date, string Text, string AnchorText);

public sealed record ReviewRevisionDto(string Kind, string Author, string Date, string Text);

public sealed record ReviewDto(ReviewCommentDto[] Comments, ReviewRevisionDto[] Revisions);
