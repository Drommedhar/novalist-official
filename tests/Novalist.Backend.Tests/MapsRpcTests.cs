using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class MapsRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly MapsRpc _rpc;

    public MapsRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-map-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "MapNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new MapsRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task CreateLoadSaveRenameDelete_FullFlow()
    {
        Assert.Empty(_rpc.List());

        var created = await _rpc.CreateAsync("Westlande");
        Assert.Equal("Westlande", created.Name);
        Assert.Single(_rpc.List());

        var loaded = await _rpc.LoadAsync(created.Id);
        Assert.NotNull(loaded);
        Assert.Contains(created.Id, loaded.Json);

        var mutated = loaded.Json.Replace("Westlande", "Ostlande");
        await _rpc.SaveAsync(mutated);
        var reloaded = await _rpc.LoadAsync(created.Id);
        Assert.Contains("Ostlande", reloaded!.Json);

        var renamed = await _rpc.RenameAsync(created.Id, "Suedlande");
        Assert.Equal("Suedlande", renamed.Single().Name);

        Assert.Empty(await _rpc.DeleteAsync(created.Id));
        Assert.Null(await _rpc.LoadAsync(created.Id));
    }

    [Fact]
    public async Task Save_InvalidPayload_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.SaveAsync("null"));
    }
}
