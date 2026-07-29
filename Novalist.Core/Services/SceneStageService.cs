using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>How many scenes and words sit at one stage.</summary>
public sealed record SceneStageTally(
    string Key,
    string Label,
    string Color,
    bool CountsAsWritten,
    int SceneCount,
    int WordCount);

/// <summary>
/// The stages scenes can be at, and the breakdown of a book across them.
///
/// Revision is scene-granular; the chapter status is not. A writer three
/// chapters into a revision has scenes at four different stages inside one
/// chapter, and a single chapter dot cannot say that.
/// </summary>
public sealed class SceneStageService
{
    private readonly IProjectService _projectService;

    public SceneStageService(IProjectService projectService)
    {
        _projectService = projectService;
    }

    /// <summary>The book's stages, falling back to the defaults so a project
    /// that never configured any still has something to set.</summary>
    public IReadOnlyList<SceneStage> Stages()
    {
        var configured = _projectService.ActiveBook?.SceneStages ?? [];
        return configured.Count > 0 ? configured : SceneStageDefaults.Build();
    }

    /// <summary>
    /// Replaces the stage list. Stages with a blank key or label are dropped,
    /// and a duplicate key is dropped rather than shadowing the first — two
    /// stages sharing a key would make a scene's stage ambiguous.
    /// </summary>
    public async Task<IReadOnlyList<SceneStage>> SetStagesAsync(IEnumerable<SceneStage> stages)
    {
        var book = _projectService.ActiveBook
            ?? throw new InvalidOperationException("No active book.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clean = new List<SceneStage>();
        foreach (var stage in stages)
        {
            var key = (stage.Key ?? string.Empty).Trim();
            var label = (stage.Label ?? string.Empty).Trim();
            if (key.Length == 0 || label.Length == 0) continue;
            if (!seen.Add(key)) continue;
            clean.Add(new SceneStage
            {
                Key = key,
                Label = label,
                Color = string.IsNullOrWhiteSpace(stage.Color) ? "#8b8b8b" : stage.Color.Trim(),
                CountsAsWritten = stage.CountsAsWritten
            });
        }

        book.SceneStages = clean;
        await _projectService.SaveProjectAsync();
        return Stages();
    }

    /// <summary>Sets one scene's stage. A key that names no stage clears it
    /// rather than storing a dangling reference.</summary>
    public async Task SetSceneStageAsync(string chapterGuid, string sceneId, string? stageKey)
    {
        var scene = _projectService.GetScenesForChapter(chapterGuid)
            .FirstOrDefault(s => s.Id == sceneId);
        if (scene == null) return;

        var key = (stageKey ?? string.Empty).Trim();
        scene.Stage = Stages().Any(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase))
            ? key
            : null;
        await _projectService.SaveScenesAsync();
    }

    /// <summary>
    /// Scenes and words per stage across the book, in the writer's stage order.
    /// Scenes with no stage set are reported under an empty key so they are
    /// visible as untriaged rather than folded into the first stage.
    /// </summary>
    public IReadOnlyList<SceneStageTally> Breakdown()
    {
        var stages = Stages();
        var tally = stages.ToDictionary(
            s => s.Key,
            s => (Scenes: 0, Words: 0),
            StringComparer.OrdinalIgnoreCase);
        var unset = (Scenes: 0, Words: 0);

        foreach (var chapter in _projectService.GetChaptersOrdered())
        {
            foreach (var scene in _projectService.GetScenesForChapter(chapter.Guid))
            {
                var key = scene.Stage ?? string.Empty;
                if (key.Length > 0 && tally.TryGetValue(key, out var current))
                    tally[key] = (current.Scenes + 1, current.Words + scene.WordCount);
                else
                    unset = (unset.Scenes + 1, unset.Words + scene.WordCount);
            }
        }

        var rows = stages
            .Select(s => new SceneStageTally(
                s.Key, s.Label, s.Color, s.CountsAsWritten,
                tally[s.Key].Scenes, tally[s.Key].Words))
            .ToList();

        if (unset.Scenes > 0)
            rows.Add(new SceneStageTally(
                string.Empty, string.Empty, "#8b8b8b", true, unset.Scenes, unset.Words));

        return rows;
    }

    /// <summary>
    /// Words in scenes at a stage that counts as written, plus every scene with
    /// no stage set. An untriaged scene is prose until the writer says
    /// otherwise; treating it as not-written would make the total drop the
    /// moment stages were introduced.
    /// </summary>
    public int WrittenWords()
        => Breakdown().Where(t => t.CountsAsWritten).Sum(t => t.WordCount);
}
