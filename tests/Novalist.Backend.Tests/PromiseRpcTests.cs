using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>Setups and payoffs from the RPC surface the Plot Grid calls.</summary>
public sealed class PromiseRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly PromiseRpc _rpc;
    private readonly string _setup;
    private readonly string _payoff;

    public PromiseRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-promise-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "PromiseNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        var chapter = _workspace.Projects.CreateChapterAsync("One").GetAwaiter().GetResult();
        _setup = _workspace.Projects.CreateSceneAsync(chapter.Guid, "The mantel")
            .GetAwaiter().GetResult().Id;
        _payoff = _workspace.Projects.CreateSceneAsync(chapter.Guid, "The shot")
            .GetAwaiter().GetResult().Id;
        _rpc = new PromiseRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task SaveReportAndDelete_FullFlow()
    {
        Assert.Empty(_rpc.Report());

        var afterAdd = await _rpc.SaveAsync(_setup, null, "the gun on the mantel", null);
        var promise = Assert.Single(afterAdd);
        Assert.Equal("Unpaid", promise.State);
        Assert.Equal("The mantel", promise.SceneTitle);

        var paid = await _rpc.SaveAsync(_setup, promise.PromiseId, promise.Label, _payoff);
        Assert.Equal("Kept", Assert.Single(paid).State);
        Assert.Equal("The shot", paid[0].PayoffSceneTitle);

        Assert.Empty(await _rpc.DeleteAsync(_setup, promise.PromiseId));
    }

    [Fact]
    public async Task APayoffEarlierInTheBookIsReportedOutOfOrder()
    {
        var promise = Assert.Single(await _rpc.SaveAsync(_payoff, null, "the gun", _setup));

        Assert.Equal("OutOfOrder", promise.State);
    }
}
