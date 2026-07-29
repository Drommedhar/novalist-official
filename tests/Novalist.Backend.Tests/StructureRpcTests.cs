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
}
