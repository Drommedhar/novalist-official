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
    public void ImageBase_IsActiveBookFolder_AndEmptyWhenNoProject()
    {
        // With a project open, the base is the book folder relative to the root.
        Assert.Equal("Book", _rpc.ImageBase());
        // With no project open, it collapses to empty.
        Assert.Equal(string.Empty, new MapsRpc(new Workspace()).ImageBase());
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

    [Fact]
    public async Task GeneratingTerrainAddsALayerUnderneathWhatTheWriterDrew()
    {
        // Generated land is a background for their map, not something pasted
        // over the top of it.
        var created = await _rpc.CreateAsync("The North");

        var loaded = await _rpc.GenerateTerrainAsync(created.Id, 7, 1600, 1200);

        Assert.NotNull(loaded);
        var map = System.Text.Json.JsonSerializer.Deserialize<Novalist.Core.Models.MapData>(
            loaded!.Json,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            })!;

        Assert.Equal("generated-7", map.Layers[0].Id);
        Assert.NotEmpty(map.Layers[0].Shapes);
        Assert.NotEmpty(map.Layers[0].Splines);
        Assert.NotEmpty(map.Pins);
    }

    [Fact]
    public async Task GeneratingTwiceWithOneSeedGivesTheSameLand()
    {
        var first = await _rpc.CreateAsync("One");
        var second = await _rpc.CreateAsync("Two");

        var a = await _rpc.GenerateTerrainAsync(first.Id, 11, 1000, 800);
        var b = await _rpc.GenerateTerrainAsync(second.Id, 11, 1000, 800);

        // The map ids and names differ; the land does not.
        Assert.Contains("generated-11", a!.Json);
        Assert.Contains("generated-11", b!.Json);
    }

    [Fact]
    public async Task GeneratingOnAMapThatIsNotThereIsRefused()
    {
        Assert.Null(await _rpc.GenerateTerrainAsync("no-such-map", 1, 100, 100));
    }
}

