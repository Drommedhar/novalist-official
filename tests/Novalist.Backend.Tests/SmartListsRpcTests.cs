using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class SmartListsRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly SmartListsRpc _rpc;

    public SmartListsRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-sl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "SmartNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new SmartListsRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task SaveEvaluateUpdateDelete_FullFlow()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S1");

        var lists = await _rpc.SaveAsync(null, "Outline scenes", "Outline", " ", null);
        var list = lists.Single();
        Assert.Equal("Outline scenes", list.Name);
        Assert.Equal("Outline", list.ChapterStatus);
        Assert.Null(list.PovContains);

        var matches = await _rpc.EvaluateAsync(list.Id);
        Assert.Single(matches);
        Assert.Equal("S1", matches.Single().SceneTitle);

        var updated = await _rpc.SaveAsync(list.Id, "Final scenes", "Final", null, null);
        Assert.Equal("Final scenes", updated.Single().Name);
        Assert.Empty(await _rpc.EvaluateAsync(list.Id));

        Assert.Empty(await _rpc.DeleteAsync(list.Id));
    }

    [Fact]
    public async Task Save_RoundTripsPlotlineFilter_AndNarrowsMatches()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        var tagged = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Tagged");
        await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Untagged");
        tagged.PlotlineIds = ["p1"];
        await _workspace.Projects.SaveScenesAsync();

        var list = (await _rpc.SaveAsync(null, "Main thread", null, null, null, "p1")).Single();
        Assert.Equal("p1", list.PlotlineId);

        var matches = await _rpc.EvaluateAsync(list.Id);
        Assert.Equal("Tagged", matches.Single().SceneTitle);

        // Clearing the filter widens the query again.
        var cleared = (await _rpc.SaveAsync(list.Id, "Main thread", null, null, null, null)).Single();
        Assert.Null(cleared.PlotlineId);
        Assert.Equal(2, (await _rpc.EvaluateAsync(list.Id)).Length);
    }

    [Fact]
    public async Task Evaluate_UnknownList_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.EvaluateAsync("missing"));
    }
}
