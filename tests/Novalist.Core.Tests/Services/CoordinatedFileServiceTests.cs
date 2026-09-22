using System.Text;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public sealed class CoordinatedFileServiceTests
{
    [Fact]
    public async Task ReadsAndExistenceWaitForTheProviderToDownloadTheFile()
    {
        using var dir = new TempDir();
        var path = dir.Combine("project.json");
        var coordinator = new RecordingFileCoordinator
        {
            BeforeRead = requested =>
            {
                Assert.Equal(path, requested);
                if (!File.Exists(path)) File.WriteAllText(path, "{\"name\":\"Übersee\"}");
            }
        };
        var files = new CoordinatedFileService(coordinator);

        Assert.True(await files.ExistsAsync(path));
        File.Delete(path); // Evicted between the existence probe and actual read.
        Assert.Equal("{\"name\":\"Übersee\"}", await files.ReadTextAsync(path));
        Assert.Equal(File.ReadAllBytes(path), await files.ReadBytesAsync(path));
        Assert.Equal(new FileInfo(path).Length, await files.GetFileSizeAsync(path));
        Assert.Equal(File.GetLastWriteTimeUtc(path), await files.GetLastWriteTimeUtcAsync(path));
        Assert.Equal(5, coordinator.Accesses.Count);
    }

    [Fact]
    public async Task AccessFailureIsNotReportedAsAnAbsentFileOrAnEmptyDirectory()
    {
        using var dir = new TempDir();
        var coordinator = new RecordingFileCoordinator
        {
            BeforeRead = _ => throw new IOException("Download unavailable")
        };
        var files = new CoordinatedFileService(coordinator);
        await Assert.ThrowsAsync<IOException>(() => files.ExistsAsync(dir.Combine("project.json")));
        await Assert.ThrowsAsync<IOException>(() => files.DirectoryExistsAsync(dir.Path));
        await Assert.ThrowsAsync<IOException>(() => files.GetFilesAsync(dir.Path));
        await Assert.ThrowsAsync<IOException>(() => files.GetDirectoriesAsync(dir.Path));
        await Assert.ThrowsAsync<IOException>(() => files.CreateDirectoryAsync(dir.Path));
        Assert.All(coordinator.Accesses, access => Assert.Equal("read", access.Operation));
    }

    [Fact]
    public async Task GenuineMissingPathsAndFileDirectoryDistinctionArePreserved()
    {
        using var dir = new TempDir();
        var files = new CoordinatedFileService(new RecordingFileCoordinator());
        await files.WriteTextAsync(dir.Combine("entry.json"), "{}");
        Assert.True(await files.DirectoryExistsAsync(dir.Path));
        Assert.False(await files.ExistsAsync(dir.Path));
        Assert.False(await files.DirectoryExistsAsync(dir.Combine("entry.json")));
        Assert.False(await files.ExistsAsync(dir.Combine("missing.json")));
        Assert.False(await files.ExistsAsync(dir.Combine("absent", "missing.json")));
        Assert.Empty(await files.GetFilesAsync(dir.Combine("absent")));
        Assert.Empty(await files.GetDirectoriesAsync(dir.Combine("absent")));
    }

    [Fact]
    public async Task WritesReplaceCompleteContentAndLeaveNoTemporaryFiles()
    {
        using var dir = new TempDir();
        var coordinator = new RecordingFileCoordinator();
        var files = new CoordinatedFileService(coordinator);
        var path = dir.Combine("nested", "deeper", "entry.json");

        await files.WriteTextAsync(path, "a long previous value");
        await files.WriteTextAsync(path, "ä");

        Assert.Equal(Encoding.UTF8.GetBytes("ä"), await files.ReadBytesAsync(path));
        Assert.Equal([path], Directory.GetFiles(dir.Path, "*", SearchOption.AllDirectories));
        Assert.Contains(("write", dir.Combine("nested")), coordinator.Accesses);
        Assert.Contains(("write", dir.Combine("nested", "deeper")), coordinator.Accesses);
        Assert.Equal(2, coordinator.Accesses.Count(a => a == ("write", path)));
    }

    [Fact]
    public async Task DeniedWriteLeavesThePreviousVersionIntact()
    {
        using var dir = new TempDir();
        var path = dir.Combine("entry.json");
        File.WriteAllText(path, "previous");
        var coordinator = new RecordingFileCoordinator
        {
            BeforeWrite = (_, _) => throw new IOException("Provider denied save")
        };

        await Assert.ThrowsAsync<IOException>(() => new CoordinatedFileService(coordinator).WriteTextAsync(path, "next"));

        Assert.Equal("previous", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(dir.Path));
    }

    [Fact]
    public async Task FailedReplacementCleansUpTheTemporaryFile()
    {
        using var dir = new TempDir();
        var destination = dir.Combine("folder");
        Directory.CreateDirectory(destination);
        var files = new CoordinatedFileService(new RecordingFileCoordinator());

        await Assert.ThrowsAnyAsync<IOException>(() => files.WriteTextAsync(destination, "cannot replace a directory"));

        Assert.Empty(Directory.GetFiles(dir.Path));
        Assert.True(Directory.Exists(destination));
    }

    [Fact]
    public async Task UsesTheProvidersReturnedPathsForReadsWritesMovesAndDeletes()
    {
        using var dir = new TempDir();
        var requested = dir.Combine("old-address.json");
        var actual = dir.Combine("current-address.json");
        var destination = dir.Combine("destination.json");
        var actualDestination = dir.Combine("current-destination.json");
        var coordinator = new RecordingFileCoordinator
        {
            Resolve = path => path == requested ? actual : path == destination ? actualDestination : path
        };
        var files = new CoordinatedFileService(coordinator);

        await files.WriteTextAsync(requested, "content");
        Assert.False(File.Exists(requested));
        Assert.Equal("content", await files.ReadTextAsync(requested));
        await files.MoveFileAsync(requested, destination);
        Assert.False(File.Exists(actual));
        Assert.Equal("content", File.ReadAllText(actualDestination));
        await files.DeleteFileAsync(destination);
        await files.DeleteFileAsync(destination); // Missing is still a no-op.
        Assert.False(File.Exists(actualDestination));
        Assert.Contains(("move", requested), coordinator.Accesses);
        Assert.Contains(("destination", destination), coordinator.Accesses);
        Assert.Single(coordinator.Accesses, a => a == ("delete", destination));
    }

    [Fact]
    public async Task EnumerationAndChapterMovesCoordinateEveryDirectory()
    {
        using var dir = new TempDir();
        var coordinator = new RecordingFileCoordinator();
        var files = new CoordinatedFileService(coordinator);
        var chapter = dir.Combine("chapter");
        await files.WriteTextAsync(Path.Combine(chapter, "a.json"), "{}");
        await files.WriteBytesAsync(Path.Combine(chapter, "sub", "b.json"), [1, 2, 3]);
        await files.WriteTextAsync(Path.Combine(chapter, "sub", "scene.novalist"), "text");
        coordinator.Accesses.Clear();

        Assert.Single(await files.GetFilesAsync(chapter, "*.json"));
        Assert.Equal(2, (await files.GetFilesAsync(chapter, "*.json", recursive: true)).Count);
        Assert.Contains(("read", Path.Combine(chapter, "sub")), coordinator.Accesses);
        Assert.Equal([Path.Combine(chapter, "sub")], await files.GetDirectoriesAsync(chapter));
        var renamed = dir.Combine("new-parent", "renamed");
        await files.MoveDirectoryAsync(chapter, renamed);
        Assert.False(Directory.Exists(chapter));
        Assert.Equal([1, 2, 3], await files.ReadBytesAsync(Path.Combine(renamed, "sub", "b.json")));
        Assert.Contains(("move", chapter), coordinator.Accesses);
        Assert.Contains(("destination", renamed), coordinator.Accesses);
        await Assert.ThrowsAsync<IOException>(() => files.DeleteDirectoryAsync(renamed, recursive: false));
        await files.DeleteDirectoryAsync(renamed);
        await files.DeleteDirectoryAsync(renamed);
        Assert.False(Directory.Exists(renamed));
        Assert.Contains(("delete", renamed), coordinator.Accesses);
    }

    [Fact]
    public void PathHelpersRetainNormalFilesystemSemantics()
    {
        var files = new CoordinatedFileService(new RecordingFileCoordinator());
        var path = files.CombinePath("book", "entry.json");
        Assert.Equal(Path.Combine("book", "entry.json"), path);
        Assert.Equal("entry.json", files.GetFileName(path));
        Assert.Equal("entry", files.GetFileNameWithoutExtension(path));
        Assert.Equal("book", files.GetDirectoryName(path));
        Assert.Empty(files.GetDirectoryName(Path.GetPathRoot(Path.GetTempPath())!));
    }
}
