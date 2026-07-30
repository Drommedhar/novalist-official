using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Places worth coming back to.
///
/// The favourite flag and saved lists answer "which scenes match this query".
/// A bookmark answers a different one - the paragraph where she finds out, the
/// entry I keep re-reading - and had nowhere to be recorded.
/// </summary>
public sealed class BookmarksRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly BookmarksRpc _rpc;

    public BookmarksRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-bm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "BookmarkNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new BookmarksRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static BookmarkDto Draft(
        string kind = "Scene", string label = "Where she finds out",
        string? group = null, string? anchor = null)
        => new("", kind, label, group, "chapter-1", "scene-1", null, anchor, null, 0);

    [Fact]
    public async Task ABookmarkRoundTripsAndKeepsItsAnchor()
    {
        var saved = await _rpc.SaveAsync(Draft(anchor: "  She read the letter twice.  "));

        var bookmark = Assert.Single(saved);
        Assert.Equal("Where she finds out", bookmark.Label);
        Assert.Equal("Scene", bookmark.Kind);
        // Stored as text rather than an offset: prose is edited above a mark
        // constantly and an offset drifts into an unrelated sentence.
        Assert.Equal("She read the letter twice.", bookmark.AnchorText);
        Assert.NotEmpty(bookmark.Id);
    }

    [Fact]
    public async Task SavingByIdUpdatesRatherThanDuplicating()
    {
        var first = (await _rpc.SaveAsync(Draft())).Single();

        var updated = await _rpc.SaveAsync(first with { Label = "Renamed" });

        Assert.Equal("Renamed", Assert.Single(updated).Label);
    }

    [Fact]
    public async Task ABlankLabelFallsBackToTheKind()
    {
        // A bookmark made in one keystroke still has to be findable in the list.
        var saved = await _rpc.SaveAsync(Draft(label: "   "));

        Assert.Equal("Scene", Assert.Single(saved).Label);
    }

    [Fact]
    public async Task AnUnknownKindReadsAsAScene()
        => Assert.Equal("Scene", Assert.Single(await _rpc.SaveAsync(Draft(kind: "rhubarb"))).Kind);

    [Fact]
    public async Task GroupsAreListedAndSortedWithLooseOnesLast()
    {
        await _rpc.SaveAsync(Draft(label: "A", group: "  Act two  "));
        await _rpc.SaveAsync(Draft(label: "B", group: "Act one"));
        await _rpc.SaveAsync(Draft(label: "C"));

        Assert.Equal(["Act one", "Act two"], await Task.FromResult(_rpc.Groups()));

        // Grouped first, then the loose one - a named set is a deliberate act.
        Assert.Equal(["B", "A", "C"], _rpc.List().Select(b => b.Label));
    }

    [Fact]
    public async Task DeletingOneLeavesTheRest()
    {
        var kept = (await _rpc.SaveAsync(Draft(label: "Kept"))).Single();
        var doomed = (await _rpc.SaveAsync(Draft(label: "Doomed")))
            .Single(b => b.Label == "Doomed");

        var left = await _rpc.DeleteAsync(doomed.Id);

        Assert.Equal(kept.Id, Assert.Single(left).Id);
        // Deleting something that is not there is not an error.
        Assert.Single(await _rpc.DeleteAsync("no-such-id"));
    }

    [Fact]
    public async Task BookmarksNeedAProject()
    {
        var bare = new BookmarksRpc(new Workspace(Path.Combine(_root, "no-project")));

        Assert.Empty(bare.List());
        Assert.Empty(bare.Groups());
        Assert.Empty(await bare.DeleteAsync("anything"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bare.SaveAsync(Draft()));
    }
}
