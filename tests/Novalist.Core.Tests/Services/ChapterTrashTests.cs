using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Deleting a chapter puts it in the trash.
///
/// It was the one structural action with no way back: a confirmed chapter
/// delete took its scenes with it and left snapshots or a backup as the only
/// recovery. Scenes already had an archive; chapters now use it.
/// </summary>
public class ChapterTrashTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProjectService _sut = new(new FileService());

    public void Dispose() => _dir.Dispose();

    private Task Create() => _sut.CreateProjectAsync(_dir.Path, "Trash", "Book");

    [Fact]
    public async Task DeleteChapter_KeepsItInTheTrashWithItsScenes()
    {
        await Create();
        var chapter = await _sut.CreateChapterAsync("One");
        var scene = await _sut.CreateSceneAsync(chapter.Guid, "Arrival");
        await _sut.WriteSceneContentAsync(chapter, scene, "<p>The bell rang once.</p>");

        await _sut.DeleteChapterAsync(chapter.Guid);

        Assert.Empty(_sut.GetChaptersOrdered());
        var trashed = Assert.Single(_sut.GetTrashedChapters());
        Assert.Equal("One", trashed.Title);
        Assert.NotNull(trashed.DeletedAt);
        // The scene went to the archive that already backs scene-level restore,
        // remembering which chapter it belonged to.
        var archived = Assert.Single(_sut.GetArchivedScenes());
        Assert.Equal(chapter.Guid, archived.OriginChapterGuid);
    }

    [Fact]
    public async Task RestoreChapter_BringsBackTheChapterAndItsProse()
    {
        await Create();
        var chapter = await _sut.CreateChapterAsync("One");
        var scene = await _sut.CreateSceneAsync(chapter.Guid, "Arrival");
        await _sut.WriteSceneContentAsync(chapter, scene, "<p>The bell rang once.</p>");
        await _sut.DeleteChapterAsync(chapter.Guid);

        Assert.True(await _sut.RestoreChapterAsync(chapter.Guid));

        var back = Assert.Single(_sut.GetChaptersOrdered());
        Assert.Equal("One", back.Title);
        Assert.Null(back.DeletedAt);
        var restored = Assert.Single(_sut.GetScenesForChapter(chapter.Guid));
        Assert.Contains("The bell rang once", await _sut.ReadSceneContentAsync(back, restored));
        Assert.Empty(_sut.GetTrashedChapters());
        Assert.Empty(_sut.GetArchivedScenes());
    }

    [Fact]
    public async Task RestoreChapter_KeepsTheScenesInOrder()
    {
        await Create();
        var chapter = await _sut.CreateChapterAsync("One");
        foreach (var title in new[] { "First", "Second", "Third" })
            await _sut.CreateSceneAsync(chapter.Guid, title);
        await _sut.DeleteChapterAsync(chapter.Guid);

        await _sut.RestoreChapterAsync(chapter.Guid);

        Assert.Equal(
            ["First", "Second", "Third"],
            _sut.GetScenesForChapter(chapter.Guid).OrderBy(s => s.Order).Select(s => s.Title));
    }

    [Fact]
    public async Task RestoreChapter_LandsAtTheEndRatherThanRenumberingTheRest()
    {
        // The chapters around it have moved on. Putting it back where it was
        // would renumber chapters the writer has since been working in.
        await Create();
        var first = await _sut.CreateChapterAsync("One");
        await _sut.CreateChapterAsync("Two");
        await _sut.DeleteChapterAsync(first.Guid);
        await _sut.CreateChapterAsync("Three");

        await _sut.RestoreChapterAsync(first.Guid);

        Assert.Equal(
            ["Two", "Three", "One"],
            _sut.GetChaptersOrdered().Select(c => c.Title));
    }

    [Fact]
    public async Task RestoreChapter_SurvivesReopeningTheProject()
    {
        await Create();
        var chapter = await _sut.CreateChapterAsync("One");
        await _sut.CreateSceneAsync(chapter.Guid, "Arrival");
        await _sut.DeleteChapterAsync(chapter.Guid);
        var root = _sut.ProjectRoot!;

        var reopened = new ProjectService(new FileService());
        await reopened.LoadProjectAsync(root);

        Assert.Single(reopened.GetTrashedChapters());
        Assert.True(await reopened.RestoreChapterAsync(chapter.Guid));
        Assert.Single(reopened.GetScenesForChapter(chapter.Guid));
    }

    [Fact]
    public async Task PurgeChapter_ErasesItAndItsScenes()
    {
        await Create();
        var chapter = await _sut.CreateChapterAsync("One");
        var scene = await _sut.CreateSceneAsync(chapter.Guid, "Arrival");
        await _sut.WriteSceneContentAsync(chapter, scene, "<p>Gone.</p>");
        await _sut.DeleteChapterAsync(chapter.Guid);
        var archivedPath = _sut.GetArchivedSceneFilePath(_sut.GetArchivedScenes()[0]);

        Assert.True(await _sut.PurgeChapterAsync(chapter.Guid));

        Assert.Empty(_sut.GetTrashedChapters());
        Assert.Empty(_sut.GetArchivedScenes());
        Assert.False(File.Exists(archivedPath));
    }

    [Fact]
    public async Task PurgeChapter_LeavesOtherChaptersArchivedScenesAlone()
    {
        await Create();
        var kept = await _sut.CreateChapterAsync("Kept");
        var keptScene = await _sut.CreateSceneAsync(kept.Guid, "Still here");
        await _sut.ArchiveSceneAsync(kept.Guid, keptScene.Id);
        var doomed = await _sut.CreateChapterAsync("Doomed");
        await _sut.CreateSceneAsync(doomed.Guid, "Going");
        await _sut.DeleteChapterAsync(doomed.Guid);

        await _sut.PurgeChapterAsync(doomed.Guid);

        Assert.Equal("Still here", Assert.Single(_sut.GetArchivedScenes()).Title);
    }

    [Fact]
    public async Task RestoreAndPurge_UnknownChapter_ReturnFalse()
    {
        await Create();
        Assert.False(await _sut.RestoreChapterAsync("not-a-chapter"));
        Assert.False(await _sut.PurgeChapterAsync("not-a-chapter"));
    }

    [Fact]
    public async Task Trash_NoProjectOpen_IsEmptyAndRefusesBoth()
    {
        var bare = new ProjectService(new FileService());
        Assert.Empty(bare.GetTrashedChapters());
        Assert.False(await bare.RestoreChapterAsync("x"));
        Assert.False(await bare.PurgeChapterAsync("x"));
    }

    [Fact]
    public async Task DeleteChapter_NewestDeletionIsListedFirst()
    {
        await Create();
        var first = await _sut.CreateChapterAsync("First out");
        var second = await _sut.CreateChapterAsync("Second out");
        await _sut.DeleteChapterAsync(first.Guid);
        await _sut.DeleteChapterAsync(second.Guid);

        Assert.Equal(
            ["Second out", "First out"],
            _sut.GetTrashedChapters().Select(c => c.Title));
    }

    [Fact]
    public async Task RestoreChapter_WithNoScenes_StillComesBack()
    {
        await Create();
        var chapter = await _sut.CreateChapterAsync("Empty");
        await _sut.DeleteChapterAsync(chapter.Guid);

        Assert.True(await _sut.RestoreChapterAsync(chapter.Guid));

        Assert.Single(_sut.GetChaptersOrdered());
        Assert.Empty(_sut.GetScenesForChapter(chapter.Guid));
    }
}
