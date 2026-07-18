using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class LibraryRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly LibraryRpc _rpc;

    public LibraryRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-lib-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "LibNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new LibraryRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void Gallery_ListsImportedImages()
    {
        Assert.Empty(_rpc.ListImages());
        var source = Path.Combine(_root, "pic.png");
        File.WriteAllBytes(source, [137, 80, 78, 71]);
        new Novalist.Core.Services.EntityService(_workspace.Projects)
            .ImportImageAsync(source).GetAwaiter().GetResult();
        Assert.Single(_rpc.ListImages());
    }

    [Fact]
    public async Task Research_SaveUpdateDelete_RoundTrip()
    {
        var saved = await _rpc.SaveResearchAsync(null, "Norse winters", "Note", "Very cold.", ["climate", " "]);
        var item = saved.Single();
        Assert.Equal("Norse winters", item.Title);
        Assert.Equal(new[] { "climate" }, item.Tags);

        var updated = await _rpc.SaveResearchAsync(item.Id, "Winters", "Link", "https://example.org", []);
        Assert.Equal("Link", updated.Single().Type);

        Assert.Empty(await _rpc.DeleteResearchAsync(item.Id));
    }
}
