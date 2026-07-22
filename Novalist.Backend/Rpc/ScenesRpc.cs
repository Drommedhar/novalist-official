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
        return new SceneContentDto(sceneId, html);
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
            StoryDateFormatter.ExtractLeadingDate(storyDate));
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

    [JsonRpcMethod("scenes/archive")]
    public async Task ArchiveAsync(string chapterGuid, string sceneId)
    {
        await _workspace.Projects.ArchiveSceneAsync(chapterGuid, sceneId);
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
        scene.Comments = comments.Length == 0
            ? null
            : comments.Select(c => new Novalist.Core.Models.SceneComment
            {
                Id = c.Id,
                AnchorText = c.AnchorText,
                Text = c.Text,
                Resolved = c.Resolved
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

    [JsonRpcMethod("scenes/write")]
    public async Task<SceneWriteResultDto> WriteAsync(
        string chapterGuid,
        string sceneId,
        string html,
        string plainText)
    {
        var wordCount = await _workspace.WriteSceneAsync(chapterGuid, sceneId, html, plainText);
        return new SceneWriteResultDto(sceneId, wordCount);
    }
}

public sealed record SceneContentDto(string SceneId, string Html);

public sealed record SceneMetaDto(
    string SceneId,
    string? Synopsis,
    string? Notes,
    string StoryDate,
    string? IsoDate);

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

public sealed record SceneWriteResultDto(string SceneId, int WordCount);
