using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>The named scene rubric, its advice, and one scene's answers.</summary>
public sealed class RubricRpc
{
    private readonly Workspace _workspace;

    public RubricRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    /// <summary>
    /// The elements themselves. Sent rather than duplicated in the interface,
    /// so the questions and the advice have one home.
    /// </summary>
    [JsonRpcMethod("rubric/elements")]
    public RubricElementDto[] Elements()
        => [.. SceneRubric.Elements.Select(e =>
            new RubricElementDto(e.Key, e.Group, e.Name, e.Question, e.Advice))];

    /// <summary>What one scene has been scored against, and what it adds up to.</summary>
    [JsonRpcMethod("rubric/scene")]
    public RubricSceneDto Scene(string chapterGuid, string sceneId)
    {
        var scene = Find(chapterGuid, sceneId);
        var summary = SceneRubric.Summarise(chapterGuid, sceneId, scene?.Properties);
        return new RubricSceneDto(
            [.. SceneRubric.Read(scene?.Properties)
                .Select(s => new RubricScoreDto(s.ElementKey, s.Score))],
            summary.Answered,
            summary.Weak,
            Math.Round(summary.Average, 2));
    }

    /// <summary>
    /// Scores one element. Zero means not asked here, which removes the answer
    /// rather than storing a nought.
    /// </summary>
    [JsonRpcMethod("rubric/setScore")]
    public async Task<RubricSceneDto> SetScoreAsync(
        string chapterGuid, string sceneId, string elementKey, int score)
    {
        var scene = Find(chapterGuid, sceneId);
        if (scene == null) return Scene(chapterGuid, sceneId);

        scene.Properties ??= [];
        SceneRubric.Write(scene.Properties, elementKey, score);
        await _workspace.Projects.SaveScenesAsync();
        return Scene(chapterGuid, sceneId);
    }

    /// <summary>
    /// The scenes with the most weak answers, worst first.
    ///
    /// A rubric answered scene by scene tells a writer about one scene. What a
    /// revision needs is which scenes to open, which is a different question
    /// and the reason the rubric is worth filling in at all.
    /// </summary>
    [JsonRpcMethod("rubric/weakest")]
    public RubricWeakSceneDto[] Weakest(int limit = 20)
    {
        var rows = new List<RubricWeakSceneDto>();
        foreach (var chapter in _workspace.Projects.GetChaptersOrdered())
        {
            foreach (var scene in _workspace.Projects.GetScenesForChapter(chapter.Guid))
            {
                if (scene.ArchivedAt != null) continue;
                var summary = SceneRubric.Summarise(chapter.Guid, scene.Id, scene.Properties);
                // A scene nobody has read against the rubric is not weak, it is
                // unread, and listing it would bury the ones that were judged.
                if (summary.Answered == 0 || summary.Weak == 0) continue;
                rows.Add(new RubricWeakSceneDto(
                    chapter.Guid, scene.Id, chapter.Title, scene.Title,
                    summary.Answered, summary.Weak, Math.Round(summary.Average, 2)));
            }
        }

        return [.. rows
            .OrderByDescending(r => r.Weak)
            .ThenBy(r => r.Average)
            .Take(Math.Max(1, limit))];
    }

    private Core.Models.SceneData? Find(string chapterGuid, string sceneId)
        => _workspace.Projects.GetScenesForChapter(chapterGuid)
            .FirstOrDefault(s => s.Id == sceneId);
}

/// <summary>One element of the rubric, with the advice bound to it.</summary>
public sealed record RubricElementDto(
    string Key, string Group, string Name, string Question, string Advice);

/// <summary>One element's answer for a scene.</summary>
public sealed record RubricScoreDto(string ElementKey, int Score);

/// <summary>A scene's answers and what they add up to.</summary>
public sealed record RubricSceneDto(
    IReadOnlyList<RubricScoreDto> Scores, int Answered, int Weak, double Average);

/// <summary>A scene worth opening again.</summary>
public sealed record RubricWeakSceneDto(
    string ChapterGuid, string SceneId, string ChapterTitle, string SceneTitle,
    int Answered, int Weak, double Average);
