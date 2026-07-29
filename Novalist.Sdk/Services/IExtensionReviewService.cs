namespace Novalist.Sdk.Services;

/// <summary>One comment on a scene.</summary>
public sealed class SceneCommentInfo
{
    public string Id { get; init; } = string.Empty;

    /// <summary>The phrase the comment is about. Empty for a comment on the whole scene.</summary>
    public string AnchorText { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;

    /// <summary>Who left it. Empty for the writer's own unattributed note.</summary>
    public string Author { get; init; } = string.Empty;

    public bool Resolved { get; init; }
}

/// <summary>
/// Comments and suggested edits on a scene.
///
/// An extension that read a scene and formed an opinion about it had one way to
/// say so: its own panel. It could not put the remark next to the sentence, and
/// it certainly could not propose the sentence. Both of those are how an editor
/// actually communicates, so both are here.
/// </summary>
public interface IExtensionReviewService
{
    /// <summary>Comments on a scene, resolved ones included.</summary>
    Task<IReadOnlyList<SceneCommentInfo>> GetCommentsAsync(string chapterGuid, string sceneId);

    /// <summary>
    /// Leaves a comment anchored to a phrase, and returns its id.
    ///
    /// <paramref name="anchorText"/> should be text that appears in the scene;
    /// one that does not is still stored, and shows as a comment on the scene
    /// rather than being dropped. <paramref name="author"/> is who is speaking -
    /// an unattributed machine remark is worse than none.
    /// </summary>
    Task<string> AddCommentAsync(
        string chapterGuid, string sceneId, string anchorText, string text, string author);

    /// <summary>Marks a comment resolved, or reopens it. False when the id is unknown.</summary>
    Task<bool> SetCommentResolvedAsync(
        string chapterGuid, string sceneId, string commentId, bool resolved);

    /// <summary>Deletes a comment. False when the id is unknown.</summary>
    Task<bool> DeleteCommentAsync(string chapterGuid, string sceneId, string commentId);

    /// <summary>
    /// Proposes replacing a phrase, as a suggested edit the writer can take or
    /// turn down.
    ///
    /// This is deliberately the only way an extension changes prose it did not
    /// write. Rewriting a sentence outright would put a machine's opinion into
    /// the manuscript with no record and no way back; a suggestion asks.
    ///
    /// Returns false when the phrase is not in the scene - there is no honest
    /// place to attach a proposal about words that are not there.
    /// </summary>
    /// <param name="replacement">
    /// The proposed wording. Empty proposes cutting the phrase.
    /// </param>
    Task<bool> SuggestEditAsync(
        string chapterGuid, string sceneId, string anchorText, string replacement, string author);

    /// <summary>How many suggested edits are still waiting in a scene.</summary>
    Task<int> PendingSuggestionCountAsync(string chapterGuid, string sceneId);
}
