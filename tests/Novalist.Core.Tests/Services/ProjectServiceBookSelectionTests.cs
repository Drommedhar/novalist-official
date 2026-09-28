using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class ProjectServiceBookSelectionTests
{
    [Fact]
    public async Task LoadProject_WithBookId_LoadsItsActiveDraftAndRemembersSelection()
    {
        using var dir = new TempDir();
        var projects = new ProjectService(new FileService());
        await projects.CreateProjectAsync(dir.Path, "Series", "Book One");
        var root = projects.ProjectRoot!;
        var firstBookId = projects.ActiveBook!.Id;
        await projects.CreateChapterAsync("Original draft chapter");
        var revision = await projects.CreateDraftAsync("Revision");
        await projects.SwitchDraftAsync(revision.Id);
        var chapter = await projects.CreateChapterAsync("Revised chapter");
        var scene = await projects.CreateSceneAsync(chapter.Guid, "Revised scene");
        await projects.WriteSceneContentAsync(chapter, scene, "<p>Revised prose.</p>");

        var second = await projects.CreateBookAsync("Book Two");
        await projects.SwitchBookAsync(second.Id);
        await projects.CreateChapterAsync("Second book chapter");
        projects.CloseProject();

        var loaded = await projects.LoadProjectAsync(root, firstBookId);

        Assert.Equal(firstBookId, loaded.ActiveBookId);
        Assert.Equal(firstBookId, projects.ActiveBook!.Id);
        Assert.Equal(revision.Id, projects.ActiveBook.ActiveDraftId);
        var loadedChapter = Assert.Single(projects.GetChaptersOrdered());
        Assert.Equal(chapter.Guid, loadedChapter.Guid);
        Assert.Equal("Revised chapter", loadedChapter.Title);
        var loadedScene = Assert.Single(projects.GetScenesForChapter(loadedChapter.Guid));
        Assert.Equal(scene.Id, loadedScene.Id);
        Assert.Equal("<p>Revised prose.</p>", await projects.ReadSceneContentAsync(loadedChapter, loadedScene));

        var reopened = new ProjectService(new FileService());
        await reopened.LoadProjectAsync(root);
        Assert.Equal(firstBookId, reopened.ActiveBook!.Id);
        Assert.Equal(revision.Id, reopened.ActiveBook.ActiveDraftId);
        Assert.Equal(chapter.Guid, Assert.Single(reopened.GetChaptersOrdered()).Guid);

        await reopened.LoadProjectAsync(root, second.Id);
        Assert.Equal(second.Id, reopened.ActiveBook!.Id);
        Assert.Equal("Second book chapter", Assert.Single(reopened.GetChaptersOrdered()).Title);
    }

    [Theory]
    [InlineData("missing-book")]
    [InlineData("")]
    public async Task LoadProject_WithInvalidBookId_PreservesOpenProjectAndStoredSelection(string bookId)
    {
        using var dir = new TempDir();
        var projects = new ProjectService(new FileService());
        var originalProject = await projects.CreateProjectAsync(dir.Path, "Open Project", "Open Book");
        var chapter = await projects.CreateChapterAsync("Current chapter");
        var scene = await projects.CreateSceneAsync(chapter.Guid, "Current scene");
        await projects.WriteSceneContentAsync(chapter, scene, "<p>Current prose.</p>");
        var originalRoot = projects.ProjectRoot;
        var originalBook = projects.ActiveBook;
        var originalScenes = projects.ScenesManifest;
        var originalSettings = projects.ProjectSettings;

        var other = new ProjectService(new FileService());
        await other.CreateProjectAsync(dir.Path, "Other Project", "Other Book");
        var metadataPath = Path.Combine(other.ProjectRoot!, ".novalist", "project.json");
        var metadataBefore = await File.ReadAllTextAsync(metadataPath);

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => projects.LoadProjectAsync(other.ProjectRoot!, bookId));

        Assert.Equal("bookId", error.ParamName);
        Assert.Equal(originalRoot, projects.ProjectRoot);
        Assert.Same(originalProject, projects.CurrentProject);
        Assert.Same(originalBook, projects.ActiveBook);
        Assert.Same(originalScenes, projects.ScenesManifest);
        Assert.Same(originalSettings, projects.ProjectSettings);
        Assert.Equal(chapter.Guid, Assert.Single(projects.GetChaptersOrdered()).Guid);
        Assert.Equal("<p>Current prose.</p>", await projects.ReadSceneContentAsync(chapter, scene));
        Assert.Equal(metadataBefore, await File.ReadAllTextAsync(metadataPath));
    }
}
