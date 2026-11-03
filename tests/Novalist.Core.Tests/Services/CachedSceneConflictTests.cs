using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public sealed class CachedSceneConflictTests
{
    [Fact]
    public async Task GuardRefreshesRenamedDocumentIdentityBeforeCheckingForExternalChanges()
    {
        using var temp = new TempDir();
        var files = new CachedAliasFiles();
        var projects = new ProjectService(files);
        await projects.CreateProjectAsync(temp.Path, "Novel", "Book");
        var chapter = await projects.CreateChapterAsync("Chapter");
        var scene = await projects.CreateSceneAsync(chapter.Guid, "Scene");
        await projects.WriteSceneContentAsync(chapter, scene, "<p>Original</p>");
        var guard = new SceneConflictGuard(projects);
        var expected = await guard.DiskHashAsync(chapter, scene);
        var path = projects.GetSceneFilePath(chapter, scene);
        var moved = path + ".moved";
        File.Move(path, moved);
        await projects.WriteSceneContentAsync(chapter, scene, "<p>External replacement</p>");
        files.CachedPath = path;
        files.CachedTarget = moved;

        var result = await guard.SaveAsync(chapter, scene, "<p>My edit</p>", expected);

        Assert.True(result.Conflicted);
        Assert.Contains("External replacement", await File.ReadAllTextAsync(path));
        Assert.Contains("Original", await File.ReadAllTextAsync(moved));
    }

    private sealed class CachedAliasFiles : FileService, IFileService, IFileMetadataCache
    {
        public string? CachedPath { get; set; }
        public string? CachedTarget { get; set; }
        Task<string> IFileService.ReadTextAsync(string path)
            => ReadTextAsync(path == CachedPath ? CachedTarget ?? path : path);
        public Task RefreshMetadataAsync(string path)
        {
            if (path == CachedPath) CachedTarget = null;
            return Task.CompletedTask;
        }
    }
}
