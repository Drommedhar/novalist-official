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

    [JsonRpcMethod("plot/grid")]
    public PlotGridDto GetGrid()
    {
        var projects = _workspace.Projects;
        var book = projects.ActiveBook ?? throw new InvalidOperationException("No project open.");
        var manifest = projects.ScenesManifest;

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
                    scene.PlotlineIds?.ToArray() ?? []));
            }
        }

        var plotlines = _plotlines.GetPlotlines()
            .Select(p => new PlotlineDto(p.Id, p.Name, p.Color, p.Order))
            .ToArray();

        return new PlotGridDto(plotlines, columns.ToArray());
    }

    [JsonRpcMethod("plot/toggle")]
    public async Task<PlotGridDto> ToggleAsync(string chapterGuid, string sceneId, string plotlineId)
    {
        await _plotlines.ToggleSceneAsync(chapterGuid, sceneId, plotlineId);
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

public sealed record PlotlineDto(string Id, string Name, string Color, int Order);

public sealed record PlotColumnDto(
    string ChapterGuid,
    string ChapterTitle,
    string SceneId,
    string SceneTitle,
    IReadOnlyList<string> PlotlineIds);
