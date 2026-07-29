using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Scene labels. A scene has held a label colour since long before anything
/// read it: a bare hex string with no name and no surface that showed it.
/// </summary>
public sealed class SceneLabelRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly SceneLabelRpc _rpc;
    private readonly string _chapter;
    private readonly string _scene;

    public SceneLabelRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-label-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "LabelNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _chapter = _workspace.Projects.CreateChapterAsync("One").GetAwaiter().GetResult().Guid;
        _scene = _workspace.Projects.CreateSceneAsync(_chapter, "A").GetAwaiter().GetResult().Id;
        _rpc = new SceneLabelRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task Set_RoundTripsAndDropsWhatCannotLabelAnything()
    {
        var saved = await _rpc.SetAsync([
            new SceneLabelDto(" beta ", "  Needs a beta read  ", " #ff0000 "),
            new SceneLabelDto("  ", "no key", "#00ff00"),
            new SceneLabelDto("nameless", "   ", "#00ff00"),
            new SceneLabelDto("BETA", "duplicate key", "#0000ff"),
            new SceneLabelDto("plain", "No colour given", null)
        ]);

        Assert.Equal(["beta", "plain"], saved.Select(l => l.Key));
        Assert.Equal("Needs a beta read", saved[0].Label);
        Assert.Equal("#ff0000", saved[0].Color);
        // A label with no colour still has to paint something.
        Assert.Equal("#8b8b8b", saved[1].Color);
    }

    [Fact]
    public async Task Set_WithoutABook_Throws()
    {
        var bare = new SceneLabelRpc(new Workspace(Path.Combine(_root, "settings2")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => bare.SetAsync([]));
    }

    [Fact]
    public async Task SetScene_PutsALabelOnAScene_AndTheDtoCarriesItsColour()
    {
        await _rpc.SetAsync([new SceneLabelDto("beta", "Needs a beta read", "#ff0000")]);

        var result = await _rpc.SetSceneLabelAsync(_scene, "beta");

        var scene = result.State.Chapters.Single().Scenes.Single();
        Assert.Equal("#ff0000", scene.LabelColor);
    }

    [Fact]
    public async Task SetScene_ALabelTheBookDoesNotHaveIsNoLabel()
    {
        await _rpc.SetAsync([new SceneLabelDto("beta", "Needs a beta read", "#ff0000")]);
        await _rpc.SetSceneLabelAsync(_scene, "beta");

        // Painting nothing reads as no label rather than as a mistake.
        var result = await _rpc.SetSceneLabelAsync(_scene, "rhubarb");

        Assert.Null(result.State.Chapters.Single().Scenes.Single().LabelColor);
    }

    [Fact]
    public async Task SetScene_ClearingIt()
    {
        await _rpc.SetAsync([new SceneLabelDto("beta", "Needs a beta read", "#ff0000")]);
        await _rpc.SetSceneLabelAsync(_scene, "beta");

        var result = await _rpc.SetSceneLabelAsync(_scene, null);

        Assert.Null(result.State.Chapters.Single().Scenes.Single().LabelColor);
    }

    [Fact]
    public async Task SetScene_UnknownScene_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.SetSceneLabelAsync("no-such-scene", null));
    }

    [Fact]
    public async Task RemovingALabelTakesItOffTheScenesCarryingIt()
    {
        await _rpc.SetAsync([new SceneLabelDto("beta", "Needs a beta read", "#ff0000")]);
        await _rpc.SetSceneLabelAsync(_scene, "beta");

        await _rpc.SetAsync([]);

        // A key whose label is gone would colour nothing while still travelling
        // with the project.
        Assert.Null(_workspace.Projects.GetScenesForChapter(_chapter).Single().LabelKey);
    }

    [Fact]
    public async Task ARawColourFromAnOlderProjectStillPaints()
    {
        var scene = _workspace.Projects.GetScenesForChapter(_chapter).Single();
        scene.LabelColor = "#123456";
        await _workspace.Projects.SaveScenesAsync();

        var state = _workspace.BuildState();

        Assert.Equal("#123456", state.Chapters.Single().Scenes.Single().LabelColor);
    }
}
