using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Conflict handling from the RPC surface the editor actually calls: a save that
/// carries the hash it read, and the resolution that follows a refusal.
/// </summary>
public sealed class SceneConflictRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly ScenesRpc _rpc;
    private readonly string _chapter;
    private readonly string _scene;

    public SceneConflictRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-conflict-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "ConflictNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();

        var chapter = _workspace.Projects.CreateChapterAsync("One").GetAwaiter().GetResult();
        _chapter = chapter.Guid;
        _scene = _workspace.Projects.CreateSceneAsync(_chapter, "A").GetAwaiter().GetResult().Id;
        _rpc = new ScenesRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    /// <summary>Writes straight to the file, standing in for the other machine's
    /// save arriving through a synced folder.</summary>
    private async Task OtherMachineWroteAsync(string html)
    {
        var (chapter, scene) = _workspace.ResolveScene(_chapter, _scene);
        await _workspace.Projects.WriteSceneContentAsync(chapter, scene, html);
    }

    [Fact]
    public async Task Read_HandsOutTheHashTheSaveWillCarryBack()
    {
        await OtherMachineWroteAsync("<p>text</p>");

        var content = await _rpc.ReadAsync(_chapter, _scene);

        Assert.NotEmpty(content.Hash);
        // Saving with it goes through, which is what makes it usable.
        var result = await _rpc.WriteAsync(_chapter, _scene, "<p>new</p>", "new", content.Hash);
        Assert.False(result.Conflicted);
    }

    [Fact]
    public async Task Write_WithNoHashKeepsWorkingForCallersWithNoEditor()
    {
        var result = await _rpc.WriteAsync(_chapter, _scene, "<p>imported</p>", "imported");

        Assert.False(result.Conflicted);
        Assert.Equal(1, result.WordCount);
    }

    [Fact]
    public async Task Write_RefusesWhenTheFileMovedUnderneath()
    {
        var stale = (await _rpc.ReadAsync(_chapter, _scene)).Hash;
        await OtherMachineWroteAsync("<p>from the other machine</p>");

        var result = await _rpc.WriteAsync(_chapter, _scene, "<p>mine</p>", "mine", stale);

        Assert.True(result.Conflicted);
        Assert.Equal("<p>from the other machine</p>", result.DiskHtml);
    }

    [Fact]
    public async Task ARefusedSaveDoesNotReportProgressThatDidNotHappen()
    {
        // Word history and the manifest must not move for a write that never
        // landed, or the dashboard credits words the writer has not saved.
        await _rpc.WriteAsync(_chapter, _scene, "<p>one two three</p>", "one two three");
        var stale = "sha1:0000000000000000000000000000000000000000";

        var result = await _rpc.WriteAsync(
            _chapter, _scene, "<p>a b c d e f</p>", "a b c d e f", stale);

        Assert.True(result.Conflicted);
        Assert.Equal(3, result.WordCount);
        Assert.Equal(
            3, _workspace.Projects.GetScenesForChapter(_chapter).Single(s => s.Id == _scene).WordCount);
    }

    [Fact]
    public async Task ResolveConflict_WritesTheChosenTextAndClearsTheClash()
    {
        var stale = (await _rpc.ReadAsync(_chapter, _scene)).Hash;
        await OtherMachineWroteAsync("<p>theirs</p>");
        await _rpc.WriteAsync(_chapter, _scene, "<p>mine</p>", "mine", stale);

        var resolved = await _rpc.ResolveConflictAsync(
            _chapter, _scene, "<p>mine</p><p>theirs</p>", "mine theirs");

        Assert.False(resolved.Conflicted);
        Assert.Equal(2, resolved.WordCount);
        // The hash it returns is the one the editor will send next time.
        var next = await _rpc.WriteAsync(_chapter, _scene, "<p>after</p>", "after", resolved.Hash);
        Assert.False(next.Conflicted);
    }

    [Fact]
    public async Task ResolveConflict_KeepsBothVersions()
    {
        await OtherMachineWroteAsync("<p>theirs</p>");

        await _rpc.ResolveConflictAsync(_chapter, _scene, "<p>merged</p>", "merged");

        var (_, scene) = _workspace.ResolveScene(_chapter, _scene);
        var snapshots = new Novalist.Core.Services.SnapshotService(
            _workspace.Projects, _workspace.FileService);
        Assert.Equal(2, (await snapshots.ListAsync(scene)).Count);
    }

    [Fact]
    public void MergeRows_LineUpTheTwoVersionsForTheDialog()
    {
        var rows = _rpc.MergeRows("<p>shared</p><p>mine</p>", "<p>shared</p><p>theirs</p>");

        Assert.Equal("equal", rows[0].State);
        Assert.Equal("changed", rows[1].State);
        Assert.Equal("mine", rows[1].Mine);
        Assert.Equal("theirs", rows[1].Theirs);
    }
}
