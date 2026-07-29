using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>Story structure from the RPC the Timeline's Structure panel calls.</summary>
public sealed class StructureRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly StructureRpc _rpc;
    private readonly string _chapter;
    private readonly string _scene;

    public StructureRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-struct-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "StructNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        var chapter = _workspace.Projects.CreateChapterAsync("One").GetAwaiter().GetResult();
        _chapter = chapter.Guid;
        _scene = _workspace.Projects.CreateSceneAsync(_chapter, "Opening").GetAwaiter().GetResult().Id;
        _rpc = new StructureRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void EveryBundledStructureIsOffered()
    {
        var ids = _rpc.Templates().Select(t => t.Id).ToList();

        Assert.Contains("three-act", ids);
        Assert.Contains("save-the-cat", ids);
        Assert.All(_rpc.Templates(), t => Assert.True(t.BeatCount > 0));
    }

    [Fact]
    public void ABookStartsWithNoStructureAndNoBeats()
    {
        Assert.Empty(_rpc.Get());
        Assert.Empty(_rpc.Beats());
    }

    [Fact]
    public async Task ChoosingAStructureReturnsItsBeats()
    {
        var beats = await _rpc.SetAsync("three-act");

        Assert.Equal(8, beats.Length);
        Assert.Equal("three-act", _rpc.Get());
        Assert.All(beats, b => Assert.False(b.IsFilled));
    }

    [Fact]
    public async Task BindingASceneFillsTheBeatAndReportsWhereItLands()
    {
        await _rpc.SetAsync("three-act");
        var key = _rpc.Beats().First().Key;

        var beats = await _rpc.BindSceneAsync(_chapter, _scene, key);

        var beat = beats.First(b => b.Key == key);
        Assert.True(beat.IsFilled);
        Assert.Equal("Opening", beat.SceneTitle);
        Assert.Equal(_chapter, beat.ChapterGuid);
    }

    [Fact]
    public async Task FillGapsCreatesPlaceholdersAndReturnsFreshState()
    {
        await _rpc.SetAsync("three-act");

        var result = await _rpc.FillGapsAsync();

        Assert.Equal(8, result.Created);
        Assert.All(result.Beats, b => Assert.True(b.IsFilled));
        // The binder repaints from this rather than refetching.
        Assert.Equal(9, result.State.Chapters.Single(c => c.Guid == _chapter).Scenes.Count);
    }

    [Fact]
    public async Task ClearingTheStructureEmptiesTheBeats()
    {
        await _rpc.SetAsync("three-act");

        Assert.Empty(await _rpc.SetAsync(null));
    }

    // ── Structures the writer authors ──
    //
    // Four hardcoded structures with no save, import or delete path meant a
    // writer following any other method could not use this feature at all.

    private static StructureDefinitionDto Definition(
        string? id, string name, params (string Title, int Percent)[] beats)
        => new(id, name, "A method", [.. beats.Select(b =>
            new StructureBeatDefDto(null, b.Title, string.Empty, b.Percent, null))]);

    [Fact]
    public async Task SaveTemplate_AddsOneAndListsItAlongsideTheBuiltIns()
    {
        var listed = await _rpc.SaveTemplateAsync(
            Definition(null, "  My method  ", ("Opening", 5), ("  ", 50), ("Close", 200)));

        var mine = listed.Single(t => t.DisplayName == "My method");
        Assert.True(mine.IsCustom);
        // A beat with no title cannot be bound to anything; a beat outside the
        // manuscript cannot be drifted from.
        Assert.Equal(2, mine.BeatCount);
        Assert.Contains(listed, t => t.Id == "three-act" && !t.IsCustom);

        var full = _rpc.Template(mine.Id)!;
        Assert.Equal(100, full.Beats![1].TargetPercent);
    }

    [Fact]
    public async Task SaveTemplate_ANamelessStructureIsRefused()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.SaveTemplateAsync(Definition(null, "   ")));
    }

    [Fact]
    public async Task SaveTemplate_EditingReplacesRatherThanDuplicating()
    {
        var first = (await _rpc.SaveTemplateAsync(Definition(null, "Mine", ("A", 10))))
            .Single(t => t.DisplayName == "Mine");

        var listed = await _rpc.SaveTemplateAsync(
            Definition(first.Id, "Mine, revised", ("A", 10), ("B", 90)));

        Assert.Single(listed, t => t.Id == first.Id);
        Assert.Equal("Mine, revised", listed.Single(t => t.Id == first.Id).DisplayName);
    }

    [Fact]
    public async Task ACustomStructureCanBeChosenAndUsed()
    {
        var mine = (await _rpc.SaveTemplateAsync(Definition(null, "Mine", ("Opening", 5))))
            .Single(t => t.DisplayName == "Mine");

        var beats = await _rpc.SetAsync(mine.Id);

        Assert.Equal("Opening", Assert.Single(beats).Title);
        Assert.Equal(mine.Id, _rpc.Get());
    }

    [Fact]
    public async Task OverridingABuiltInIdReplacesItInTheList()
    {
        // How somebody adjusts a shipped method rather than being stuck with it.
        var listed = await _rpc.SaveTemplateAsync(Definition("three-act", "Three-Act, mine", ("A", 1)));

        var threeAct = listed.Single(t => t.Id == "three-act");
        Assert.Equal("Three-Act, mine", threeAct.DisplayName);
        Assert.True(threeAct.IsCustom);
        Assert.Equal(1, threeAct.BeatCount);
    }

    [Fact]
    public async Task DeleteTemplate_ReleasesABookWrittenAgainstIt()
    {
        var mine = (await _rpc.SaveTemplateAsync(Definition(null, "Mine", ("Opening", 5))))
            .Single(t => t.DisplayName == "Mine");
        await _rpc.SetAsync(mine.Id);

        await _rpc.DeleteTemplateAsync(mine.Id);

        // The book stops pointing at something that no longer exists.
        Assert.Equal(string.Empty, _rpc.Get());
    }

    [Fact]
    public async Task DeleteTemplate_ABuiltInOverrideFallsBackToTheShippedOne()
    {
        await _rpc.SaveTemplateAsync(Definition("three-act", "Three-Act, mine", ("A", 1)));
        await _rpc.SetAsync("three-act");

        await _rpc.DeleteTemplateAsync("three-act");

        // Still a real structure, so the book keeps using it.
        Assert.Equal("three-act", _rpc.Get());
        Assert.Equal("Three-Act", _rpc.Templates().Single(t => t.Id == "three-act").DisplayName);
    }

    [Fact]
    public async Task ExportAndImport_RoundTripThroughAFile()
    {
        var mine = (await _rpc.SaveTemplateAsync(Definition(null, "Mine", ("Opening", 5))))
            .Single(t => t.DisplayName == "Mine");
        var path = Path.Combine(_root, "structure.json");

        await _rpc.ExportTemplateAsync(mine.Id, path);
        var listed = await _rpc.ImportTemplateAsync(path);

        // Importing must never silently replace one already in use, so the
        // clashing id becomes a new one.
        var copies = listed.Where(t => t.DisplayName == "Mine").ToList();
        Assert.Equal(2, copies.Count);
        Assert.NotEqual(copies[0].Id, copies[1].Id);
    }

    [Fact]
    public async Task Import_RefusesSomethingThatIsNotAStructure()
    {
        var path = Path.Combine(_root, "nonsense.json");
        await File.WriteAllTextAsync(path, "{ not json at all");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.ImportTemplateAsync(path));

        await File.WriteAllTextAsync(path, "{ \"id\": \"x\" }");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.ImportTemplateAsync(path));

        // A path that is not a file at all fails the same readable way.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.ImportTemplateAsync(_root));
    }

    [Fact]
    public async Task ExportAndTemplate_UnknownIdsSaySo()
    {
        Assert.Null(_rpc.Template("nope"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.ExportTemplateAsync("nope", Path.Combine(_root, "x.json")));
    }
}
