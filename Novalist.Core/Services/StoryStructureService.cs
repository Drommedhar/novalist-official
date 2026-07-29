using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>
/// One beat of the chosen structure, and where the manuscript actually puts it.
///
/// <paramref name="ActualPercent"/> is -1 when no scene is bound, which is not
/// the same as a beat at 0% — a hole in the structure and a beat right at the
/// opening are very different things to tell a writer.
/// </summary>
public sealed record StructureBeatStatus(
    string Key,
    string Title,
    string Description,
    int TargetPercent,
    string? SceneId,
    string? SceneTitle,
    string? ChapterGuid,
    int ActualPercent)
{
    /// <summary>Whether a scene claims this beat at all.</summary>
    public bool IsFilled => SceneId != null;

    /// <summary>How far off the structure's own position it sits, in
    /// percentage points. Zero when unfilled or when the template says nothing
    /// about where the beat belongs.</summary>
    public int DriftPercent
        => !IsFilled || TargetPercent <= 0 ? 0 : ActualPercent - TargetPercent;
}

/// <summary>
/// Binds a story structure to the manuscript.
///
/// Applying a template used to append undated timeline events that by design
/// never touched a chapter or a scene, so the structure and the book had no
/// relationship at all. A beat now points at the scene that fulfils it, which
/// makes two questions answerable: which beats are still holes, and whether the
/// midpoint actually lands in the middle.
/// </summary>
public sealed class StoryStructureService
{
    private readonly IProjectService _projectService;

    public StoryStructureService(IProjectService projectService)
    {
        _projectService = projectService;
    }

    /// <summary>The structure this book is written against, or null for none.</summary>
    public StoryStructureTemplate? ActiveTemplate()
    {
        var id = _projectService.ActiveBook?.StructureTemplateId ?? string.Empty;
        return id.Length == 0 ? null : StoryStructureTemplates.GetById(id);
    }

    /// <summary>Chooses the structure. An unknown id clears it rather than
    /// leaving the book pointing at something that does not exist.</summary>
    public async Task SetTemplateAsync(string? templateId)
    {
        var book = _projectService.ActiveBook;
        if (book == null) return;

        var id = (templateId ?? string.Empty).Trim();
        book.StructureTemplateId = StoryStructureTemplates.GetById(id) != null ? id : string.Empty;
        await _projectService.SaveProjectAsync();
    }

    /// <summary>Binds a scene to a beat, or clears its binding with a null key.
    /// A beat can only be claimed once - binding a second scene to it releases
    /// the first, because two scenes cannot both be the midpoint.</summary>
    public async Task SetSceneBeatAsync(string chapterGuid, string sceneId, string? beatKey)
    {
        var template = ActiveTemplate();
        var scene = _projectService.GetScenesForChapter(chapterGuid)
            .FirstOrDefault(s => s.Id == sceneId);
        if (scene == null) return;

        var key = (beatKey ?? string.Empty).Trim();
        if (key.Length == 0 || template == null
            || !template.Beats.Any(b => StoryStructureBeatKeys.For(b) == key))
        {
            scene.BeatKey = null;
            await _projectService.SaveScenesAsync();
            return;
        }

        foreach (var chapter in _projectService.GetChaptersOrdered())
        {
            foreach (var other in _projectService.GetScenesForChapter(chapter.Guid))
            {
                if (other.Id != sceneId && other.BeatKey == key) other.BeatKey = null;
            }
        }

        scene.BeatKey = key;
        await _projectService.SaveScenesAsync();
    }

    /// <summary>
    /// Every beat of the chosen structure with the scene bound to it and where
    /// that scene sits in the book. Empty when no structure is chosen.
    ///
    /// Position is measured by words rather than by scene count, because that is
    /// what "the midpoint" means - a book with three long scenes and twenty
    /// short ones does not turn over at scene eleven.
    /// </summary>
    public IReadOnlyList<StructureBeatStatus> Beats()
    {
        var template = ActiveTemplate();
        if (template == null) return [];

        // Running word total before each scene, so a scene's position is where
        // it starts rather than where it ends.
        var positions = new Dictionary<string, (int Start, string Title, string ChapterGuid)>(
            StringComparer.Ordinal);
        var total = 0;
        foreach (var chapter in _projectService.GetChaptersOrdered())
        {
            foreach (var scene in _projectService.GetScenesForChapter(chapter.Guid).OrderBy(s => s.Order))
            {
                positions[scene.Id] = (total, scene.Title, chapter.Guid);
                total += scene.WordCount;
            }
        }

        var bound = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var chapter in _projectService.GetChaptersOrdered())
        {
            foreach (var scene in _projectService.GetScenesForChapter(chapter.Guid))
            {
                if (!string.IsNullOrEmpty(scene.BeatKey)) bound[scene.BeatKey] = scene.Id;
            }
        }

        return [.. template.Beats.Select(beat =>
        {
            var key = StoryStructureBeatKeys.For(beat);
            if (!bound.TryGetValue(key, out var sceneId) || !positions.TryGetValue(sceneId, out var at))
                return new StructureBeatStatus(
                    key, beat.Title, beat.Description, beat.TargetPercent, null, null, null, -1);

            // A book with no words yet has no positions to report, so every
            // bound beat sits at zero rather than dividing by nothing.
            var percent = total > 0 ? (int)Math.Round(at.Start * 100.0 / total) : 0;
            return new StructureBeatStatus(
                key, beat.Title, beat.Description, beat.TargetPercent,
                sceneId, at.Title, at.ChapterGuid, percent);
        })];
    }

    /// <summary>
    /// Creates a placeholder scene for every beat nothing is bound to, at the
    /// end of the last chapter, each already bound to its beat.
    ///
    /// Placeholders go in one chapter rather than being scattered at their
    /// target positions: guessing where a beat belongs among existing scenes
    /// would reorder a manuscript the writer did not ask to have reordered.
    /// </summary>
    public async Task<int> FillGapsAsync()
    {
        var chapter = _projectService.GetChaptersOrdered().LastOrDefault();
        if (chapter == null) return 0;

        var missing = Beats().Where(b => !b.IsFilled).ToList();
        foreach (var beat in missing)
        {
            var scene = await _projectService.CreateSceneAsync(chapter.Guid, beat.Title);
            scene.BeatKey = beat.Key;
            scene.Synopsis = beat.Description;
        }

        if (missing.Count > 0) await _projectService.SaveScenesAsync();
        return missing.Count;
    }
}
