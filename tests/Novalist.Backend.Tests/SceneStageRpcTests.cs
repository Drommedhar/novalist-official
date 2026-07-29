using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>Scene stages from the RPC surface the binder and Settings call.</summary>
public sealed class SceneStageRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly SceneStageRpc _rpc;
    private readonly string _chapter;
    private readonly string _scene;

    public SceneStageRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "StageNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        var chapter = _workspace.Projects.CreateChapterAsync("One").GetAwaiter().GetResult();
        _chapter = chapter.Guid;
        _scene = _workspace.Projects.CreateSceneAsync(_chapter, "A").GetAwaiter().GetResult().Id;
        _rpc = new SceneStageRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void AFreshProjectOffersTheDefaultStages()
    {
        Assert.Equal(
            ["outline", "firstDraft", "revised", "edited", "final"],
            _rpc.List().Select(s => s.Key));
    }

    [Fact]
    public async Task StagesRoundTrip()
    {
        var saved = await _rpc.SetAsync([new SceneStageDto("beta", "Beta read", "#ff0000", false)]);

        Assert.Equal("Beta read", saved.Single().Label);
        Assert.False(saved.Single().CountsAsWritten);
        Assert.Equal("beta", _rpc.List().Single().Key);
    }

    [Fact]
    public async Task SettingASceneStageReturnsFreshStateSoTheBinderRepaints()
    {
        var state = await _rpc.SetSceneStageAsync(_chapter, _scene, "revised");

        var scene = state.Chapters.Single(c => c.Guid == _chapter).Scenes.Single();
        Assert.Equal("revised", scene.Stage);
    }

    [Fact]
    public async Task ASceneWithNoStageReportsNullRatherThanTheFirstStage()
    {
        // Untriaged is not the same as outlined.
        var state = await _rpc.SetSceneStageAsync(_chapter, _scene, null);

        Assert.Null(state.Chapters.Single(c => c.Guid == _chapter).Scenes.Single().Stage);
    }

    [Fact]
    public async Task TheBreakdownReportsWhatIsAtEachStage()
    {
        await _rpc.SetSceneStageAsync(_chapter, _scene, "final");

        var final = _rpc.Breakdown().Single(t => t.Key == "final");

        Assert.Equal(1, final.SceneCount);
        Assert.True(final.CountsAsWritten);
    }

    [Fact]
    public void TheBreakdownNamesUntriagedScenesWithAnEmptyKey()
    {
        var untriaged = _rpc.Breakdown().Single(t => t.Key.Length == 0);

        Assert.Equal(1, untriaged.SceneCount);
    }
}
