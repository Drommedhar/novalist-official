using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class FileServiceTests
{
    private readonly FileService _sut = new();

    [Fact]
    public async Task WriteThenRead_RoundTrips()
    {
        using var dir = new TempDir();
        var path = dir.Combine("a.txt");
        await _sut.WriteTextAsync(path, "hello");
        Assert.Equal("hello", await _sut.ReadTextAsync(path));
    }

    [Fact]
    public async Task WriteTextAsync_CreatesMissingDirectory()
    {
        using var dir = new TempDir();
        var path = dir.Combine("nested", "deep", "a.txt");
        await _sut.WriteTextAsync(path, "x");
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task GetFileSizeAsync_ReturnsByteLength()
    {
        using var dir = new TempDir();
        var path = dir.Combine("a.txt");
        await _sut.WriteTextAsync(path, "hello"); // 5 ASCII bytes
        Assert.Equal(5, await _sut.GetFileSizeAsync(path));
    }

    [Fact]
    public async Task GetLastWriteTimeUtcAsync_ReflectsWrite()
    {
        using var dir = new TempDir();
        var path = dir.Combine("a.txt");
        var before = DateTime.UtcNow.AddSeconds(-2);
        await _sut.WriteTextAsync(path, "x");
        var mtime = await _sut.GetLastWriteTimeUtcAsync(path);
        Assert.True(mtime >= before);
        Assert.Equal(DateTimeKind.Utc, mtime.Kind);
    }

    [Fact]
    public async Task ExistsAsync_TrueThenFalse()
    {
        using var dir = new TempDir();
        var path = dir.Combine("a.txt");
        Assert.False(await _sut.ExistsAsync(path));
        await _sut.WriteTextAsync(path, "x");
        Assert.True(await _sut.ExistsAsync(path));
    }

    [Fact]
    public async Task DirectoryExistsAsync()
    {
        using var dir = new TempDir();
        Assert.True(await _sut.DirectoryExistsAsync(dir.Path));
        Assert.False(await _sut.DirectoryExistsAsync(dir.Combine("nope")));
    }

    [Fact]
    public async Task CreateDirectoryAsync_Creates()
    {
        using var dir = new TempDir();
        var sub = dir.Combine("sub");
        await _sut.CreateDirectoryAsync(sub);
        Assert.True(Directory.Exists(sub));
    }

    [Fact]
    public async Task GetFilesAsync_MissingDirectory_ReturnsEmpty()
    {
        using var dir = new TempDir();
        Assert.Empty(await _sut.GetFilesAsync(dir.Combine("nope")));
    }

    [Fact]
    public async Task GetFilesAsync_TopAndRecursive()
    {
        using var dir = new TempDir();
        await _sut.WriteTextAsync(dir.Combine("a.txt"), "1");
        await _sut.WriteTextAsync(dir.Combine("sub", "b.txt"), "2");

        Assert.Single(await _sut.GetFilesAsync(dir.Path, "*.txt"));
        Assert.Equal(2, (await _sut.GetFilesAsync(dir.Path, "*.txt", recursive: true)).Count);
    }

    [Fact]
    public async Task GetDirectoriesAsync_MissingAndPresent()
    {
        using var dir = new TempDir();
        Assert.Empty(await _sut.GetDirectoriesAsync(dir.Combine("nope")));
        await _sut.CreateDirectoryAsync(dir.Combine("sub"));
        Assert.Single(await _sut.GetDirectoriesAsync(dir.Path));
    }

    [Fact]
    public async Task DeleteFileAsync_ExistingAndMissing()
    {
        using var dir = new TempDir();
        var path = dir.Combine("a.txt");
        await _sut.WriteTextAsync(path, "x");
        await _sut.DeleteFileAsync(path);
        Assert.False(File.Exists(path));
        // Deleting a missing file is a no-op.
        await _sut.DeleteFileAsync(path);
    }

    [Fact]
    public async Task DeleteDirectoryAsync_ExistingAndMissing()
    {
        using var dir = new TempDir();
        var sub = dir.Combine("sub");
        await _sut.CreateDirectoryAsync(sub);
        await _sut.DeleteDirectoryAsync(sub);
        Assert.False(Directory.Exists(sub));
        await _sut.DeleteDirectoryAsync(sub); // no-op
    }

    [Fact]
    public async Task MoveFileAsync_CreatesTargetDirectory()
    {
        using var dir = new TempDir();
        var src = dir.Combine("a.txt");
        var dst = dir.Combine("moved", "b.txt");
        await _sut.WriteTextAsync(src, "x");
        await _sut.MoveFileAsync(src, dst);
        Assert.False(File.Exists(src));
        Assert.Equal("x", await _sut.ReadTextAsync(dst));
    }

    [Fact]
    public async Task MoveFileAsync_ExistingTargetDir()
    {
        using var dir = new TempDir();
        var src = dir.Combine("a.txt");
        var dst = dir.Combine("b.txt"); // target dir = dir.Path, already exists
        await _sut.WriteTextAsync(src, "x");
        await _sut.MoveFileAsync(src, dst);
        Assert.True(File.Exists(dst));
    }

    [Fact]
    public void PathHelpers()
    {
        Assert.Equal(Path.Combine("a", "b"), _sut.CombinePath("a", "b"));
        Assert.Equal("a.txt", _sut.GetFileName(Path.Combine("x", "a.txt")));
        Assert.Equal("a", _sut.GetFileNameWithoutExtension(Path.Combine("x", "a.txt")));
        Assert.Equal(Path.Combine("x"), _sut.GetDirectoryName(Path.Combine("x", "a.txt")));
    }

    [Fact]
    public void GetDirectoryName_Root_ReturnsEmpty()
    {
        // Path.GetDirectoryName of a path root is null -> coalesced to empty.
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        Assert.Equal(string.Empty, _sut.GetDirectoryName(root));
    }

    [Fact]
    public async Task ConcurrentWritesToOneFileAllLand()
    {
        using var dir = new TempDir();
        var path = dir.Combine("settings.json");

        // Novalist saves on a timer while the writer keeps working, so two
        // saves of the same file overlap in ordinary use. On Windows the second
        // one does not wait - it fails with "used by another process" and the
        // write is lost.
        await Task.WhenAll(Enumerable.Range(0, 40)
            .Select(i => _sut.WriteTextAsync(path, $"value {i}")));

        Assert.StartsWith("value ", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task ReadsAndWritesOfOneFileDoNotCollide()
    {
        using var dir = new TempDir();
        var path = dir.Combine("settings.json");
        await _sut.WriteTextAsync(path, "first");

        var work = Enumerable.Range(0, 20)
            .SelectMany(i => new[] { _sut.WriteTextAsync(path, $"v{i}"), _sut.ReadTextAsync(path) })
            .ToArray();

        // A read landing mid-write is the same collision from the other side.
        await Task.WhenAll(work);
        Assert.NotEmpty(await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task AFileHeldByAnotherProcessIsWaitedOutRatherThanFailed()
    {
        using var dir = new TempDir();
        var path = dir.Combine("held.txt");
        await _sut.WriteTextAsync(path, "before");

        // The gate settles what Novalist itself is doing; a backup tool or a
        // scanner holding the file for a moment is outside it.
        var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var write = _sut.WriteTextAsync(path, "after");
        await Task.Delay(50);
        holder.Dispose();

        await write;
        Assert.Equal("after", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task AFileHeldForeverStillReportsTheFailure()
    {
        using var dir = new TempDir();
        var path = dir.Combine("stuck.txt");
        await _sut.WriteTextAsync(path, "before");

        using var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        // Retrying forever would hang the save queue behind one stuck file.
        // The caller has to hear about it eventually.
        await Assert.ThrowsAsync<IOException>(() => _sut.WriteTextAsync(path, "after"));
    }
}
