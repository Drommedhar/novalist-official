using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>Covers the snapshot line-diff surface added to <see cref="SnapshotsRpc"/>.</summary>
public sealed class SnapshotsRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly SnapshotsRpc _rpc;

    public SnapshotsRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-snap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "SnapNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new SnapshotsRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task Diff_TwoSnapshots_ProducesEqualChangedAndOneSidedRows()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");

        await _workspace.WriteSceneAsync(chapter.Guid, scene.Id,
            "<p>same</p><p>alpha</p><p>beta</p>", "same alpha beta");
        await _rpc.TakeAsync(chapter.Guid, scene.Id, "A");

        await _workspace.WriteSceneAsync(chapter.Guid, scene.Id,
            "<p>same</p><p>gamma</p>", "same gamma");
        var list = await _rpc.TakeAsync(chapter.Guid, scene.Id, "B");

        var idA = list.Single(s => s.Label == "A").Id;
        var idB = list.Single(s => s.Label == "B").Id;

        var forward = await _rpc.DiffAsync(chapter.Guid, scene.Id, idA, idB);
        Assert.Contains(forward, r => r.State == "equal" && r.Left == "same");
        Assert.Contains(forward, r => r.State == "changed" && r.Left == "alpha" && r.Right == "gamma");
        Assert.Contains(forward, r => r.State == "left" && r.Left == "beta");

        // Reversing surfaces the extra line as a right-only insertion.
        var reverse = await _rpc.DiffAsync(chapter.Guid, scene.Id, idB, idA);
        Assert.Contains(reverse, r => r.State == "right" && r.Right == "beta");
    }

    [Fact]
    public async Task Diff_MissingSnapshotIds_TreatedAsEmptyContent()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        await _workspace.WriteSceneAsync(chapter.Guid, scene.Id, "<p>only line</p>", "only line");
        var list = await _rpc.TakeAsync(chapter.Guid, scene.Id, "real");
        var realId = list.Single().Id;

        // Missing left -> the real snapshot's lines are all right-only.
        var missingLeft = await _rpc.DiffAsync(chapter.Guid, scene.Id, "nope", realId);
        Assert.Contains(missingLeft, r => r.State == "right" && r.Right == "only line");
        Assert.DoesNotContain(missingLeft, r => r.State == "left");

        // Missing right -> the real snapshot's lines are all left-only.
        var missingRight = await _rpc.DiffAsync(chapter.Guid, scene.Id, realId, "nope");
        Assert.Contains(missingRight, r => r.State == "left" && r.Left == "only line");
        Assert.DoesNotContain(missingRight, r => r.State == "right");
    }
}
