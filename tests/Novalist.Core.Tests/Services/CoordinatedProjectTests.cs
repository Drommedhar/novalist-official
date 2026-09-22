using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public sealed class CoordinatedProjectTests
{
    [Fact]
    public async Task ProjectScenesCodexAndHistoryUseTheHostsCoordinatorAndReopen()
    {
        using var dir = new TempDir();
        var coordinator = new RecordingFileCoordinator();
        var files = new CoordinatedFileService(coordinator);
        var projects = new ProjectService(files);
        await projects.CreateProjectAsync(dir.Path, "Cloud project", "Book");
        var chapter = await projects.CreateChapterAsync("Chapter");
        var scene = await projects.CreateSceneAsync(chapter.Guid, "Scene");
        await projects.WriteSceneContentAsync(chapter, scene, "<p>Synced prose</p>");
        await projects.RenameChapterAsync(chapter.Guid, "Renamed chapter");
        var entity = new CharacterData { Id = "mira", Name = "Mira", EyeColor = "green" };
        var entities = new EntityService(projects);
        await entities.SaveCharacterAsync(entity);
        entity.EyeColor = "brown";
        await entities.SaveCharacterAsync(entity);
        await new ExposeService(projects).SaveAsync("<p>Synopsis</p>");
        var sourceImage = dir.Combine("source.png");
        File.WriteAllBytes(sourceImage, [137, 80, 78, 71]);
        var image = await entities.ImportImageAsync(sourceImage);
        Assert.Equal([image], entities.GetProjectImages());
        Assert.Contains(coordinator.Accesses, a => a.Operation == "read" && a.Path.EndsWith("Images"));

        var history = new EntityHistory(projects);
        var revision = Assert.Single(await history.ListAsync(entity.Id));
        Assert.Contains("green", await history.ReadAsync(entity.Id, revision.Id));
        Assert.Contains(coordinator.Accesses, a => a.Operation == "write" && a.Path.EndsWith("project.json"));
        Assert.Contains(coordinator.Accesses, a => a.Operation == "write" && a.Path.EndsWith("scenes.json"));
        Assert.Contains(coordinator.Accesses, a => a.Operation == "write" && a.Path.EndsWith("mira.json"));
        Assert.Contains(coordinator.Accesses, a => a.Operation == "write" && a.Path.EndsWith(scene.FileName));
        Assert.Contains(coordinator.Accesses, a => a.Operation == "write" && a.Path.EndsWith(revision.Id + ".json"));
        Assert.Contains(coordinator.Accesses, a => a.Operation == "move" && a.Path.Contains("Chapter"));

        var root = projects.ProjectRoot!;
        projects.CloseProject();
        coordinator.Accesses.Clear();
        var reopened = new ProjectService(files);
        await reopened.LoadProjectAsync(root);
        var loadedChapter = Assert.Single(reopened.ActiveBook!.Chapters);
        var loadedScene = Assert.Single(reopened.ScenesManifest!.Chapters[loadedChapter.Guid]);
        Assert.Equal("<p>Synced prose</p>", await reopened.ReadSceneContentAsync(loadedChapter, loadedScene));
        Assert.Equal("brown", Assert.Single(await new EntityService(reopened).LoadCharactersAsync()).EyeColor);
        Assert.Equal("<p>Synopsis</p>", (await new ExposeService(reopened).GetAsync()).Html);
        Assert.Contains(coordinator.Accesses, a => a.Operation == "read" && a.Path.EndsWith("project.json"));
        Assert.Contains(coordinator.Accesses, a => a.Operation == "read" && a.Path.EndsWith("mira.json"));
        reopened.CloseProject();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnUnavailableCodexEntryFailsTheLoadInsteadOfDisappearing(bool worldBible)
    {
        using var dir = new TempDir();
        var coordinator = new RecordingFileCoordinator();
        var projects = new ProjectService(new CoordinatedFileService(coordinator));
        await projects.CreateProjectAsync(dir.Path, "Cloud project", "Book");
        var entities = new EntityService(projects);
        await entities.SaveCharacterAsync(new CharacterData { Id = "mira", Name = "Mira", IsWorldBible = worldBible });
        coordinator.BeforeRead = path =>
        {
            if (path.EndsWith("mira.json")) throw new IOException("Cloud download unavailable");
        };

        await Assert.ThrowsAsync<IOException>(() => entities.LoadCharactersAsync());
        projects.CloseProject();
    }

    [Fact]
    public async Task FailedHistoryDownloadPreventsOverwritingTheExistingEntry()
    {
        using var dir = new TempDir();
        var coordinator = new RecordingFileCoordinator();
        var projects = new ProjectService(new CoordinatedFileService(coordinator));
        await projects.CreateProjectAsync(dir.Path, "Cloud project", "Book");
        var entities = new EntityService(projects);
        var entity = new CharacterData { Id = "mira", Name = "Before" };
        await entities.SaveCharacterAsync(entity);
        var entryPath = coordinator.Accesses.Last(a => a.Operation == "write" && a.Path.EndsWith("mira.json")).Path;
        var before = File.ReadAllText(entryPath);
        coordinator.BeforeRead = path =>
        {
            if (path == entryPath) throw new IOException("Download unavailable");
        };
        entity.Name = "After";

        await Assert.ThrowsAsync<IOException>(() => entities.SaveCharacterAsync(entity));

        Assert.Equal(before, File.ReadAllText(entryPath));
        projects.CloseProject();
    }
}
