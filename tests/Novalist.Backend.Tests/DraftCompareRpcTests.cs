using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>Comparing drafts over the RPC surface.</summary>
public sealed class DraftCompareRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly DraftCompareRpc _rpc;

    public DraftCompareRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-draftcmp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "CompareNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new DraftCompareRpc(_workspace);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    /// <summary>A scene in the first draft, then a clone of that draft.</summary>
    private async Task<(string First, string Second, string ChapterGuid, string SceneId)> TwoDrafts()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Arrival");
        await _workspace.WriteSceneAsync(
            chapter.Guid, scene.Id, "<p>The bell rang once.</p>", "The bell rang once.");

        var first = _workspace.Projects.ActiveBook!.ActiveDraftId;
        var clone = await _workspace.Projects.CreateDraftAsync("Draft 2", cloneFromDraftId: first);
        await _workspace.Projects.SwitchDraftAsync(clone.Id);
        return (first, clone.Id, chapter.Guid, scene.Id);
    }

    [Fact]
    public async Task Drafts_ListsEveryDraftAndMarksTheActiveOne()
    {
        var (first, second, _, _) = await TwoDrafts();

        var list = await _rpc.DraftsAsync();

        Assert.Equal(2, list.Length);
        Assert.True(list.Single(d => d.Id == second).IsActive);
        // The clone knows where it came from, which is what the dialog opens on.
        Assert.Equal(first, list.Single(d => d.Id == second).ParentDraftId);
    }

    [Fact]
    public async Task Drafts_NoProjectOpen_IsEmpty()
    {
        using var bare = new Workspace(Path.Combine(_root, "settings2"));
        Assert.Empty(await new DraftCompareRpc(bare).DraftsAsync());
    }

    [Fact]
    public async Task Compare_ReportsTheRewrittenScene()
    {
        var (first, second, chapterGuid, sceneId) = await TwoDrafts();
        await _workspace.WriteSceneAsync(
            chapterGuid, sceneId, "<p>The bell rang twice.</p>", "The bell rang twice.");

        var result = await _rpc.CompareAsync(first, second);

        Assert.NotNull(result);
        Assert.Equal(1, result!.ChangedCount);
        Assert.Equal("changed", Assert.Single(result.Scenes).State);
    }

    [Fact]
    public async Task Compare_UnknownDraft_ReturnsNull()
    {
        var (first, _, _, _) = await TwoDrafts();
        Assert.Null(await _rpc.CompareAsync(first, "nope"));
    }

    [Fact]
    public async Task Scene_ReturnsASideBySideDiff()
    {
        var (first, second, chapterGuid, sceneId) = await TwoDrafts();
        await _workspace.WriteSceneAsync(
            chapterGuid, sceneId, "<p>The bell rang twice.</p>", "The bell rang twice.");

        var rows = await _rpc.SceneAsync(first, second, sceneId);

        Assert.NotEmpty(rows);
        Assert.Contains(rows, r => r.State == "changed");
    }

    [Fact]
    public async Task Take_BringsTheOtherDraftsProseAcross()
    {
        var (first, second, chapterGuid, sceneId) = await TwoDrafts();
        await _workspace.WriteSceneAsync(
            chapterGuid, sceneId, "<p>A version nobody liked.</p>", "A version nobody liked.");

        Assert.True(await _rpc.TakeAsync(first, sceneId));

        var result = await _rpc.CompareAsync(first, second);
        Assert.Equal("same", Assert.Single(result!.Scenes).State);
    }

    [Fact]
    public async Task Take_UnknownScene_ReturnsFalse()
    {
        var (first, _, _, _) = await TwoDrafts();
        Assert.False(await _rpc.TakeAsync(first, "not-a-scene"));
    }
}
