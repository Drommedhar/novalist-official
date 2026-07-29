using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>The freeform corkboard over the RPC surface.</summary>
public sealed class CorkboardRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly CorkboardRpc _rpc;

    public CorkboardRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-board-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "BoardNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new CorkboardRpc(_workspace);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private async Task<string> SceneAsync(string title = "Arrival")
    {
        var chapter = _workspace.Projects.GetChaptersOrdered().FirstOrDefault()
            ?? await _workspace.Projects.CreateChapterAsync("One");
        return (await _workspace.Projects.CreateSceneAsync(chapter.Guid, title)).Id;
    }

    [Fact]
    public async Task PlacementsStartInReadingOrder()
    {
        await SceneAsync("A");
        await SceneAsync("B");

        var placements = _rpc.Placements();

        Assert.Equal(2, placements.Length);
        Assert.Equal(0, placements[0].X);
        Assert.True(placements[1].X > placements[0].X);
    }

    [Fact]
    public async Task SetPositionSticksAndResetForgetsIt()
    {
        var sceneId = await SceneAsync();

        Assert.True(await _rpc.SetPositionAsync(sceneId, 512, 288));
        Assert.Equal(512, _rpc.Placements().Single().X);

        var afterReset = await _rpc.ResetAsync();
        Assert.Equal(0, afterReset.Single().X);
    }

    [Fact]
    public async Task SetPositionOnAnUnknownSceneReturnsFalse()
    {
        await SceneAsync();
        Assert.False(await _rpc.SetPositionAsync("not-a-scene", 1, 1));
    }
}
