using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Comparing two drafts without switching to either.
///
/// Cloning a draft was always one click and the clone always recorded where it
/// came from, but nothing read it back: no way to see what the rewrite changed,
/// no way to bring one scene of it across.
/// </summary>
public class DraftCompareServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FileService _files = new();
    private readonly ProjectService _projects;
    private readonly DraftCompareService _sut;

    public DraftCompareServiceTests()
    {
        _projects = new ProjectService(_files);
        _sut = new DraftCompareService(_projects, _files);
    }

    public void Dispose() => _dir.Dispose();

    /// <summary>
    /// A chapter with one scene in the first draft, then a clone of the whole
    /// draft. Returns the two draft ids and the scene.
    /// </summary>
    private async Task<(string First, string Second, string ChapterGuid, string SceneId)> TwoDrafts(
        string prose = "<p>The bell rang once.</p>")
    {
        await _projects.CreateProjectAsync(_dir.Path, "Compare", "Book");
        var chapter = await _projects.CreateChapterAsync("One");
        var scene = await _projects.CreateSceneAsync(chapter.Guid, "Arrival");
        await _projects.WriteSceneContentAsync(chapter, scene, prose);

        var first = _projects.ActiveBook!.ActiveDraftId;
        var clone = await _projects.CreateDraftAsync("Draft 2", cloneFromDraftId: first);
        return (first, clone.Id, chapter.Guid, scene.Id);
    }

    [Fact]
    public async Task Compare_IdenticalDrafts_EverySceneIsUnchanged()
    {
        var (first, second, _, _) = await TwoDrafts();

        var result = await _sut.CompareAsync(first, second);

        Assert.NotNull(result);
        var only = Assert.Single(result!.Scenes);
        Assert.Equal(DraftSceneState.Same, only.State);
        Assert.Equal("Arrival", only.Title);
        Assert.Equal("One", only.ChapterTitle);
        Assert.Equal(1, result.SameCount);
    }

    [Fact]
    public async Task Compare_RewrittenScene_ReadsAsChangedWithBothWordCounts()
    {
        var (first, second, chapterGuid, _) = await TwoDrafts();
        await _projects.SwitchDraftAsync(second);
        var chapter = _projects.GetChaptersOrdered().First(c => c.Guid == chapterGuid);
        var scene = _projects.GetScenesForChapter(chapterGuid)[0];
        await _projects.WriteSceneContentAsync(chapter, scene, "<p>The bell rang twice, then stopped.</p>");

        var result = await _sut.CompareAsync(first, second);

        var only = Assert.Single(result!.Scenes);
        Assert.Equal(DraftSceneState.Changed, only.State);
        Assert.Equal(4, only.LeftWords);
        Assert.Equal(6, only.RightWords);
        Assert.Equal(1, result.ChangedCount);
        Assert.Equal(4, result.LeftWords);
        Assert.Equal(6, result.RightWords);
    }

    [Fact]
    public async Task Compare_MarkupOnlyChange_IsNotARewrite()
    {
        // Changing formatting without moving a word is not a change to the
        // prose, and reading it as one would bury the real edits.
        var (first, second, chapterGuid, _) = await TwoDrafts();
        await _projects.SwitchDraftAsync(second);
        var chapter = _projects.GetChaptersOrdered().First(c => c.Guid == chapterGuid);
        var scene = _projects.GetScenesForChapter(chapterGuid)[0];
        await _projects.WriteSceneContentAsync(chapter, scene, "<p><em>The bell</em>  rang   once.</p>");

        var result = await _sut.CompareAsync(first, second);

        Assert.Equal(DraftSceneState.Same, Assert.Single(result!.Scenes).State);
    }

    [Fact]
    public async Task Compare_SceneWrittenAfterTheClone_ReadsAsAdded()
    {
        var (first, second, chapterGuid, _) = await TwoDrafts();
        await _projects.SwitchDraftAsync(second);
        var chapter = _projects.GetChaptersOrdered().First(c => c.Guid == chapterGuid);
        var fresh = await _projects.CreateSceneAsync(chapterGuid, "Aftermath");
        await _projects.WriteSceneContentAsync(chapter, fresh, "<p>Nobody spoke.</p>");

        var result = await _sut.CompareAsync(first, second);

        var added = Assert.Single(result!.Scenes, s => s.State == DraftSceneState.Added);
        Assert.Equal("Aftermath", added.Title);
        Assert.Equal(0, added.LeftWords);
        Assert.Equal(1, result.AddedCount);
    }

    [Fact]
    public async Task Compare_SceneCutFromTheRewrite_ReadsAsRemoved()
    {
        var (first, second, chapterGuid, sceneId) = await TwoDrafts();
        await _projects.SwitchDraftAsync(second);
        await _projects.DeleteSceneAsync(chapterGuid, sceneId);

        var result = await _sut.CompareAsync(first, second);

        var removed = Assert.Single(result!.Scenes);
        Assert.Equal(DraftSceneState.Removed, removed.State);
        Assert.Equal("Arrival", removed.Title);
        Assert.Equal(0, removed.RightWords);
        Assert.Equal(1, result.RemovedCount);
    }

    [Fact]
    public async Task Compare_RenamedScene_IsStillTheSameScene()
    {
        // Matched by id, not title. A scene that was retitled during the
        // rewrite is one changed scene, not one added and one removed.
        var (first, second, chapterGuid, _) = await TwoDrafts();
        await _projects.SwitchDraftAsync(second);
        var scene = _projects.GetScenesForChapter(chapterGuid)[0];
        scene.Title = "The bell";
        await _projects.SaveScenesAsync();

        var result = await _sut.CompareAsync(first, second);

        var only = Assert.Single(result!.Scenes);
        Assert.Equal("The bell", only.Title);
        Assert.Equal(DraftSceneState.Same, only.State);
    }

    [Fact]
    public async Task Compare_UnknownDraft_ReturnsNull()
    {
        var (first, _, _, _) = await TwoDrafts();
        Assert.Null(await _sut.CompareAsync(first, "not-a-draft"));
        Assert.Null(await _sut.CompareAsync("not-a-draft", first));
    }

    [Fact]
    public async Task Compare_NoProjectOpen_ReturnsNull()
        => Assert.Null(await new DraftCompareService(new ProjectService(_files), _files)
            .CompareAsync("a", "b"));

    [Fact]
    public async Task ReadSceneText_ReadsADraftTheWriterIsNotIn()
    {
        var (first, second, chapterGuid, sceneId) = await TwoDrafts();
        await _projects.SwitchDraftAsync(second);
        var chapter = _projects.GetChaptersOrdered().First(c => c.Guid == chapterGuid);
        var scene = _projects.GetScenesForChapter(chapterGuid)[0];
        await _projects.WriteSceneContentAsync(chapter, scene, "<p>Rewritten.</p>");

        Assert.Equal("The bell rang once.", (await _sut.ReadSceneTextAsync(first, sceneId)).Trim());
        Assert.Equal("Rewritten.", (await _sut.ReadSceneTextAsync(second, sceneId)).Trim());
    }

    [Fact]
    public async Task ReadScene_UnknownDraftOrScene_IsEmpty()
    {
        var (first, _, _, _) = await TwoDrafts();
        Assert.Equal(string.Empty, await _sut.ReadSceneTextAsync("nope", "also-nope"));
        Assert.Equal(string.Empty, await _sut.ReadSceneHtmlAsync(first, "no-such-scene"));
    }

    // ── Taking a scene across ──

    [Fact]
    public async Task TakeScene_BringsTheOtherDraftsProseIntoTheActiveOne()
    {
        var (first, second, chapterGuid, sceneId) = await TwoDrafts();
        await _projects.SwitchDraftAsync(second);
        var chapter = _projects.GetChaptersOrdered().First(c => c.Guid == chapterGuid);
        var scene = _projects.GetScenesForChapter(chapterGuid)[0];
        await _projects.WriteSceneContentAsync(chapter, scene, "<p>A version nobody liked.</p>");

        Assert.True(await _sut.TakeSceneAsync(first, sceneId));

        var now = await _projects.ReadSceneContentAsync(chapter, scene);
        Assert.Contains("The bell rang once", now);
    }

    [Fact]
    public async Task TakeScene_SnapshotsWhatItOverwrites()
    {
        // Overwriting prose is the destructive half of a merge. It has to be
        // recoverable from the scene's own history, not just from a backup.
        var snapshots = new SnapshotService(_projects, _files);
        var sut = new DraftCompareService(_projects, _files, snapshots);
        var (first, second, chapterGuid, sceneId) = await TwoDrafts();
        await _projects.SwitchDraftAsync(second);
        var chapter = _projects.GetChaptersOrdered().First(c => c.Guid == chapterGuid);
        var scene = _projects.GetScenesForChapter(chapterGuid)[0];
        await _projects.WriteSceneContentAsync(chapter, scene, "<p>About to be replaced.</p>");

        Assert.True(await sut.TakeSceneAsync(first, sceneId));

        var taken = Assert.Single(await snapshots.ListAsync(scene));
        var restored = await snapshots.LoadAsync(scene, taken.Id);
        Assert.Contains("About to be replaced", restored!.Content);
    }

    [Fact]
    public async Task TakeScene_SceneMissingFromTheActiveDraft_CreatesItInItsChapter()
    {
        var (first, second, chapterGuid, sceneId) = await TwoDrafts();
        await _projects.SwitchDraftAsync(second);
        await _projects.DeleteSceneAsync(chapterGuid, sceneId);

        Assert.True(await _sut.TakeSceneAsync(first, sceneId));

        var scene = Assert.Single(_projects.GetScenesForChapter(chapterGuid));
        Assert.Equal("Arrival", scene.Title);
        var chapter = _projects.GetChaptersOrdered().First(c => c.Guid == chapterGuid);
        Assert.Contains("The bell rang once", await _projects.ReadSceneContentAsync(chapter, scene));
    }

    [Fact]
    public async Task TakeScene_ChapterGoneFromTheActiveDraft_RefusesRatherThanInventingOne()
    {
        // Recreating a chapter the writer deleted would be a structural change
        // they did not ask for while they were asking about one scene.
        var (first, second, chapterGuid, sceneId) = await TwoDrafts();
        await _projects.SwitchDraftAsync(second);
        await _projects.DeleteChapterAsync(chapterGuid);

        Assert.False(await _sut.TakeSceneAsync(first, sceneId));
        Assert.Empty(_projects.GetChaptersOrdered());
    }

    [Fact]
    public async Task TakeScene_FromTheDraftAlreadyIn_DoesNothing()
    {
        var (first, _, _, sceneId) = await TwoDrafts();
        Assert.False(await _sut.TakeSceneAsync(first, sceneId));
    }

    [Fact]
    public async Task TakeScene_UnknownDraftOrScene_ReturnsFalse()
    {
        var (first, second, _, _) = await TwoDrafts();
        await _projects.SwitchDraftAsync(second);
        Assert.False(await _sut.TakeSceneAsync("not-a-draft", "whatever"));
        Assert.False(await _sut.TakeSceneAsync(first, "not-a-scene"));
    }

    [Fact]
    public async Task TakeScene_NoProjectOpen_ReturnsFalse()
        => Assert.False(await new DraftCompareService(new ProjectService(_files), _files)
            .TakeSceneAsync("a", "b"));

    [Fact]
    public async Task Compare_DraftFolderWithUnreadableJson_ReadsAsEmpty()
    {
        // A folder someone edited by hand should not take the comparison down.
        var (first, second, _, _) = await TwoDrafts();
        var folder = _projects.ActiveBook!.Drafts.First(d => d.Id == second).FolderName;
        var path = Path.Combine(_projects.ActiveBookRoot!, "Drafts", folder, "draft.json");
        await File.WriteAllTextAsync(path, "{ not json");

        var result = await _sut.CompareAsync(first, second);

        Assert.Equal(DraftSceneState.Removed, Assert.Single(result!.Scenes).State);
    }

    [Fact]
    public async Task Compare_DraftWithNoScenesFile_ReadsAsEmpty()
    {
        var (first, second, _, _) = await TwoDrafts();
        var folder = _projects.ActiveBook!.Drafts.First(d => d.Id == second).FolderName;
        File.Delete(Path.Combine(_projects.ActiveBookRoot!, "Drafts", folder, "scenes.json"));

        var result = await _sut.CompareAsync(first, second);

        Assert.Equal(DraftSceneState.Removed, Assert.Single(result!.Scenes).State);
    }

    [Fact]
    public async Task Compare_ChapterWithNoScenes_ContributesNothing()
    {
        var (first, second, _, _) = await TwoDrafts();
        await _projects.SwitchDraftAsync(second);
        await _projects.CreateChapterAsync("Empty");

        var result = await _sut.CompareAsync(first, second);

        Assert.Single(result!.Scenes);
    }

    [Fact]
    public async Task Compare_SceneFileMissingFromDisk_ReadsAsNoWords()
    {
        var (first, second, chapterGuid, _) = await TwoDrafts();
        var folder = _projects.ActiveBook!.Drafts.First(d => d.Id == second).FolderName;
        var chapter = _projects.GetChaptersOrdered().First(c => c.Guid == chapterGuid);
        var scene = _projects.GetScenesForChapter(chapterGuid)[0];
        File.Delete(Path.Combine(
            _projects.ActiveBookRoot!, "Drafts", folder,
            _projects.ActiveBook!.ChapterFolder, chapter.FolderName, scene.FileName));

        var result = await _sut.CompareAsync(first, second);

        var only = Assert.Single(result!.Scenes);
        Assert.Equal(DraftSceneState.Changed, only.State);
        Assert.Equal(0, only.RightWords);
    }
}
