using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>What is wrong with a promise, or nothing.</summary>
public enum PromiseState
{
    /// <summary>Paid off by a scene that comes after it. Nothing to do.</summary>
    Kept,

    /// <summary>Nothing pays it off. The gun stays on the mantel.</summary>
    Unpaid,

    /// <summary>The scene that paid it off is gone.</summary>
    Broken,

    /// <summary>The payoff comes before the setup in reading order.</summary>
    OutOfOrder
}

/// <summary>One promise, with the scenes on both ends resolved.</summary>
public sealed record PromiseReport(
    string SceneId,
    string SceneTitle,
    string ChapterGuid,
    string ChapterTitle,
    string PromiseId,
    string Label,
    string? PayoffSceneId,
    string? PayoffSceneTitle,
    PromiseState State);

/// <summary>
/// Setups and their payoffs.
///
/// Novalist had no edge between two scenes at all: a manual timeline event
/// could link to a chapter, character relationships joined entities, and
/// nothing said "this scene answers that one". A writer could not ask the only
/// question that matters about a setup, which is whether anything pays it off.
/// </summary>
public sealed class PromiseService
{
    private readonly IProjectService _projectService;

    public PromiseService(IProjectService projectService)
    {
        _projectService = projectService;
    }

    /// <summary>
    /// Every promise in the book, in reading order, each judged against where
    /// its payoff sits.
    /// </summary>
    public IReadOnlyList<PromiseReport> Report()
    {
        var chapters = _projectService.GetChaptersOrdered();
        // Reading order as a number per scene, so "before" and "after" mean the
        // same thing here as they do to a reader.
        var position = new Dictionary<string, int>(StringComparer.Ordinal);
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var chapter in chapters)
            foreach (var scene in _projectService.GetScenesForChapter(chapter.Guid))
            {
                position[scene.Id] = index++;
                titles[scene.Id] = scene.Title;
            }

        var reports = new List<PromiseReport>();
        foreach (var chapter in chapters)
            foreach (var scene in _projectService.GetScenesForChapter(chapter.Guid))
                foreach (var promise in scene.Promises ?? [])
                    reports.Add(new PromiseReport(
                        scene.Id, scene.Title, chapter.Guid, chapter.Title,
                        promise.Id, promise.Label,
                        promise.PayoffSceneId,
                        promise.PayoffSceneId != null && titles.TryGetValue(promise.PayoffSceneId, out var pt)
                            ? pt
                            : null,
                        Judge(promise, scene.Id, position)));

        return reports;
    }

    private static PromiseState Judge(
        ScenePromise promise, string sceneId, IReadOnlyDictionary<string, int> position)
    {
        if (string.IsNullOrWhiteSpace(promise.PayoffSceneId)) return PromiseState.Unpaid;
        if (!position.TryGetValue(promise.PayoffSceneId, out var payoffAt)) return PromiseState.Broken;
        // A payoff the reader meets before the setup is not a payoff. Moving a
        // scene is enough to cause this, which is why it is worth reporting.
        return position.TryGetValue(sceneId, out var setupAt) && payoffAt <= setupAt
            ? PromiseState.OutOfOrder
            : PromiseState.Kept;
    }

    /// <summary>
    /// Adds a promise to a scene, or edits one it already has. Returns the
    /// promise's id so a caller that just created one can point a payoff at it.
    /// </summary>
    public async Task<string> SaveAsync(
        string sceneId, string? promiseId, string label, string? payoffSceneId)
    {
        var scene = FindScene(sceneId) ?? throw new InvalidOperationException($"Unknown scene '{sceneId}'.");
        var text = (label ?? string.Empty).Trim();
        if (text.Length == 0) throw new InvalidOperationException("A promise needs a label.");

        // A scene cannot pay itself off; the report would read as kept while
        // nothing had been answered.
        var payoff = string.IsNullOrWhiteSpace(payoffSceneId) || payoffSceneId == sceneId
            ? null
            : payoffSceneId;

        var promises = scene.Promises ??= [];
        var existing = promises.FirstOrDefault(p => p.Id == promiseId);
        if (existing != null)
        {
            existing.Label = text;
            existing.PayoffSceneId = payoff;
        }
        else
        {
            existing = new ScenePromise { Label = text, PayoffSceneId = payoff };
            promises.Add(existing);
        }

        await _projectService.SaveScenesAsync();
        return existing.Id;
    }

    /// <summary>Removes a promise. Returns false when it was already gone.</summary>
    public async Task<bool> DeleteAsync(string sceneId, string promiseId)
    {
        var scene = FindScene(sceneId);
        if (scene?.Promises == null) return false;
        if (scene.Promises.RemoveAll(p => p.Id == promiseId) == 0) return false;
        if (scene.Promises.Count == 0) scene.Promises = null;
        await _projectService.SaveScenesAsync();
        return true;
    }

    private SceneData? FindScene(string sceneId)
        => _projectService.GetChaptersOrdered()
            .SelectMany(c => _projectService.GetScenesForChapter(c.Guid))
            .FirstOrDefault(s => s.Id == sceneId);
}
