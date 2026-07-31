using Novalist.Core.Models;

namespace Novalist.Core.Services;

public sealed class PlotlineService : IPlotlineService
{
    private readonly IProjectService _projectService;

    public PlotlineService(IProjectService projectService)
    {
        _projectService = projectService;
    }

    private List<PlotlineData> EnsureList()
        => _projectService.ActiveBook?.Plotlines ?? throw new InvalidOperationException("No active book.");

    public IReadOnlyList<PlotlineData> GetPlotlines()
    {
        var list = _projectService.ActiveBook?.Plotlines;
        return list == null
            ? Array.Empty<PlotlineData>()
            : list.OrderBy(p => p.Order).ToList();
    }

    /// <summary>
    /// Colours new threads take, in turn.
    ///
    /// Every thread used to be the same blue, so a grid of coloured cells and a
    /// lane view of coloured tracks both said nothing about which thread was
    /// which - and the manual has claimed an automatically assigned colour the
    /// whole time. Chosen to stay apart at a glance and on the dark ground the
    /// grid is drawn on.
    /// </summary>
    public static readonly IReadOnlyList<string> Palette =
    [
        "#3498db", "#7bd88f", "#f9a03f", "#cba6f7", "#f38ba8",
        "#74c7ec", "#f9e2af", "#94e2d5", "#eba0ac", "#a6adc8"
    ];

    /// <param name="color">
    /// Null takes the next colour in turn. An explicit one is honoured, which is
    /// what an import or a test wants.
    /// </param>
    public async Task<PlotlineData> CreateAsync(string name, string? color = null)
    {
        var list = EnsureList();
        var plotline = new PlotlineData
        {
            Name = name,
            Color = color ?? Palette[list.Count % Palette.Count],
            Order = list.Count
        };
        list.Add(plotline);
        await _projectService.SaveProjectAsync().ConfigureAwait(false);
        return plotline;
    }

    /// <param name="previousJson">
    /// What the thread said before the caller changed it, from
    /// <see cref="Serialize"/>. Callers edit the very object this list holds,
    /// so by the time this is reached the old state is already gone - it has to
    /// be taken where the thread was still untouched. Null records nothing,
    /// which is right for a caller that built the thread from scratch.
    /// </param>
    public async Task UpdateAsync(PlotlineData plotline, string? previousJson = null)
    {
        var list = EnsureList();
        var idx = list.FindIndex(p => p.Id == plotline.Id);
        if (idx < 0) return;

        // Codex entries kept their earlier versions and threads did not, so
        // typing over a thread's description - or its steps - had no answer
        // inside the app.
        if (previousJson != null)
        {
            await new EntityHistory(_projectService)
                .RecordAsync(plotline.Id, previousJson, Serialize(plotline))
                .ConfigureAwait(false);
        }

        list[idx] = plotline;
        await _projectService.SaveProjectAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// A thread as stored. Indented, because a revision is something a person
    /// may end up reading in a file listing.
    /// </summary>
    public static string Serialize(PlotlineData plotline)
        => System.Text.Json.JsonSerializer.Serialize(
            plotline, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    /// <summary>A thread's earlier versions, newest first.</summary>
    public IReadOnlyList<EntityRevision> History(string plotlineId)
        => new EntityHistory(_projectService).List(plotlineId);

    /// <summary>
    /// Puts an earlier version of a thread back. The state being replaced is
    /// recorded by the ordinary update path, so an unwanted restore is itself
    /// undoable. False when the revision is no longer there.
    /// </summary>
    public async Task<bool> RestoreAsync(string plotlineId, string revisionId)
    {
        var stored = await new EntityHistory(_projectService)
            .ReadAsync(plotlineId, revisionId).ConfigureAwait(false);
        if (stored == null) return false;

        var restored = System.Text.Json.JsonSerializer.Deserialize<PlotlineData>(stored);
        if (restored == null) return false;
        // The id restored onto wins over whatever the file says: a revision put
        // back under a different id would duplicate the thread.
        restored.Id = plotlineId;

        // The state being replaced becomes a version of its own, so an unwanted
        // restore is undoable in the same list.
        var current = GetPlotlines().FirstOrDefault(p => p.Id == plotlineId);
        await UpdateAsync(restored, current == null ? null : Serialize(current))
            .ConfigureAwait(false);
        return true;
    }

    public async Task DeleteAsync(string plotlineId)
    {
        var list = EnsureList();
        list.RemoveAll(p => p.Id == plotlineId);

        // Drop dangling scene assignments.
        foreach (var chapter in _projectService.GetChaptersOrdered())
        {
            foreach (var scene in _projectService.GetScenesForChapter(chapter.Guid))
            {
                if (scene.PlotlineIds == null) continue;
                if (scene.PlotlineIds.RemoveAll(id => id == plotlineId) > 0)
                {
                    if (scene.PlotlineIds.Count == 0) scene.PlotlineIds = null;
                }
            }
        }

        await _projectService.SaveScenesAsync().ConfigureAwait(false);
        await _projectService.SaveProjectAsync().ConfigureAwait(false);
    }

    public async Task ReorderAsync(IReadOnlyList<string> orderedIds)
    {
        var list = EnsureList();
        for (int i = 0; i < orderedIds.Count; i++)
        {
            var p = list.FirstOrDefault(x => x.Id == orderedIds[i]);
            if (p != null) p.Order = i;
        }
        await _projectService.SaveProjectAsync().ConfigureAwait(false);
    }

    public async Task ToggleSceneAsync(string chapterGuid, string sceneId, string plotlineId)
    {
        var chapter = _projectService.GetChaptersOrdered().FirstOrDefault(c => c.Guid == chapterGuid);
        if (chapter == null) return;
        var scene = _projectService.GetScenesForChapter(chapter.Guid).FirstOrDefault(s => s.Id == sceneId);
        if (scene == null) return;

        scene.PlotlineIds ??= new List<string>();
        if (scene.PlotlineIds.Contains(plotlineId))
        {
            scene.PlotlineIds.Remove(plotlineId);
            if (scene.PlotlineIds.Count == 0) scene.PlotlineIds = null;
        }
        else
        {
            scene.PlotlineIds.Add(plotlineId);
        }

        await _projectService.SaveScenesAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Sets (or clears, with blank text) the note on one plot-grid cell — the
    /// short "what this scene does for this thread" line. Only meaningful for a
    /// scene that belongs to the plotline; removing the last note drops the whole
    /// map so scenes without notes stay clean on disk.
    /// </summary>
    public async Task SetCellNoteAsync(
        string chapterGuid, string sceneId, string plotlineId, string? note)
    {
        var chapter = _projectService.GetChaptersOrdered().FirstOrDefault(c => c.Guid == chapterGuid);
        if (chapter == null) return;
        var scene = _projectService.GetScenesForChapter(chapter.Guid).FirstOrDefault(s => s.Id == sceneId);
        if (scene == null) return;

        var text = (note ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            if (scene.PlotlineNotes == null) return;
            scene.PlotlineNotes.Remove(plotlineId);
            if (scene.PlotlineNotes.Count == 0) scene.PlotlineNotes = null;
        }
        else
        {
            scene.PlotlineNotes ??= [];
            scene.PlotlineNotes[plotlineId] = text;
        }

        await _projectService.SaveScenesAsync().ConfigureAwait(false);
    }

    public bool IsSceneInPlotline(SceneData scene, string plotlineId)
        => scene.PlotlineIds != null && scene.PlotlineIds.Contains(plotlineId);
}
