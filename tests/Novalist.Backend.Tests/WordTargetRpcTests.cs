using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>Word targets from the RPC surface the binder and Outliner call.</summary>
public sealed class WordTargetRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly WordTargetRpc _rpc;
    private readonly string _chapter;
    private readonly string _scene;

    public WordTargetRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-target-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "TargetNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        var chapter = _workspace.Projects.CreateChapterAsync("One").GetAwaiter().GetResult();
        chapter.Act = "Act One";
        _chapter = chapter.Guid;
        _scene = _workspace.Projects.CreateSceneAsync(_chapter, "A").GetAwaiter().GetResult().Id;
        _rpc = new WordTargetRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void AProjectWithNoTargetsReportsNoRows()
    {
        Assert.Empty(_rpc.All());
    }

    [Fact]
    public async Task ASceneTargetAppearsAtEveryLevelItRollsUpTo()
    {
        var rows = await _rpc.SetSceneAsync(_chapter, _scene, 1000);

        Assert.Equal(["act", "chapter", "scene"], rows.Select(r => r.Kind));
        Assert.All(rows, r => Assert.Equal(1000, r.Target));
        // Only the scene stated it; the rest inherited.
        Assert.Single(rows, r => r.Explicit);
    }

    [Fact]
    public async Task AChapterTargetOverridesWhatItsScenesAddUpTo()
    {
        await _rpc.SetSceneAsync(_chapter, _scene, 1000);

        var rows = await _rpc.SetChapterAsync(_chapter, 5000);

        var chapter = rows.Single(r => r.Kind == "chapter");
        Assert.Equal(5000, chapter.Target);
        Assert.True(chapter.Explicit);
    }

    [Fact]
    public async Task AnActTargetIsSetByName()
    {
        var rows = await _rpc.SetActAsync("Act One", 40000);

        var act = rows.Single(r => r.Kind == "act");
        Assert.Equal(40000, act.Target);
        Assert.True(act.Explicit);
    }

    [Fact]
    public async Task ClearingATargetRemovesItsRow()
    {
        await _rpc.SetSceneAsync(_chapter, _scene, 1000);

        var rows = await _rpc.SetSceneAsync(_chapter, _scene, null);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task RemainingAndOverrunAreReportedForTheUi()
    {
        var scene = _workspace.Projects.GetScenesForChapter(_chapter).Single();
        scene.WordCount = 1200;

        var rows = await _rpc.SetSceneAsync(_chapter, _scene, 1000);

        var row = rows.Single(r => r.Kind == "scene");
        Assert.Equal(0, row.Remaining);
        Assert.Equal(200, row.Overrun);
    }
}
