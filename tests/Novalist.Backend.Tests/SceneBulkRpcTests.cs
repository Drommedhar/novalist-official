using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>Bulk operations over a selection of scenes, from the RPC surface the
/// multi-select bar actually calls.</summary>
public sealed class SceneBulkRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly SceneBulkRpc _rpc;
    private readonly string _chapter;
    private readonly string _sceneA;
    private readonly string _sceneB;

    public SceneBulkRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-bulk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "BulkNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();

        var chapter = _workspace.Projects.CreateChapterAsync("One").GetAwaiter().GetResult();
        _chapter = chapter.Guid;
        _sceneA = _workspace.Projects.CreateSceneAsync(_chapter, "A").GetAwaiter().GetResult().Id;
        _sceneB = _workspace.Projects.CreateSceneAsync(_chapter, "B").GetAwaiter().GetResult().Id;
        _rpc = new SceneBulkRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task Delete_RemovesTheSelectionAndReturnsFreshState()
    {
        var result = await _rpc.DeleteAsync([_sceneA, _sceneB]);

        Assert.Equal(2, result.Count);
        // The caller adopts the returned state rather than refetching it.
        Assert.Empty(result.State.Chapters.Single(c => c.Guid == _chapter).Scenes);
    }

    [Fact]
    public async Task Delete_AStaleIdDoesNotStopTheRest()
    {
        var result = await _rpc.DeleteAsync([_sceneA, "gone"]);

        Assert.Equal(1, result.Count);
        Assert.Single(result.State.Chapters.Single(c => c.Guid == _chapter).Scenes);
    }

    [Fact]
    public async Task Archive_MovesTheSelectionOutOfTheChapter()
    {
        var result = await _rpc.ArchiveAsync([_sceneA]);

        Assert.Equal(1, result.Count);
        Assert.Single(result.State.Chapters.Single(c => c.Guid == _chapter).Scenes);
        Assert.Single(_workspace.Projects.ScenesManifest!.Archived);
    }

    [Fact]
    public async Task SetTags_TagsTheWholeSelection()
    {
        var result = await _rpc.SetTagsAsync([_sceneA, _sceneB], ["night"], replace: false);

        Assert.Equal(2, result.Count);
        foreach (var scene in _workspace.Projects.GetScenesForChapter(_chapter))
            Assert.Equal(["night"], scene.AnalysisOverrides!.Tags);
    }

    [Fact]
    public async Task SetTags_ReplaceDropsTheOldOnes()
    {
        await _rpc.SetTagsAsync([_sceneA], ["night"], replace: false);

        await _rpc.SetTagsAsync([_sceneA], ["dawn"], replace: true);

        Assert.Equal(
            ["dawn"],
            _workspace.Projects.GetScenesForChapter(_chapter)
                .First(s => s.Id == _sceneA).AnalysisOverrides!.Tags);
    }

    [Fact]
    public async Task PreviewDateShift_ShowsBeforeAndAfterWithoutMovingAnything()
    {
        await _workspace.Projects.SetSceneDateAsync(_chapter, _sceneA, "2026-03-01");

        var rows = _rpc.PreviewDateShift([_sceneA], 4);

        Assert.Equal("2026-03-01", rows.Single().Before);
        Assert.Equal("2026-03-05", rows.Single().After);
        Assert.Equal("A", rows.Single().Title);
        // Still where it was: preview must not be a disguised write.
        Assert.Equal(
            "2026-03-01",
            _workspace.Projects.GetScenesForChapter(_chapter).First(s => s.Id == _sceneA).Date);
    }

    [Fact]
    public async Task ShiftDates_MovesTheSelection()
    {
        await _workspace.Projects.SetSceneDateAsync(_chapter, _sceneA, "2026-03-01");
        await _workspace.Projects.SetSceneDateRangeAsync(
            _chapter, _sceneB, new StoryDateRange { Start = "2026-03-02", End = "2026-03-03" });

        var result = await _rpc.ShiftDatesAsync([_sceneA, _sceneB], -1);

        Assert.Equal(2, result.Count);
        var scenes = _workspace.Projects.GetScenesForChapter(_chapter);
        Assert.Equal("2026-02-28", scenes.First(s => s.Id == _sceneA).Date);
        Assert.Equal("2026-03-01", scenes.First(s => s.Id == _sceneB).DateRange!.Start);
    }

    [Fact]
    public async Task MoveToChapter_CarriesTheWholeSelectionAcross()
    {
        var target = await _workspace.Projects.CreateChapterAsync("Two");

        var result = await _rpc.MoveToChapterAsync([_sceneA, _sceneB], target.Guid, 0);

        Assert.Equal(2, result.Count);
        Assert.Empty(result.State.Chapters.Single(c => c.Guid == _chapter).Scenes);
        Assert.Equal(2, result.State.Chapters.Single(c => c.Guid == target.Guid).Scenes.Count);
    }
}
