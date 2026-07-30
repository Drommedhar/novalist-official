using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Plot grid: plotlines (rows) crossed with scenes (columns).</summary>
public sealed class PlotRpc
{
    private readonly Workspace _workspace;
    private readonly PlotlineService _plotlines;

    public PlotRpc(Workspace workspace)
    {
        _workspace = workspace;
        _plotlines = new PlotlineService(workspace.Projects);
    }

    /// <param name="rowSource">
    /// <c>plotline</c> for the plotlines, or a Codex type key - <c>character</c>,
    /// <c>location</c>, <c>item</c>, <c>lore</c>, or a custom type - for a row
    /// per entry. A grid whose rows are only plotlines cannot answer "which
    /// scenes is she in", which is the same shape of question.
    /// </param>
    [JsonRpcMethod("plot/grid")]
    public PlotGridDto GetGrid(string rowSource = "plotline")
    {
        var projects = _workspace.Projects;
        var book = projects.ActiveBook ?? throw new InvalidOperationException("No project open.");
        var manifest = projects.ScenesManifest;

        var byCodex = !string.Equals(rowSource, "plotline", StringComparison.OrdinalIgnoreCase);

        var columns = new List<PlotColumnDto>();
        foreach (var chapter in book.Chapters.OrderBy(c => c.Order))
        {
            var scenes = (manifest?.Chapters.GetValueOrDefault(chapter.Guid) ?? [])
                .Where(s => s.ArchivedAt == null)
                .OrderBy(s => s.Order);
            foreach (var scene in scenes)
            {
                columns.Add(new PlotColumnDto(
                    chapter.Guid,
                    chapter.Title,
                    scene.Id,
                    scene.Title,
                    // With Codex rows a cell says who is in the scene, which is
                    // the cast the writer recorded rather than plotline
                    // membership. The notes belong to the plotlines either way.
                    byCodex
                        ? scene.Cast?.ToArray() ?? []
                        : scene.PlotlineIds?.ToArray() ?? [],
                    byCodex ? [] : scene.PlotlineNotes ?? []));
            }
        }

        var rows = byCodex
            ? CodexRows(rowSource)
            : [.. _plotlines.GetPlotlines().Select(ToDto)];

        return new PlotGridDto(rows, columns.ToArray());
    }

    /// <summary>
    /// A row per Codex entry of one type, in name order. They carry no colour
    /// of their own, so the row takes the neutral one and the grid stays
    /// readable rather than inventing five palettes.
    /// </summary>
    private PlotlineDto[] CodexRows(string typeKey)
    {
        var entities = new EntityService(_workspace.Projects);
        var names = typeKey.ToLowerInvariant() switch
        {
            "character" => entities.LoadCharactersAsync().GetAwaiter().GetResult()
                .Select(c => (c.Id, Name: EntityResolveIndex.Compose(c.Name, c.Surname))),
            "location" => entities.LoadLocationsAsync().GetAwaiter().GetResult()
                .Select(l => (l.Id, l.Name)),
            "item" => entities.LoadItemsAsync().GetAwaiter().GetResult()
                .Select(i => (i.Id, i.Name)),
            "lore" => entities.LoadLoreAsync().GetAwaiter().GetResult()
                .Select(l => (l.Id, l.Name)),
            // A custom type that is gone - deleted since the writer last chose
            // it - is no rows rather than a broken view.
            _ => entities.GetCustomEntityTypes().Any(t =>
                    string.Equals(t.TypeKey, typeKey, StringComparison.OrdinalIgnoreCase))
                ? entities.LoadCustomEntitiesAsync(typeKey).GetAwaiter().GetResult()
                    .Select(e => (e.Id, e.Name))
                : []
        };

        return [.. names
            .OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            // Cast rows are people, not threads, so they carry no importance,
            // no steps and nobody's name but their own.
            .Select((n, index) => new PlotlineDto(
                n.Id, n.Name, "#7f7f7f", index,
                Core.Models.PlotlineImportance.Subplot.ToString(), string.Empty, [], [], 0))];
    }

