using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class ProjectCreationSafetyTests
{
    private sealed class RacingFiles : InMemoryFileService
    {
        public bool Armed { get; set; }
        public bool CompetitorCreatesDirectory { get; init; }
        public override Task MoveDirectoryAsync(string oldPath, string newPath)
        {
            if (!Armed) return base.MoveDirectoryAsync(oldPath, newPath);
            Armed = false;
            if (CompetitorCreatesDirectory) Dirs.Add(newPath);
            throw new IOException("Publication interrupted");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BookReservationRetriesOnlyWhenAnotherWriterClaimedTheFolder(bool claimed)
    {
        var files = new RacingFiles { CompetitorCreatesDirectory = claimed };
        var projects = new ProjectService(files);
        await projects.CreateProjectAsync("/parent", "Project", "Book");
        files.Armed = true;

        if (claimed)
            Assert.Equal("Race-2", (await projects.CreateBookAsync("Race")).FolderName);
        else
            await Assert.ThrowsAsync<IOException>(() => projects.CreateBookAsync("Race"));

        Assert.DoesNotContain(files.Dirs, path => path.Contains(".novalist-create-", StringComparison.Ordinal));
        Assert.Equal(claimed ? 2 : 1, projects.CurrentProject!.Books.Count);
    }

    [Theory]
    [InlineData("Project")]
    [InlineData(" Project... ")]
    public async Task ExistingProjectIsNeverReplaced(string requestedName)
    {
        using var dir = new TempDir();
        var projects = new ProjectService(new FileService());
        await projects.CreateProjectAsync(dir.Path, "Project", "Book");
        var chapter = await projects.CreateChapterAsync("Opening");
        var scene = await projects.CreateSceneAsync(chapter.Guid, "Scene");
        scene.Notes = "Keep these notes";
        await projects.SaveScenesAsync();
        var original = projects.CurrentProject;
        var before = Directory.GetFiles(projects.ProjectRoot!, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);

        await Assert.ThrowsAsync<IOException>(() => projects.CreateProjectAsync(dir.Path, requestedName, "New book"));

        Assert.Same(original, projects.CurrentProject);
        foreach (var (path, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetDirectories(dir.Path, ".novalist-create-*"));
    }

    [Fact]
    public async Task ExistingEmptyDirectoryAndFileAreNotClaimed()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Combine("Empty"));
        File.WriteAllText(dir.Combine("File"), "existing");
        var projects = new ProjectService(new FileService());

        await Assert.ThrowsAsync<IOException>(() => projects.CreateProjectAsync(dir.Path, "Empty", "Book"));
        await Assert.ThrowsAsync<IOException>(() => projects.CreateProjectAsync(dir.Path, "File", "Book"));

        Assert.Empty(Directory.GetFileSystemEntries(dir.Combine("Empty")));
        Assert.Equal("existing", File.ReadAllText(dir.Combine("File")));
        Assert.Null(projects.CurrentProject);
    }

    [Theory]
    [InlineData("...", "Book")]
    [InlineData("Project", "...")]
    [InlineData("", "Book")]
    public async Task InvalidNamesDoNotCreateAnyProjectFiles(string projectName, string bookName)
    {
        using var dir = new TempDir();
        var projects = new ProjectService(new FileService());
        await Assert.ThrowsAsync<ArgumentException>(() => projects.CreateProjectAsync(dir.Path, projectName, bookName));
        Assert.Empty(Directory.GetFileSystemEntries(dir.Path));
        Assert.Null(projects.CurrentProject);
    }

    [Fact]
    public async Task DuplicateBookNamesHaveIndependentProseAcrossRestart()
    {
        using var dir = new TempDir();
        var projects = new ProjectService(new FileService());
        await projects.CreateProjectAsync(dir.Path, "Project", "Book");
        var originalId = projects.ActiveBook!.Id;
        var chapter = await projects.CreateChapterAsync("Original chapter");
        var scene = await projects.CreateSceneAsync(chapter.Guid, "Original scene");
        await projects.WriteSceneContentAsync(chapter, scene, "<p>Original prose</p>");
        var duplicate = await projects.CreateBookAsync("Book");
        Assert.Equal("Book-2", duplicate.FolderName);
        await projects.SwitchBookAsync(duplicate.Id);
        Assert.Empty(projects.GetChaptersOrdered());
        var secondChapter = await projects.CreateChapterAsync("Second chapter");
        var secondScene = await projects.CreateSceneAsync(secondChapter.Guid, "Second scene");
        await projects.WriteSceneContentAsync(secondChapter, secondScene, "<p>Second prose</p>");

        var reopened = new ProjectService(new FileService());
        await reopened.LoadProjectAsync(projects.ProjectRoot!, originalId);
        Assert.Equal("<p>Original prose</p>", await reopened.ReadSceneContentAsync(chapter, scene));
        await reopened.SwitchBookAsync(duplicate.Id);
        Assert.Equal("<p>Second prose</p>", await reopened.ReadSceneContentAsync(secondChapter, secondScene));
    }

    [Theory]
    [InlineData("World Bible")]
    [InlineData(".novalist")]
    public async Task BookStorageDoesNotShareProjectInfrastructure(string name)
    {
        using var dir = new TempDir();
        var projects = new ProjectService(new FileService());
        await projects.CreateProjectAsync(dir.Path, "Project", name);
        Assert.Equal(name + "-Book", projects.ActiveBook!.FolderName);
        var next = await projects.CreateBookAsync(name);
        Assert.Equal(name + "-2", next.FolderName);
    }

    [Fact]
    public async Task BookStorageAvoidsUntrackedDirectoriesAndFiles()
    {
        using var dir = new TempDir();
        var projects = new ProjectService(new FileService());
        await projects.CreateProjectAsync(dir.Path, "Project", "Book");
        Directory.CreateDirectory(Path.Combine(projects.ProjectRoot!, "Novel"));
        File.WriteAllText(Path.Combine(projects.ProjectRoot!, "Novel-2"), "keep");
        var book = await projects.CreateBookAsync("Novel");
        Assert.Equal("Novel-3", book.FolderName);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(projects.ProjectRoot!, "Novel-2")));
    }
}
