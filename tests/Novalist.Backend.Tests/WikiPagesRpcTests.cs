using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Free-form Wiki articles over the wire.
///
/// Every article was generated from a Codex entity, so an essay on how the
/// economy works had to hang off whichever entity it least badly belonged to.
/// </summary>
public sealed class WikiPagesRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly WikiPagesRpc _rpc;

    public WikiPagesRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-pages-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "PageNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new WikiPagesRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void AProjectStartsWithNoArticles() => Assert.Empty(_rpc.List());

    [Fact]
    public async Task AnArticleIsSavedAndReadBack()
    {
        var all = await _rpc.SaveAsync(null, "How the economy works", "Salt is the currency.");

        var page = Assert.Single(all);
        Assert.Equal("How the economy works", page.Title);
        Assert.Equal("Salt is the currency.", _rpc.Get(page.Id)!.Body);
    }

    [Fact]
    public async Task AnArticleCanSitUnderAnother()
    {
        var parent = (await _rpc.SaveAsync(null, "The world"))[0];

        var all = await _rpc.SaveAsync(null, "The economy", null, parent.Id);

        Assert.Contains(all, p => p.ParentId == parent.Id);
    }

    [Fact]
    public async Task AnArticleCannotBeMovedUnderItsOwnChild()
    {
        var parent = (await _rpc.SaveAsync(null, "The world"))[0];
        var child = (await _rpc.SaveAsync(null, "The economy", null, parent.Id))
            .Single(p => p.ParentId == parent.Id);

        await _rpc.MoveAsync(parent.Id, child.Id);

        // A ring makes the tree unreachable from the top.
        Assert.Equal(string.Empty, _rpc.Get(parent.Id)!.ParentId);
    }

    [Fact]
    public async Task DeletingLiftsTheChildrenRatherThanTakingThem()
    {
        var parent = (await _rpc.SaveAsync(null, "The world"))[0];
        var child = (await _rpc.SaveAsync(null, "The economy", null, parent.Id))
            .Single(p => p.ParentId == parent.Id);

        var left = await _rpc.DeleteAsync(parent.Id);

        Assert.Equal(string.Empty, Assert.Single(left).ParentId);
        Assert.Equal(child.Id, left[0].Id);
    }

    [Fact]
    public void ReadingAnArticleThatIsGoneIsNull()
        => Assert.Null(_rpc.Get("no-such-page"));

    [Fact]
    public async Task ArticlesSurviveReopeningTheProject()
    {
        await _rpc.SaveAsync(null, "How the economy works", "Salt is the currency.");

        await _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!);

        Assert.Single(_rpc.List());
    }
}
