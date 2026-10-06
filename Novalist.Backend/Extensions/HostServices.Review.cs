using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;
using Novalist.Sdk.Services;

namespace Novalist.Backend.Extensions;

public sealed partial class HostServices
{
    // ── Review (IExtensionReviewService) ───────────────────────────

    Task<IReadOnlyList<SceneCommentInfo>> IExtensionReviewService.GetCommentsAsync(
        string chapterGuid, string sceneId)
    {
        var scene = FindScene(chapterGuid, sceneId);
        IReadOnlyList<SceneCommentInfo> comments = scene == null
            ? []
            : [.. (scene.Comments ?? []).Select(c => new SceneCommentInfo
            {
                Id = c.Id,
                AnchorText = c.AnchorText,
                Text = c.Text,
                Author = c.Author ?? string.Empty,
                Resolved = c.Resolved
            })];
        return Task.FromResult(comments);
    }

    async Task<string> IExtensionReviewService.AddCommentAsync(
        string chapterGuid, string sceneId, string anchorText, string text, string author)
    {
        var scene = FindScene(chapterGuid, sceneId);
        if (scene == null) return string.Empty;

        var comment = new SceneComment
        {
            AnchorText = anchorText ?? string.Empty,
            Text = text ?? string.Empty,
            Author = string.IsNullOrWhiteSpace(author) ? null : author
        };
        (scene.Comments ??= []).Add(comment);
        await _projectService.SaveScenesAsync();
        return comment.Id;
    }

    async Task<bool> IExtensionReviewService.SetCommentResolvedAsync(
        string chapterGuid, string sceneId, string commentId, bool resolved)
    {
        var comment = FindScene(chapterGuid, sceneId)?.Comments?
            .FirstOrDefault(c => c.Id == commentId);
        if (comment == null) return false;

        comment.Resolved = resolved;
        await _projectService.SaveScenesAsync();
        return true;
    }

    async Task<bool> IExtensionReviewService.DeleteCommentAsync(
        string chapterGuid, string sceneId, string commentId)
    {
        var comments = FindScene(chapterGuid, sceneId)?.Comments;
        var comment = comments?.FirstOrDefault(c => c.Id == commentId);
        if (comments == null || comment == null) return false;

        comments.Remove(comment);
        await _projectService.SaveScenesAsync();
        return true;
    }

    async Task<bool> IExtensionReviewService.SuggestEditAsync(
        string chapterGuid, string sceneId, string anchorText, string replacement, string author)
    {
        var chapter = _projectService.GetChaptersOrdered().FirstOrDefault(c => c.Guid == chapterGuid);
        var scene = FindScene(chapterGuid, sceneId);
        if (chapter == null || scene == null || string.IsNullOrEmpty(anchorText)) return false;

        var html = await _projectService.ReadSceneContentAsync(chapter, scene);
        var at = html.IndexOf(anchorText, StringComparison.Ordinal);
        // Nowhere honest to attach a proposal about words that are not there.
        if (at < 0) return false;

        var stamp = DateTime.UtcNow.ToString("o");
        var id = Guid.NewGuid().ToString("N")[..8];
        var proposal = TrackedChanges.Deletion(id + "d", anchorText, author ?? string.Empty, stamp)
            + (string.IsNullOrEmpty(replacement)
                ? string.Empty
                : TrackedChanges.Insertion(id + "i", replacement, author ?? string.Empty, stamp));

        var updated = html[..at] + proposal + html[(at + anchorText.Length)..];
        await _projectService.WriteSceneContentAsync(chapter, scene, updated);
        scene.WordCount = Workspace.CountWords(TextDiff.StripHtml(updated));
        await _projectService.SaveScenesAsync();
        return true;
    }

    async Task<int> IExtensionReviewService.PendingSuggestionCountAsync(
        string chapterGuid, string sceneId)
    {
        var chapter = _projectService.GetChaptersOrdered().FirstOrDefault(c => c.Guid == chapterGuid);
        var scene = FindScene(chapterGuid, sceneId);
        if (chapter == null || scene == null) return 0;

        return TrackedChanges.Count(await _projectService.ReadSceneContentAsync(chapter, scene));
    }

    private SceneData? FindScene(string chapterGuid, string sceneId)
        => !ChapterExists(chapterGuid)
            ? null
            : _projectService.GetScenesForChapter(chapterGuid).FirstOrDefault(s => s.Id == sceneId);
}
