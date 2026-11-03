using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class ScenesRpc
{
    /// <summary>Checks the active book after returning from an external editor.
    /// Reading updates counts, never prose, word history, or an editor's save base.</summary>
    [JsonRpcMethod("scenes/refresh")]
    public async Task<SceneRefreshDto> RefreshAsync(SceneVersionDto[] versions)
    {
        var counts = new List<RefreshedSceneCountDto>();
        var changes = new List<SceneDiskChangeDto>();
        var updates = new List<(SceneData Scene, int Count)>();
        var known = versions.ToLookup(version => (version.ChapterGuid, version.SceneId));
        var projects = _workspace.Projects;
        foreach (var chapter in projects.GetChaptersOrdered())
        {
            foreach (var scene in projects.GetScenesForChapter(chapter.Guid))
            {
                // A cloud file temporarily absent is not an empty manuscript.
                if (!await _workspace.FileService.ExistsAsync(projects.GetSceneFilePath(chapter, scene))) continue;
                var html = await projects.ReadSceneContentAsync(chapter, scene);
                var wordCount = Workspace.CountWords(TextDiff.StripHtml(html));
                counts.Add(new(scene.Id, wordCount));
                if (wordCount != scene.WordCount) updates.Add((scene, wordCount));
                var hash = ContentHasher.Hash(html);
                if (known[(chapter.Guid, scene.Id)].Any(version => version.Hash != hash))
                    changes.Add(new(chapter.Guid, scene.Id, html, hash, wordCount));
            }
        }
        if (updates.Count > 0)
        {
            foreach (var (scene, count) in updates) scene.WordCount = count;
            await projects.SaveScenesAsync();
        }
        return new(counts, changes);
    }
}

public sealed record SceneVersionDto(string ChapterGuid, string SceneId, string Hash);
public sealed record RefreshedSceneCountDto(string SceneId, int WordCount);
public sealed record SceneDiskChangeDto(string ChapterGuid, string SceneId, string Html, string Hash, int WordCount);
public sealed record SceneRefreshDto(IReadOnlyList<RefreshedSceneCountDto> Counts, IReadOnlyList<SceneDiskChangeDto> Changes);
