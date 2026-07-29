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
    public async Task SetCellNote_RoundTripsAndClears()
    {
        var created = await _rpc.CreatePlotlineAsync("Main");
        var plotlineId = created.Plotlines.Single().Id;
        await _rpc.ToggleAsync(_chapterGuid, _sceneId, plotlineId);

        var withNote = await _rpc.SetCellNoteAsync(
            _chapterGuid, _sceneId, plotlineId, "Sets up the betrayal.");
        Assert.Equal("Sets up the betrayal.", withNote.Columns.Single().Notes[plotlineId]);

        var cleared = await _rpc.SetCellNoteAsync(_chapterGuid, _sceneId, plotlineId, "");
        Assert.Empty(cleared.Columns.Single().Notes);
    }

    [Fact]
    public async Task RenameUnknownPlotline_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.RenamePlotlineAsync("missing", "x"));
    }

    // -- Rows from the Codex --

    [Fact]
    public async Task RowsCanComeFromTheCharactersInsteadOfThePlotlines()
    {
        var entities = new Novalist.Core.Services.EntityService(_workspace.Projects);
        await entities.SaveCharacterAsync(new Novalist.Core.Models.CharacterData
        {
            Id = "mira", Name = "Mira", Surname = "Vance"
        });
        await entities.SaveCharacterAsync(new Novalist.Core.Models.CharacterData
        {
            Id = "halden", Name = "Halden"
        });

        var grid = _rpc.GetGrid("character");

        // In name order, whatever order they were written in.
        Assert.Equal(["Halden", "Mira Vance"], grid.Plotlines.Select(p => p.Name));
    }

    [Fact]
    public async Task TickingACodexCellRecordsWhoIsInTheScene()
    {
        var entities = new Novalist.Core.Services.EntityService(_workspace.Projects);
        await entities.SaveCharacterAsync(new Novalist.Core.Models.CharacterData
        {
            Id = "mira", Name = "Mira"
        });
        var column = _rpc.GetGrid("character").Columns[0];

        var grid = await _rpc.ToggleCastAsync(
            column.ChapterGuid, column.SceneId, "mira", "character");

        Assert.Contains("mira", grid.Columns[0].PlotlineIds);
        var (_, scene) = _workspace.ResolveScene(column.ChapterGuid, column.SceneId);
        Assert.Equal(["mira"], scene.Cast);

        // Ticking it again takes them back out, and leaves no empty list behind.
        grid = await _rpc.ToggleCastAsync(column.ChapterGuid, column.SceneId, "mira", "character");
        Assert.Empty(grid.Columns[0].PlotlineIds);
        Assert.Null(_workspace.ResolveScene(column.ChapterGuid, column.SceneId).scene.Cast);
    }

    [Fact]
    public async Task ACodexGridReadsTheCastRatherThanThePlotlines()
    {
        var column = _rpc.GetGrid().Columns[0];
        await _rpc.CreatePlotlineAsync("Thread");
        var plotlineId = _rpc.GetGrid().Plotlines[0].Id;
        await _rpc.ToggleAsync(column.ChapterGuid, column.SceneId, plotlineId);

        var codex = _rpc.GetGrid("character");

        // The plotline is on the scene, but a Codex grid is not asking that.
        Assert.Empty(codex.Columns[0].PlotlineIds);
        Assert.NotEmpty(_rpc.GetGrid().Columns[0].PlotlineIds);
    }

    [Fact]
    public async Task ACustomEntryTypeCanBeRowsToo()
    {
        var entities = new Novalist.Core.Services.EntityService(_workspace.Projects);
        await entities.SaveCustomEntityTypeAsync(new Novalist.Core.Models.CustomEntityTypeDefinition
        {
            TypeKey = "faction",
            DisplayName = "Factions"
        });
        await entities.SaveCustomEntityAsync(new Novalist.Core.Models.CustomEntityData
        {
            Id = "guild", Name = "The Guild", EntityTypeKey = "faction"
        });

        var grid = _rpc.GetGrid("faction");

        Assert.Equal(["The Guild"], grid.Plotlines.Select(p => p.Name));
    }

    [Theory]
    [InlineData("location")]
    [InlineData("item")]
    [InlineData("lore")]
    [InlineData("faction")]
    public void EveryEntryKindCanBeRows(string typeKey)
        // No entries of these kinds yet: an empty row list rather than a throw,
        // which is what a book that has not written any should get.
        => Assert.Empty(_rpc.GetGrid(typeKey).Plotlines);
}
