using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Sending chapters and scenes between the drafts of a book.
///
/// A draft owns its own chapter tree and its own files, so until now content
/// crossed between two of them one scene at a time through the compare dialog,
/// or not at all. What matters here is that a scene keeps its identity on the
/// way over - drafts are clones of one another, so the same scene exists on
/// both sides under one id, and a transfer that minted a new one would leave
/// two copies of the same scene that no longer compare.
/// </summary>
public class DraftTransferServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FileService _files = new();
    private readonly ProjectService _projects;
    private readonly DraftTransferService _sut;

    public DraftTransferServiceTests()
    {
        _projects = new ProjectService(_files);
        _sut = new DraftTransferService(_projects, _files);
    }

    public void Dispose() => _dir.Dispose();

    /// <summary>A book with one chapter of two scenes, and an empty second draft.</summary>
    private async Task<(string First, string Second, ChapterData Chapter, SceneData A, SceneData B)>
        TwoDraftsAsync()
    {
        await _projects.CreateProjectAsync(_dir.Path, "Transfer", "Book");
        var chapter = await _projects.CreateChapterAsync("The letter");
        var a = await _projects.CreateSceneAsync(chapter.Guid, "The kitchen");
        var b = await _projects.CreateSceneAsync(chapter.Guid, "After the rain");
        await _projects.WriteSceneContentAsync(chapter, a, "<p>She read it twice.</p>");
        await _projects.WriteSceneContentAsync(chapter, b, "<p>The gutters ran.</p>");

        var first = _projects.ActiveBook!.ActiveDraftId;
        var second = await _projects.CreateDraftAsync("Beta cut");
        return (first, second.Id, chapter, a, b);
    }

    [Fact]
    public async Task ReadStructure_ListsChaptersAndScenesInOrder()
    {
        var (first, _, chapter, a, b) = await TwoDraftsAsync();

        var structure = await _sut.ReadStructureAsync(first);

        Assert.NotNull(structure);
        Assert.Equal("Draft 1", structure!.Name);
        var only = Assert.Single(structure.Chapters);
        Assert.Equal(chapter.Guid, only.Guid);
        Assert.Equal("The letter", only.Title);
        Assert.Equal([a.Id, b.Id], only.Scenes.Select(s => s.Id));
    }

    [Fact]
    public async Task ReadStructure_UnknownDraft_IsNull()
    {
        await TwoDraftsAsync();

        Assert.Null(await _sut.ReadStructureAsync("draft-nobody"));
    }

    [Fact]
    public async Task ReadStructure_NoProject_IsNull()
    {
        Assert.Null(await _sut.ReadStructureAsync("draft-1"));
    }

    [Fact]
    public async Task Transfer_WholeChapter_ArrivesWithItsScenesAndProse()
    {
        var (first, second, chapter, a, b) = await TwoDraftsAsync();

        var result = await _sut.TransferAsync(first, second, [chapter.Guid], [], move: false);

        Assert.Equal(1, result.Chapters);
        Assert.Equal(2, result.Scenes);
        Assert.Equal(0, result.Replaced);
        Assert.False(result.Moved);

        var landed = await _sut.ReadStructureAsync(second);
        var arrived = Assert.Single(landed!.Chapters);
        Assert.Equal(chapter.Guid, arrived.Guid);
        // The ids crossed unchanged, which is what keeps the two drafts
        // comparable afterwards.
        Assert.Equal([a.Id, b.Id], arrived.Scenes.Select(s => s.Id));

        await _projects.SwitchDraftAsync(second);
        var here = _projects.GetChaptersOrdered().Single();
        var scene = _projects.GetScenesForChapter(here.Guid).Single(s => s.Id == a.Id);
        Assert.Contains("She read it twice.", await _projects.ReadSceneContentAsync(here, scene));
    }

    [Fact]
    public async Task Transfer_SingleScene_TakesItsChapterWithIt()
    {
        var (first, second, chapter, _, b) = await TwoDraftsAsync();

        var result = await _sut.TransferAsync(first, second, [], [b.Id], move: false);

        Assert.Equal(1, result.Chapters);
        Assert.Equal(1, result.Scenes);

        var landed = await _sut.ReadStructureAsync(second);
        var arrived = Assert.Single(landed!.Chapters);
        Assert.Equal(chapter.Guid, arrived.Guid);
        // Only the scene that was asked for.
        var scene = Assert.Single(arrived.Scenes);
        Assert.Equal(b.Id, scene.Id);
    }

    [Fact]
    public async Task Transfer_SceneTheTargetAlreadyHas_RewritesItRatherThanDuplicating()
    {
        await _projects.CreateProjectAsync(_dir.Path, "Transfer", "Book");
        var chapter = await _projects.CreateChapterAsync("One");
        var scene = await _projects.CreateSceneAsync(chapter.Guid, "Arrival");
        await _projects.WriteSceneContentAsync(chapter, scene, "<p>First try.</p>");

        var first = _projects.ActiveBook!.ActiveDraftId;
        var clone = await _projects.CreateDraftAsync("Rewrite", cloneFromDraftId: first);

        // Rewrite the scene in the clone, then send it back over the original.
        await _projects.SwitchDraftAsync(clone.Id);
        var there = _projects.GetChaptersOrdered().Single();
        var thatScene = _projects.GetScenesForChapter(there.Guid).Single();
        await _projects.WriteSceneContentAsync(there, thatScene, "<p>Second try, better.</p>");

        var result = await _sut.TransferAsync(clone.Id, first, [chapter.Guid], [], move: false);

        Assert.Equal(0, result.Chapters);
        Assert.Equal(1, result.Scenes);
        Assert.Equal(1, result.Replaced);

        await _projects.SwitchDraftAsync(first);
        var back = _projects.GetChaptersOrdered().Single();
        var one = Assert.Single(_projects.GetScenesForChapter(back.Guid));
        Assert.Equal(scene.Id, one.Id);
        Assert.Contains("Second try, better.", await _projects.ReadSceneContentAsync(back, one));
    }

    [Fact]
    public async Task Transfer_Move_TakesTheChapterOutOfTheSource()
    {
        var (first, second, chapter, _, _) = await TwoDraftsAsync();

        var result = await _sut.TransferAsync(first, second, [chapter.Guid], [], move: true);

        Assert.True(result.Moved);
        Assert.Equal(2, result.Scenes);

        // Gone from the draft it came from, chapter and all, because nothing of
        // it was left behind.
        var source = await _sut.ReadStructureAsync(first);
        Assert.Empty(source!.Chapters);
        Assert.Empty(_projects.GetChaptersOrdered());

        var target = await _sut.ReadStructureAsync(second);
        Assert.Single(target!.Chapters);
    }

    [Fact]
    public async Task Transfer_MoveOneScene_LeavesTheChapterAndItsOtherScene()
    {
        var (first, second, _, a, b) = await TwoDraftsAsync();

        await _sut.TransferAsync(first, second, [], [a.Id], move: true);

        var source = await _sut.ReadStructureAsync(first);
        var stayed = Assert.Single(source!.Chapters);
        var remaining = Assert.Single(stayed.Scenes);
        Assert.Equal(b.Id, remaining.Id);
    }

    [Fact]
    public async Task Transfer_NothingSelected_MovesNothingAndLeavesBothDraftsAlone()
    {
        var (first, second, _, _, _) = await TwoDraftsAsync();

        var result = await _sut.TransferAsync(first, second, [], [], move: true);

        Assert.Equal(0, result.Scenes);
        Assert.Single((await _sut.ReadStructureAsync(first))!.Chapters);
        Assert.Empty((await _sut.ReadStructureAsync(second))!.Chapters);
    }

    [Fact]
    public async Task Transfer_ToItself_DoesNothing()
    {
        var (first, _, chapter, _, _) = await TwoDraftsAsync();

        var result = await _sut.TransferAsync(first, first, [chapter.Guid], [], move: true);

        Assert.Equal(0, result.Scenes);
        Assert.Single(_projects.GetChaptersOrdered());
    }

    [Fact]
    public async Task Transfer_UnknownDraft_DoesNothing()
    {
        var (first, _, chapter, _, _) = await TwoDraftsAsync();

        var result = await _sut.TransferAsync(first, "draft-nobody", [chapter.Guid], [], move: false);

        Assert.Equal(0, result.Scenes);
    }

    [Fact]
    public async Task Transfer_NoProject_DoesNothing()
    {
        var result = await _sut.TransferAsync("a", "b", ["c"], [], move: false);

        Assert.Equal(0, result.Scenes);
        Assert.Equal(0, result.Chapters);
    }

    [Fact]
    public async Task Transfer_ChapterWhoseSceneFileIsMissing_CrossesAsAnEmptyScene()
    {
        var (first, second, chapter, a, _) = await TwoDraftsAsync();
        File.Delete(_projects.GetSceneFilePath(chapter, a));

        var result = await _sut.TransferAsync(first, second, [chapter.Guid], [], move: false);

        Assert.Equal(2, result.Scenes);
        await _projects.SwitchDraftAsync(second);
        var here = _projects.GetChaptersOrdered().Single();
        var landed = _projects.GetScenesForChapter(here.Guid).Single(s => s.Id == a.Id);
        Assert.Equal(string.Empty, await _projects.ReadSceneContentAsync(here, landed));
    }

    [Fact]
    public async Task Transfer_TargetFolderNameTaken_LandsUnderAFolderOfItsOwn()
    {
        var (first, second, chapter, _, _) = await TwoDraftsAsync();

        // A chapter of the same name but a different identity, already in the
        // target, holding the very folder name the arriving one would take.
        // Folder names keep the number they were born with, so deleting the
        // chapter between them leaves a third-numbered folder in second place.
        await _projects.SwitchDraftAsync(second);
        await _projects.CreateChapterAsync("Before");
        var spare = await _projects.CreateChapterAsync("Between");
        var twin = await _projects.CreateChapterAsync("The letter");
        Assert.Equal("03 - The letter", twin.FolderName);
        await _projects.DeleteChapterAsync(spare.Guid);
        await _projects.SwitchDraftAsync(first);

        await _sut.TransferAsync(first, second, [chapter.Guid], [], move: false);

        await _projects.SwitchDraftAsync(second);
        var chapters = _projects.GetChaptersOrdered();
        Assert.Equal(3, chapters.Count);
        var landed = chapters.Single(c => c.Guid == chapter.Guid);
        Assert.NotEqual(twin.FolderName, landed.FolderName);
        Assert.True(Directory.Exists(_projects.GetChapterFolderPath(landed)));
    }

    [Fact]
    public async Task Transfer_TargetChapterWithAClashingSceneFileName_KeepsBothFiles()
    {
        await _projects.CreateProjectAsync(_dir.Path, "Transfer", "Book");
        var chapter = await _projects.CreateChapterAsync("The letter");
        var a = await _projects.CreateSceneAsync(chapter.Guid, "The kitchen");
        var first = _projects.ActiveBook!.ActiveDraftId;
        var clone = await _projects.CreateDraftAsync("Beta cut", cloneFromDraftId: first);

        // In the clone that scene is cut and another written in its place, so
        // the chapter is the same chapter and its one scene has taken the file
        // name the arriving scene was born with.
        await _projects.SwitchDraftAsync(clone.Id);
        await _projects.DeleteSceneAsync(chapter.Guid, a.Id);
        var theirs = await _projects.CreateSceneAsync(chapter.Guid, "Their own");
        Assert.Equal(a.FileName, theirs.FileName);
        await _projects.SwitchDraftAsync(first);

        await _sut.TransferAsync(first, clone.Id, [], [a.Id], move: false);

        await _projects.SwitchDraftAsync(clone.Id);
        var scenes = _projects.GetScenesForChapter(chapter.Guid);
        Assert.Contains(scenes, s => s.Id == a.Id);
        Assert.Contains(scenes, s => s.Id == theirs.Id);
        Assert.Equal(scenes.Select(s => s.FileName).Distinct().Count(), scenes.Count);
    }

    [Fact]
    public async Task Transfer_DraftFolderWithUnreadableJson_ReadsAsEmptyRatherThanThrowing()
    {
        var (first, second, chapter, _, _) = await TwoDraftsAsync();
        var book = _projects.ActiveBook!;
        var target = book.Drafts.Single(d => d.Id == second);
        var draftJson = Path.Combine(
            _projects.ActiveBookRoot!, "Drafts", target.FolderName, "draft.json");
        await File.WriteAllTextAsync(draftJson, "{ not json at all");

        var result = await _sut.TransferAsync(first, second, [chapter.Guid], [], move: false);

        // The hand-edited draft read as empty, so the chapter arrived as new.
        Assert.Equal(1, result.Chapters);
        Assert.Equal(2, result.Scenes);
    }
}
