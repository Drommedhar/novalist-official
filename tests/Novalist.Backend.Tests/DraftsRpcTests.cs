using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Managing the drafts of a book over the RPC surface.
///
/// Renaming a draft existed in the project service and was reachable from
/// nothing, which is the failure the project rules describe: a tested unit
/// production could not get to. Everything here is the route.
/// </summary>
public sealed class DraftsRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly DraftsRpc _rpc;

    public DraftsRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-drafts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "DraftsNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new DraftsRpc(_workspace);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    /// <summary>One chapter of one scene, and a second, empty draft beside it.</summary>
    private async Task<(string First, string Second, string ChapterGuid, string SceneId)> TwoDraftsAsync()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Arrival");
        await _workspace.WriteSceneAsync(
            chapter.Guid, scene.Id, "<p>The bell rang once.</p>", "The bell rang once.");

        var first = _workspace.Projects.ActiveBook!.ActiveDraftId;
        var second = await _workspace.Projects.CreateDraftAsync("Beta cut");
        return (first, second.Id, chapter.Guid, scene.Id);
    }

    [Fact]
    public async Task List_CountsWhatEachDraftHoldsAndMarksTheActiveOne()
    {
        await TwoDraftsAsync();

        var rows = await _rpc.ListAsync();

        Assert.Equal(2, rows.Length);
        var active = Assert.Single(rows, r => r.IsActive);
        Assert.Equal(1, active.Chapters);
        Assert.Equal(1, active.Scenes);
        var other = Assert.Single(rows, r => !r.IsActive);
        Assert.Equal("Beta cut", other.Name);
        Assert.Equal(0, other.Scenes);
    }

    [Fact]
    public async Task List_NoProjectOpen_IsEmpty()
    {
        using var bare = new Workspace(Path.Combine(_root, "settings-2"));
        Assert.Empty(await new DraftsRpc(bare).ListAsync());
    }

    [Fact]
    public async Task Rename_ChangesTheNameTheListReportsBack()
    {
        var (first, _, _, _) = await TwoDraftsAsync();

        var rows = await _rpc.RenameAsync(first, "Zero draft");

        Assert.Equal("Zero draft", Assert.Single(rows, r => r.Id == first).Name);
    }

    [Fact]
    public async Task SetNotes_KeepsWhatTheDraftIsFor()
    {
        var (first, _, _, _) = await TwoDraftsAsync();

        var rows = await _rpc.SetNotesAsync(first, "  agent submission  ");

        Assert.Equal("agent submission", Assert.Single(rows, r => r.Id == first).Notes);

        var cleared = await _rpc.SetNotesAsync(first, "   ");
        Assert.Equal(string.Empty, Assert.Single(cleared, r => r.Id == first).Notes);
    }

    [Fact]
    public async Task Reorder_ListsThemInTheOrderAsked()
    {
        var (first, second, _, _) = await TwoDraftsAsync();

        var rows = await _rpc.ReorderAsync([second, first]);

        Assert.Equal([second, first], rows.Select(r => r.Id));
    }

    [Fact]
    public async Task Reorder_AListMissingADraft_KeepsItRatherThanDroppingIt()
    {
        var (first, second, _, _) = await TwoDraftsAsync();

        var rows = await _rpc.ReorderAsync([second]);

        Assert.Equal(2, rows.Length);
        Assert.Equal(second, rows[0].Id);
        Assert.Equal(first, rows[1].Id);
    }

    [Fact]
    public async Task Duplicate_CopiesTheContentAndRecordsWhereItCameFrom()
    {
        var (first, _, _, _) = await TwoDraftsAsync();

        var rows = await _rpc.DuplicateAsync(first, "Submission");

        var copy = Assert.Single(rows, r => r.Name == "Submission");
        Assert.Equal(first, copy.ParentDraftId);
        Assert.Equal(1, copy.Scenes);
    }

    [Fact]
    public async Task Structure_ReadsADraftTheWriterIsNotIn()
    {
        var (first, second, chapterGuid, sceneId) = await TwoDraftsAsync();
        await _workspace.Projects.SwitchDraftAsync(second);

        var structure = await _rpc.StructureAsync(first);

        Assert.NotNull(structure);
        var chapter = Assert.Single(structure!.Chapters);
        Assert.Equal(chapterGuid, chapter.Guid);
        Assert.Equal(sceneId, Assert.Single(chapter.Scenes).Id);
    }

    [Fact]
    public async Task Structure_UnknownDraft_IsNull()
    {
        await TwoDraftsAsync();

        Assert.Null(await _rpc.StructureAsync("draft-nobody"));
    }

    [Fact]
    public async Task Transfer_SendsAChapterToTheOtherDraft()
    {
        var (first, second, chapterGuid, sceneId) = await TwoDraftsAsync();

        var result = await _rpc.TransferContentAsync(first, second, [chapterGuid], [], move: false);

        Assert.Equal(1, result.Chapters);
        Assert.Equal(1, result.Scenes);
        Assert.Equal(0, result.Replaced);
        Assert.False(result.Moved);

        var landed = await _rpc.StructureAsync(second);
        Assert.Equal(sceneId, Assert.Single(Assert.Single(landed!.Chapters).Scenes).Id);
    }

    [Fact]
    public async Task Transfer_Move_LeavesTheSourceWithoutIt()
    {
        var (first, second, chapterGuid, _) = await TwoDraftsAsync();

        var result = await _rpc.TransferContentAsync(first, second, [chapterGuid], [], move: true);

        Assert.True(result.Moved);
        Assert.Empty((await _rpc.StructureAsync(first))!.Chapters);
    }
}
