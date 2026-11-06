using Novalist.Core.Services;
using Novalist.Backend.Tests.TestHelpers;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class ProjectOpenPerformanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpeningProject_ReconcilesOnceAndStillFindsExternalRenames(bool rename)
    {
        using var temp = new TempDir();
        var files = new ChapterScanCounter();
        using var workspace = new Workspace(Path.Combine(temp.Path, "settings"), fileService: files);
        await workspace.Projects.CreateProjectAsync(temp.Path, "Novel", "Book");
        var chapter = await workspace.Projects.CreateChapterAsync("Chapter");
        var scene = await workspace.Projects.CreateSceneAsync(chapter.Guid, "Scene");
        var root = workspace.Projects.ProjectRoot!;
        await workspace.Projects.LoadProjectAsync(root);
        files.ChaptersRoot = Path.Combine(workspace.Projects.ActiveDraftRoot!, workspace.Projects.ActiveBook!.ChapterFolder);
        if (rename)
            File.Move(Path.Combine(files.ChaptersRoot, chapter.FolderName, scene.FileName),
                Path.Combine(files.ChaptersRoot, chapter.FolderName, "renamed.novalist"));

        var state = await workspace.OpenProjectAsync(root);

        Assert.True(state.IsLoaded);
        Assert.Equal(rename ? 2 : 1, files.ChapterScans);
        var loaded = Assert.Single(workspace.Projects.ScenesManifest!.Chapters[chapter.Guid]);
        Assert.Equal(scene.Id, loaded.Id);
        Assert.Equal(rename ? "renamed.novalist" : scene.FileName, loaded.FileName);
    }

    private sealed class ChapterScanCounter : FileService, IFileService
    {
        public string? ChaptersRoot { get; set; }
        public int ChapterScans { get; private set; }

        Task<IReadOnlyList<string>> IFileService.GetDirectoriesAsync(string directory)
        {
            if (directory == ChaptersRoot) ChapterScans++;
            return GetDirectoriesAsync(directory);
        }
    }
}
