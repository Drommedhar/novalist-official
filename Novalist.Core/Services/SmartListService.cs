using Novalist.Core.Models;
using Novalist.Core.Utilities;

namespace Novalist.Core.Services;

public sealed class SmartListService : ISmartListService
{
    private readonly IProjectService _projectService;
    private readonly IEntityService _entityService;

    public SmartListService(IProjectService projectService, IEntityService entityService)
    {
        _projectService = projectService;
        _entityService = entityService;
    }

    public IReadOnlyList<SmartList> GetAll()
    {
        return _projectService.CurrentProject?.SmartLists ?? (IReadOnlyList<SmartList>)Array.Empty<SmartList>();
    }

    public async Task SaveAsync(SmartList list)
    {
        var project = _projectService.CurrentProject;
        if (project == null) return;

        var existing = project.SmartLists.FindIndex(l => string.Equals(l.Id, list.Id, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) project.SmartLists[existing] = list;
        else project.SmartLists.Add(list);

        await _projectService.SaveProjectAsync().ConfigureAwait(false);
    }

    public async Task DeleteAsync(string listId)
    {
        var project = _projectService.CurrentProject;
        if (project == null) return;

        project.SmartLists.RemoveAll(l => string.Equals(l.Id, listId, StringComparison.OrdinalIgnoreCase));
        await _projectService.SaveProjectAsync().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<(ChapterData Chapter, SceneData Scene)>> EvaluateAsync(SmartList list)
    {
        var result = new List<(ChapterData, SceneData)>();
        var chapters = _projectService.GetChaptersOrdered();

        // Cache characters once for POV resolution; cheap.
        var characters = await _entityService.LoadCharactersAsync().ConfigureAwait(false);

        foreach (var chapter in chapters)
        {
            if (!ChapterStatusMatches(list, chapter)) continue;

            var scenes = _projectService.GetScenesForChapter(chapter.Guid);
            foreach (var scene in scenes)
            {
                if (!await SceneMatchesAsync(list, chapter, scene, characters).ConfigureAwait(false))
                    continue;

                result.Add((chapter, scene));
            }
        }

        return result;
    }

    private static bool ChapterStatusMatches(SmartList list, ChapterData chapter)
    {
        if (string.IsNullOrEmpty(list.ChapterStatus)) return true;
        return string.Equals(list.ChapterStatus, chapter.Status.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> SceneMatchesAsync(
        SmartList list,
        ChapterData chapter,
        SceneData scene,
        IReadOnlyList<CharacterData> characters)
    {
        if (!string.IsNullOrEmpty(list.Tag))
        {
            var tags = scene.AnalysisOverrides?.Tags;
            if (tags == null || !tags.Any(t => string.Equals(t, list.Tag, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        if (!string.IsNullOrEmpty(list.PovContains))
        {
            var pov = scene.AnalysisOverrides?.Pov;
            if (string.IsNullOrEmpty(pov))
            {
                // Fall back to auto-detected POV via plain text.
                var html = await _projectService.ReadSceneContentAsync(chapter, scene).ConfigureAwait(false);
                var plain = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
                pov = PovDetector.Detect(plain, characters);
            }
            if (string.IsNullOrEmpty(pov)
                || pov.IndexOf(list.PovContains, StringComparison.OrdinalIgnoreCase) < 0)
                return false;
        }

        return true;
    }
}