    /// <summary>
    /// Puts an entry in a scene, or takes it out. The same gesture as toggling
    /// a plotline, writing to the cast the rest of the app already reads.
    /// </summary>
    [JsonRpcMethod("plot/toggleCast")]
    public async Task<PlotGridDto> ToggleCastAsync(
        string chapterGuid, string sceneId, string entityId, string rowSource)
    {
        var (_, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        var cast = scene.Cast ?? [];
        if (cast.Any(id => string.Equals(id, entityId, StringComparison.OrdinalIgnoreCase)))
            cast = [.. cast.Where(id => !string.Equals(id, entityId, StringComparison.OrdinalIgnoreCase))];
        else
            cast = [.. cast, entityId];

        scene.Cast = cast.Count > 0 ? cast : null;
        await _workspace.Projects.SaveScenesAsync();
        return GetGrid(rowSource);
    }

    [JsonRpcMethod("plot/toggle")]
    public async Task<PlotGridDto> ToggleAsync(string chapterGuid, string sceneId, string plotlineId)
    {
        await _plotlines.ToggleSceneAsync(chapterGuid, sceneId, plotlineId);
        return GetGrid();
    }

    /// <summary>Sets or clears the short note on one plot-grid cell.</summary>
    [JsonRpcMethod("plot/setCellNote")]
    public async Task<PlotGridDto> SetCellNoteAsync(
        string chapterGuid, string sceneId, string plotlineId, string? note)
    {
        await _plotlines.SetCellNoteAsync(chapterGuid, sceneId, plotlineId, note);
        return GetGrid();
    }

    [JsonRpcMethod("plot/createPlotline")]
    public async Task<PlotGridDto> CreatePlotlineAsync(string name)
    {
        await _plotlines.CreateAsync(name);
        return GetGrid();
    }

    [JsonRpcMethod("plot/renamePlotline")]
    public async Task<PlotGridDto> RenamePlotlineAsync(string plotlineId, string name)
    {
        var plotline = _plotlines.GetPlotlines().FirstOrDefault(p => p.Id == plotlineId)
            ?? throw new InvalidOperationException("Unknown plotline.");
        plotline.Name = name;
        await _plotlines.UpdateAsync(plotline);
        return GetGrid();
    }

    /// <summary>
    /// How much of the book a thread is, whose it is, and what has to happen
    /// for it to be finished.
    ///
    /// A grid of equal rows says a romance running through every chapter and a
    /// running joke are the same kind of thing, and can say which scenes a
    /// thread touches but never whether it resolves - which is the commonest
    /// developmental note there is.
    /// </summary>
    [JsonRpcMethod("plot/setPlotlineDetail")]
    public async Task<PlotGridDto> SetPlotlineDetailAsync(
        string plotlineId,
        string? importance = null,
        string[]? castIds = null,
        PlotlineStepDto[]? steps = null,
        string? color = null,
        string? description = null)
    {
        var plotline = _plotlines.GetPlotlines().FirstOrDefault(p => p.Id == plotlineId)
            ?? throw new InvalidOperationException("Unknown plotline.");

        if (importance != null)
        {
            // An unknown value reads as a subplot rather than as the spine:
            // promoting a thread nobody promoted is the worse mistake.
            plotline.Importance =
                Enum.TryParse<Core.Models.PlotlineImportance>(importance, true, out var parsed)
                    ? parsed
                    : Core.Models.PlotlineImportance.Subplot;
        }

        if (castIds != null)
        {
            plotline.CastIds = [.. castIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)];
        }

        if (steps != null)
        {
            plotline.Steps = [.. steps
                .Where(step => !string.IsNullOrWhiteSpace(step.Text))
                .Select((step, index) => new Core.Models.PlotlineStep
                {
                    Id = string.IsNullOrWhiteSpace(step.Id) ? Guid.NewGuid().ToString() : step.Id,
                    Text = step.Text.Trim(),
                    SceneId = step.SceneId ?? string.Empty,
                    Resolved = step.Resolved,
                    Order = index
                })];
        }

        if (!string.IsNullOrWhiteSpace(color)) plotline.Color = color.Trim();
        if (description != null) plotline.Description = description.Trim();

        await _plotlines.UpdateAsync(plotline);
        return GetGrid();
    }

    private static PlotlineDto ToDto(Core.Models.PlotlineData p) => new(
        p.Id, p.Name, p.Color, p.Order,
        p.Importance.ToString(),
        p.Description,
        [.. p.CastIds],
        [.. p.Steps
            .OrderBy(step => step.Order)
            .Select(step => new PlotlineStepDto(
                step.Id, step.Text, step.SceneId, step.Resolved, step.Order))],
        p.UnresolvedSteps);

    [JsonRpcMethod("plot/deletePlotline")]
    public async Task<PlotGridDto> DeletePlotlineAsync(string plotlineId)
    {
        await _plotlines.DeleteAsync(plotlineId);
        return GetGrid();
    }
}

public sealed record PlotGridDto(
    IReadOnlyList<PlotlineDto> Plotlines,
    IReadOnlyList<PlotColumnDto> Columns);

public sealed record PlotlineDto(
    string Id,
    string Name,
    string Color,
    int Order,
    /// <summary>"Main", "Subplot" or "Minor".</summary>
    string Importance,
    string Description,
    /// <summary>Codex ids this thread belongs to.</summary>
    IReadOnlyList<string> CastIds,
    IReadOnlyList<PlotlineStepDto> Steps,
    /// <summary>Steps still unresolved. Zero with no steps means nothing was
    /// planned rather than everything being done.</summary>
    int UnresolvedSteps);

/// <summary>One thing that has to happen for a thread to be finished.</summary>
public sealed record PlotlineStepDto(
    string Id, string Text, string? SceneId, bool Resolved, int Order);

public sealed record PlotColumnDto(
    string ChapterGuid,
    string ChapterTitle,
    string SceneId,
    string SceneTitle,
    IReadOnlyList<string> PlotlineIds,
    /// <summary>Per-plotline cell notes for this scene, keyed by plotline id.</summary>
    IReadOnlyDictionary<string, string> Notes);
