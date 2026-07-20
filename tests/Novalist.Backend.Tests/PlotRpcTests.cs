using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class PlotRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly PlotRpc _rpc;
    private readonly string _chapterGuid;
    private readonly string _sceneId;

    public PlotRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-plot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "PlotNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        var chapter = _workspace.Projects.CreateChapterAsync("C").GetAwaiter().GetResult();
        var scene = _workspace.Projects.CreateSceneAsync(chapter.Guid, "S").GetAwaiter().GetResult();
        _chapterGuid = chapter.Guid;
        _sceneId = scene.Id;
        _rpc = new PlotRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task CreateToggleRenameDelete_FullFlow()
    {
        var created = await _rpc.CreatePlotlineAsync("Main Plot");
        var plotline = created.Plotlines.Single();
        Assert.Equal("Main Plot", plotline.Name);
        Assert.Single(created.Columns);
        Assert.Empty(created.Columns.Single().PlotlineIds);

        var toggledOn = await _rpc.ToggleAsync(_chapterGuid, _sceneId, plotline.Id);
        Assert.Contains(plotline.Id, toggledOn.Columns.Single().PlotlineIds);

        var renamed = await _rpc.RenamePlotlineAsync(plotline.Id, "Hauptplot");
        Assert.Equal("Hauptplot", renamed.Plotlines.Single().Name);

        var toggledOff = await _rpc.ToggleAsync(_chapterGuid, _sceneId, plotline.Id);
        Assert.Empty(toggledOff.Columns.Single().PlotlineIds);

        var deleted = await _rpc.DeletePlotlineAsync(plotline.Id);
        Assert.Empty(deleted.Plotlines);
    }

    [Fact]
    public async Task RenameUnknownPlotline_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.RenamePlotlineAsync("missing", "x"));
    }
}
