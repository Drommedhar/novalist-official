using Novalist.Core.Models;
using Novalist.Core.Utilities;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Scene content IO for the editor round-trip.</summary>
public sealed class ScenesRpc
{
    private readonly Workspace _workspace;

    public ScenesRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    [JsonRpcMethod("scenes/read")]
    public async Task<SceneContentDto> ReadAsync(string chapterGuid, string sceneId)
    {
        var (chapter, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        var html = await _workspace.Projects.ReadSceneContentAsync(chapter, scene);
        // Opening a scene in the editor is a scenes/read; notify extensions so the
        // AI Assistant tracks the current-scene context and knowledge cache.
        _workspace.RaiseSceneOpened(chapter, scene);
        // The hash rides along so the editor can prove, at save time, that it is
        // overwriting the version it actually read.
        return new SceneContentDto(sceneId, html, Core.Services.ContentHasher.Hash(html));
    }

    [JsonRpcMethod("scenes/getMeta")]
    public SceneMetaDto GetMeta(string chapterGuid, string sceneId)
    {
        var (chapter, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        var storyDate = SceneStoryDate.Resolve(chapter, scene);
        return new SceneMetaDto(
            scene.Id,
            scene.Synopsis,
            scene.Notes,
            storyDate,
            StoryDateFormatter.ExtractLeadingDate(storyDate),
            scene.Cast?.ToArray() ?? [],
            scene.FocusEntityId);
    }

    /// <summary>
    /// Who and what is in this scene, and which of them it is about. Both are
    /// the writer's statement rather than something read out of the prose.
    /// </summary>
    [JsonRpcMethod("scenes/setCast")]
    public async Task<SceneMetaDto> SetCastAsync(
        string chapterGuid, string sceneId, string[]? cast, string? focusEntityId)
    {
        var (_, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        var clean = (cast ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        scene.Cast = clean.Count > 0 ? clean : null;
        // A focus nobody put in the scene is a dangling reference, so it only
        // sticks while its entity is in the cast.
        scene.FocusEntityId = !string.IsNullOrWhiteSpace(focusEntityId)
                              && clean.Contains(focusEntityId, StringComparer.Ordinal)
            ? focusEntityId
            : null;
        await _workspace.Projects.SaveScenesAsync();
        return GetMeta(chapterGuid, sceneId);
    }

    /// <summary>
    /// Merge-patches the scene's analysis overrides: only non-null fields on the
    /// patch are applied; the rest keep their existing value. Clearing a single
    /// field is done via <c>scenes/resetAnalysisOverride</c>.
    /// </summary>
    [JsonRpcMethod("scenes/setAnalysisOverride")]
    public async Task SetAnalysisOverrideAsync(string chapterGuid, string sceneId, AnalysisOverrideDto patch)
    {
        var (_, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        var overrides = scene.AnalysisOverrides?.Clone() ?? new SceneAnalysisOverrides();

        if (patch.Pov != null)
            overrides.Pov = patch.Pov.Trim();
        if (patch.Emotion != null)
            overrides.Emotion = patch.Emotion.Trim();
        if (patch.Intensity.HasValue)
            overrides.Intensity = Math.Clamp(patch.Intensity.Value, -10, 10);
        if (patch.Conflict != null)
            overrides.Conflict = patch.Conflict.Trim();
        if (patch.Tags != null)
            overrides.Tags = patch.Tags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .ToList();

        await _workspace.Projects.SetSceneAnalysisOverridesAsync(
            chapterGuid, sceneId, overrides.HasValues ? overrides : null);
    }

    /// <summary>Clears a single analysis-override field so it reverts to the
    /// auto-computed value. Unknown fields are ignored.</summary>
    [JsonRpcMethod("scenes/resetAnalysisOverride")]
    public async Task ResetAnalysisOverrideAsync(string chapterGuid, string sceneId, string field)
    {
        var (_, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        var overrides = scene.AnalysisOverrides?.Clone();
        if (overrides == null)
            return;

        switch (field)
        {
            case "pov": overrides.Pov = null; break;
            case "emotion": overrides.Emotion = null; break;
            case "intensity": overrides.Intensity = null; break;
            case "conflict": overrides.Conflict = null; break;
            case "tags": overrides.Tags = null; break;
            default: return;
        }

        await _workspace.Projects.SetSceneAnalysisOverridesAsync(
            chapterGuid, sceneId, overrides.HasValues ? overrides : null);
    }

    [JsonRpcMethod("scenes/archived")]
    public ArchivedSceneDto[] GetArchived() =>
        _workspace.Projects.GetArchivedScenes()
            .Select(s => new ArchivedSceneDto(
                s.Id, s.Title, s.WordCount, s.ArchivedAt?.ToString("o")))
            .ToArray();

    [JsonRpcMethod("scenes/restoreArchived")]
    public async Task RestoreArchivedAsync(string sceneId, string targetChapterGuid)
    {
        await _workspace.Projects.RestoreArchivedSceneAsync(sceneId, targetChapterGuid, null);
    }

    [JsonRpcMethod("scenes/getAnnotations")]
    public SceneAnnotationsDto GetAnnotations(string chapterGuid, string sceneId)
    {
        var (_, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        return new SceneAnnotationsDto(
            (scene.Comments ?? [])
                .Select(c => new SceneCommentDto(c.Id, c.AnchorText, c.Text, c.Resolved))
                .ToArray(),
            (scene.Footnotes ?? [])
                .Select(f => new SceneFootnoteDto(f.Id, f.Number, f.Text))
                .ToArray());
    }

    [JsonRpcMethod("scenes/setAnnotations")]
    public async Task SetAnnotationsAsync(
        string chapterGuid,
        string sceneId,
        SceneCommentDto[] comments,
        SceneFootnoteDto[] footnotes)
    {
        var (_, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        // The panel only knows about the text and the resolved flag. Rebuilding
        // each comment from that alone would throw away its author, its replies
        // and whether it is a to-do every time somebody edited one.
        var existing = (scene.Comments ?? []).ToDictionary(c => c.Id, StringComparer.Ordinal);
        var author = _workspace.Projects.ProjectSettings.Author;
        scene.Comments = comments.Length == 0
            ? null
            : comments.Select(c =>
            {
                var kept = existing.GetValueOrDefault(c.Id);
                return new Novalist.Core.Models.SceneComment
                {
                    Id = c.Id,
                    AnchorText = c.AnchorText,
                    Text = c.Text,
                    Resolved = c.Resolved,
                    CreatedAt = kept?.CreatedAt ?? DateTime.UtcNow,
                    Author = kept?.Author ?? (string.IsNullOrWhiteSpace(author) ? null : author),
                    IsTodo = kept?.IsTodo ?? false,
                    Replies = kept?.Replies
                };
            }).ToList();
        scene.Footnotes = footnotes.Length == 0
            ? null
            : footnotes.Select(f => new Novalist.Core.Models.SceneFootnote
            {
                Id = f.Id,
                Number = f.Number,
                Text = f.Text
            }).ToList();
        await _workspace.Projects.SaveScenesAsync();
    }

    /// <summary>
    /// Saves a scene. <paramref name="expectedHash"/> is what the editor read;
    /// when it no longer matches the file, the save is refused and the result
    /// carries what is on disk instead. Omitting it skips the check, which is
    /// how callers with no editor behind them keep working.
    /// </summary>
    [JsonRpcMethod("scenes/write")]
    public async Task<SceneWriteResultDto> WriteAsync(
        string chapterGuid,
        string sceneId,
        string html,
        string plainText,
        string? expectedHash = null)
    {
        var (outcome, wordCount) = await _workspace.WriteSceneCheckedAsync(
            chapterGuid, sceneId, html, plainText, expectedHash);
        return new SceneWriteResultDto(
            sceneId, wordCount, outcome.Hash, outcome.Conflicted, outcome.DiskHtml);
    }

    /// <summary>The writer's chosen resolution. Both versions are snapshotted
    /// before it lands, so a wrong click at the merge dialog is recoverable.</summary>
    [JsonRpcMethod("scenes/resolveConflict")]
    public async Task<SceneWriteResultDto> ResolveConflictAsync(
        string chapterGuid, string sceneId, string html, string plainText)
    {
        var (chapter, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        var hash = await _workspace.SceneConflicts.ResolveAsync(chapter, scene, html);
        // Re-save through the unchecked path so the word count, history and
        // manifest catch up with the resolved text.
        var wordCount = await _workspace.WriteSceneAsync(chapterGuid, sceneId, html, plainText);
        return new SceneWriteResultDto(sceneId, wordCount, hash, false, null);
    }

    /// <summary>The two versions lined up row by row for the merge dialog.</summary>
    [JsonRpcMethod("scenes/mergeRows")]
    public MergeRowDto[] MergeRows(string mineHtml, string theirsHtml)
        => [.. Core.Services.SceneConflictGuard.Rows(mineHtml, theirsHtml)
            .Select(r => new MergeRowDto(r.Mine, r.Theirs, r.State))];
}

/// <summary>One row of the merge view. <c>State</c> is "equal", "changed",
/// "mine" (only the writer has it) or "theirs" (only the file has it).</summary>
public sealed record MergeRowDto(string? Mine, string? Theirs, string State);

public sealed record SceneContentDto(string SceneId, string Html, string Hash);

public sealed record SceneMetaDto(
    string SceneId,
    string? Synopsis,
    string? Notes,
    string StoryDate,
    string? IsoDate,
    string[] Cast,
    string? FocusEntityId);

/// <summary>Partial patch for a scene's analysis overrides. Null fields are
/// left unchanged; only supplied fields are written.</summary>
public sealed record AnalysisOverrideDto(
    string? Pov,
    string? Emotion,
    int? Intensity,
    string? Conflict,
    string[]? Tags);

public sealed record ArchivedSceneDto(string Id, string Title, int WordCount, string? ArchivedAt);

public sealed record SceneAnnotationsDto(
    IReadOnlyList<SceneCommentDto> Comments,
    IReadOnlyList<SceneFootnoteDto> Footnotes);

public sealed record SceneCommentDto(string Id, string AnchorText, string Text, bool Resolved);

public sealed record SceneFootnoteDto(string Id, int Number, string Text);

/// <summary>The result of a scene save. <c>Conflicted</c> means nothing was
/// written because the file changed underneath, and <c>DiskHtml</c> carries what
/// is actually there.</summary>
public sealed record SceneWriteResultDto(
    string SceneId,
    int WordCount,
    string Hash,
    bool Conflicted,
    string? DiskHtml);
