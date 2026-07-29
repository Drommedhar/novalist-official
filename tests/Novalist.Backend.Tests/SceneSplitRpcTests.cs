using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>Split and merge from the RPC the editor and binder call.</summary>
public sealed class SceneSplitRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly SceneSplitRpc _rpc;
    private readonly string _chapter;
    private readonly string _scene;

    public SceneSplitRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-split-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "SplitNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        var chapter = _workspace.Projects.CreateChapterAsync("One").GetAwaiter().GetResult();
        _chapter = chapter.Guid;
        _scene = _workspace.Projects.CreateSceneAsync(_chapter, "Arrival").GetAwaiter().GetResult().Id;
        _rpc = new SceneSplitRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task SplitReturnsTheNewSceneAndFreshStateSoTheBinderRepaints()
    {
        var result = await _rpc.SplitAsync(
            _chapter, _scene, "<p>one two</p>", "<p>three four five</p>");

        Assert.NotNull(result.SceneId);
        var scenes = result.State.Chapters.Single(c => c.Guid == _chapter).Scenes;
        Assert.Equal(2, scenes.Count);
        Assert.Equal(["Arrival", "Arrival (2)"], scenes.Select(s => s.Title));
    }

    [Fact]
    public async Task BothHalvesGetTheirOwnWordCount()
    {
        // Left stale, the binder would show the pre-split count on both.
        var result = await _rpc.SplitAsync(
            _chapter, _scene, "<p>one two</p>", "<p>three four five</p>");

        var scenes = result.State.Chapters.Single(c => c.Guid == _chapter).Scenes;
        Assert.Equal(2, scenes[0].WordCount);
        Assert.Equal(3, scenes[1].WordCount);
    }

    [Fact]
    public async Task SplittingASceneThatIsGoneReportsNothingHappened()
    {
        var result = await _rpc.SplitAsync(_chapter, "no-such-scene", "<p>a</p>", "<p>b</p>");

        Assert.Null(result.SceneId);
    }

    [Fact]
    public async Task MergeJoinsThemBackAndRecountsTheSurvivor()
    {
        var split = await _rpc.SplitAsync(
            _chapter, _scene, "<p>one two</p>", "<p>three four five</p>");

        var result = await _rpc.MergeAsync(_chapter, _scene, split.SceneId!);

        Assert.Equal(_scene, result.SceneId);
        var scene = result.State.Chapters.Single(c => c.Guid == _chapter).Scenes.Single();
        Assert.Equal(5, scene.WordCount);
    }

    [Fact]
    public async Task MergingSomethingThatIsGoneReportsNothingHappened()
    {
        var result = await _rpc.MergeAsync(_chapter, _scene, "no-such-scene");

        Assert.Null(result.SceneId);
        Assert.Single(result.State.Chapters.Single(c => c.Guid == _chapter).Scenes);
    }

    [Fact]
    public async Task ATitleCanBeGivenForTheSecondHalf()
    {
        var result = await _rpc.SplitAsync(
            _chapter, _scene, "<p>a</p>", "<p>b</p>", "The Inn");

        Assert.Contains(
            result.State.Chapters.Single(c => c.Guid == _chapter).Scenes,
            s => s.Title == "The Inn");
    }
}
