using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>
/// Where one part of the book stands against its target.
///
/// <paramref name="Target"/> of zero means nothing was set here and nothing
/// underneath it was either, so there is no progress to show — which is not the
/// same as being at zero percent.
/// </summary>
public sealed record WordTargetProgress(
    string Kind,
    string Id,
    string Title,
    int Words,
    int Target,
    bool Explicit)
{
    /// <summary>Whether there is a target to show progress against at all.</summary>
    public bool HasTarget => Target > 0;

    /// <summary>Words still to write, never below zero — overrun is reported by
    /// <see cref="Overrun"/> rather than as a negative remainder.</summary>
    public int Remaining => Math.Max(0, Target - Words);

    /// <summary>Words past the target, or zero.</summary>
    public int Overrun => Math.Max(0, Words - Target);
}

/// <summary>
/// Word targets on scenes, chapters and acts.
///
/// A target set on a part is that writer's stated intention. A part with none
/// aggregates the targets underneath it, so putting targets on a handful of
/// scenes is enough to tell you where the chapter stands — rather than making
/// the writer restate the same number at three levels.
/// </summary>
public sealed class WordTargetService
{
    private readonly IProjectService _projectService;

    public WordTargetService(IProjectService projectService)
    {
        _projectService = projectService;
    }

    /// <summary>Progress for one scene. Never aggregates: a scene is the bottom.</summary>
    public WordTargetProgress? Scene(string chapterGuid, string sceneId)
    {
        var scene = _projectService.GetScenesForChapter(chapterGuid)
            .FirstOrDefault(s => s.Id == sceneId);
        return scene == null
            ? null
            : new WordTargetProgress(
                "scene", scene.Id, scene.Title, scene.WordCount,
                scene.WordTarget ?? 0, scene.WordTarget.HasValue);
    }

    /// <summary>
    /// Progress for a chapter. Its own target when it has one, otherwise the sum
    /// of its scenes' targets — so a chapter inherits the intention its scenes
    /// already express.
    /// </summary>
    public WordTargetProgress? Chapter(string chapterGuid)
    {
        var chapter = _projectService.GetChaptersOrdered()
            .FirstOrDefault(c => c.Guid == chapterGuid);
        if (chapter == null) return null;

        // A scene taken out of the book neither contributes its words nor its
        // target: counting a target the writer has parked would make the
        // chapter look permanently behind.
        var scenes = _projectService.GetScenesForChapter(chapterGuid)
            .Where(s => !s.Inactive)
            .ToList();
        var words = scenes.Sum(s => s.WordCount);
        var target = chapter.WordTarget ?? scenes.Sum(s => s.WordTarget ?? 0);

        return new WordTargetProgress(
            "chapter", chapter.Guid, chapter.Title, words, target, chapter.WordTarget.HasValue);
    }

    /// <summary>
    /// Progress for an act, by name. Its own target when the act carries one,
    /// otherwise the sum of what its chapters report — which itself may have
    /// come from their scenes.
    /// </summary>
    public WordTargetProgress? Act(string actName)
    {
        var name = (actName ?? string.Empty).Trim();
        var chapters = _projectService.GetChaptersOrdered()
            .Where(c => string.Equals(c.Act, name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (chapters.Count == 0) return null;

        var rows = chapters.Select(c => Chapter(c.Guid)!).ToList();
        var stored = _projectService.ActiveBook?.Acts
            .FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

        var target = stored?.WordTarget ?? rows.Sum(r => r.Target);
        return new WordTargetProgress(
            "act", name, name, rows.Sum(r => r.Words), target, stored?.WordTarget.HasValue == true);
    }

    /// <summary>Every part of the book that has a target, in reading order:
    /// act, then its chapters, then their scenes.</summary>
    public IReadOnlyList<WordTargetProgress> All()
    {
        var rows = new List<WordTargetProgress>();
        var seenActs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var chapter in _projectService.GetChaptersOrdered())
        {
            if (chapter.Act.Length > 0 && seenActs.Add(chapter.Act))
            {
                var act = Act(chapter.Act);
                if (act?.HasTarget == true) rows.Add(act);
            }

            var chapterRow = Chapter(chapter.Guid);
            if (chapterRow?.HasTarget == true) rows.Add(chapterRow);

            foreach (var scene in _projectService.GetScenesForChapter(chapter.Guid))
            {
                if (scene.WordTarget is > 0)
                    rows.Add(Scene(chapter.Guid, scene.Id)!);
            }
        }

        return rows;
    }

    /// <summary>
    /// Sets or clears a scene's target. A target of zero or less clears it,
    /// because "no target" and "a target of nothing" are the same intention and
    /// storing the second would show a permanently-complete progress bar.
    /// </summary>
    public async Task SetSceneTargetAsync(string chapterGuid, string sceneId, int? target)
    {
        var scene = _projectService.GetScenesForChapter(chapterGuid)
            .FirstOrDefault(s => s.Id == sceneId);
        if (scene == null) return;
        scene.WordTarget = Normalize(target);
        await _projectService.SaveScenesAsync();
    }

    public async Task SetChapterTargetAsync(string chapterGuid, int? target)
    {
        var chapter = _projectService.GetChaptersOrdered()
            .FirstOrDefault(c => c.Guid == chapterGuid);
        if (chapter == null) return;
        chapter.WordTarget = Normalize(target);
        await _projectService.SaveProjectAsync();
    }

    /// <summary>Sets an act's target, creating the act's metadata entry when it
    /// only existed as a name on its chapters.</summary>
    public async Task SetActTargetAsync(string actName, int? target)
    {
        var book = _projectService.ActiveBook;
        var name = (actName ?? string.Empty).Trim();
        if (book == null || name.Length == 0) return;

        var act = book.Acts.FirstOrDefault(
            a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        if (act == null)
        {
            act = new ActData { Name = name };
            book.Acts.Add(act);
        }

        act.WordTarget = Normalize(target);
        await _projectService.SaveProjectAsync();
    }

    private static int? Normalize(int? target) => target is > 0 ? target : null;
}
