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

    // ── A thread as more than a row of ticks ──

    [Fact]
    public async Task PlotlineDetail_CarriesImportanceCastAndSteps()
    {
        var rpc = new PlotRpc(_workspace);
        var created = await rpc.CreatePlotlineAsync("The debt");
        var id = created.Plotlines.Single().Id;

        // Everything starts a subplot: promoting a thread nobody promoted is
        // the worse mistake of the two.
        Assert.Equal("Subplot", created.Plotlines.Single().Importance);

        var grid = await rpc.SetPlotlineDetailAsync(
            id,
            importance: "main",
            castIds: ["c1", "c2", "c1"],
            steps:
            [
                new PlotlineStepDto("", "  She borrows it  ", null, true, 0),
                new PlotlineStepDto("", "She cannot pay", null, false, 1),
                new PlotlineStepDto("", "   ", null, false, 2)
            ],
            color: " #ff0000 ",
            description: "  Money and what it costs  ");

        var row = grid.Plotlines.Single();
        Assert.Equal("Main", row.Importance);
        // The same id twice is one cast member.
        Assert.Equal(["c1", "c2"], row.CastIds);
        // A step with nothing in it is not a step.
        Assert.Equal(2, row.Steps.Count);
        Assert.Equal("She borrows it", row.Steps[0].Text);
        Assert.Equal("#ff0000", row.Color);
        Assert.Equal("Money and what it costs", row.Description);

        // The number that answers "does this thread ever resolve" - the
        // commonest developmental note there is.
        Assert.Equal(1, row.UnresolvedSteps);
    }

    [Fact]
    public async Task PlotlineDetail_NonsenseFallsBackAndOmissionsChangeNothing()
    {
        var rpc = new PlotRpc(_workspace);
        var id = (await rpc.CreatePlotlineAsync("A")).Plotlines.Single().Id;
        await rpc.SetPlotlineDetailAsync(id, importance: "main", castIds: ["c1"]);

        // An unknown importance is a subplot, not the spine.
        var nonsense = await rpc.SetPlotlineDetailAsync(id, importance: "rhubarb");
        Assert.Equal("Subplot", nonsense.Plotlines.Single().Importance);
        // And a call that names nothing leaves the cast alone.
        Assert.Equal(["c1"], nonsense.Plotlines.Single().CastIds);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => rpc.SetPlotlineDetailAsync("no-such-id", importance: "main"));
    }

    [Fact]
    public async Task PlotlineColours_ReachTheBinder()
    {
        var rpc = new PlotRpc(_workspace);
        var id = (await rpc.CreatePlotlineAsync("The debt")).Plotlines.Single().Id;
        await rpc.SetPlotlineDetailAsync(id, color: "#ff0000");

        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        scene.PlotlineIds = [id];
        await _workspace.Projects.SaveScenesAsync();

        // A plotline has carried a colour since the Plot Grid shipped and it
        // never left that view, so which threads a scene serves was invisible
        // everywhere the writer actually is.
        var state = _workspace.BuildState();
        var row = state.Chapters
            .Single(c => c.Guid == chapter.Guid).Scenes
            .Single(sc => sc.Id == scene.Id);
        Assert.Equal(["#ff0000"], row.PlotlineColors);
    }

    [Fact]
    public async Task PlotlineColours_AreEmptyForASceneOnNoThread()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("Threadless");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");

        var row = _workspace.BuildState().Chapters
            .Single(c => c.Guid == chapter.Guid).Scenes
            .Single(sc => sc.Id == scene.Id);

        Assert.Empty(row.PlotlineColors);
    }


    [Fact]
    public async Task APlotThreadKeepsWhatItSaidBeforeThroughTheSavePath()
    {
        // Through the RPC on purpose: the detail save edits the very object the
        // book holds, so a version taken inside the service would compare a
        // thread with itself and record nothing.
        await _rpc.CreatePlotlineAsync("The crossing");
        var id = _rpc.GetGrid().Plotlines[0].Id;
        await _rpc.SetPlotlineDetailAsync(id, description: "She has to get over the river.");

        await _rpc.SetPlotlineDetailAsync(id, description: "Replaced by mistake.");

        var history = _rpc.PlotlineHistory(id);
        Assert.NotEmpty(history);

        var grid = await _rpc.RestorePlotlineRevisionAsync(id, history[0].Id);
        Assert.Equal(
            "She has to get over the river.",
            grid.Plotlines.First(p => p.Id == id).Description);
    }

    [Fact]
    public async Task ARestoreOfAThreadRevisionThatIsGoneIsRefused()
    {
        await _rpc.CreatePlotlineAsync("The crossing");
        var id = _rpc.GetGrid().Plotlines[0].Id;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.RestorePlotlineRevisionAsync(id, "no-such-revision"));
    }

    [Fact]
    public async Task RenamingAThreadKeepsTheNameItHad()
    {
        await _rpc.CreatePlotlineAsync("The crossing");
        var id = _rpc.GetGrid().Plotlines[0].Id;

        await _rpc.RenamePlotlineAsync(id, "The river");

        var history = _rpc.PlotlineHistory(id);
        Assert.NotEmpty(history);
        var grid = await _rpc.RestorePlotlineRevisionAsync(id, history[0].Id);
        Assert.Equal("The crossing", grid.Plotlines.First(p => p.Id == id).Name);
    }
}

