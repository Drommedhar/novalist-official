using System.IO.Compression;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class ArchiveServiceTests
{
    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public async Task CreateFromDirectoryAsync_ArchivesNestedTree()
    {
        using var temp = new TempDir();
        var src = temp.Combine("project");
        Write(Path.Combine(src, "project.json"), "{}");
        Write(Path.Combine(src, "Books", "One", "scene.novalist"), "<p>text</p>");

        var zipPath = temp.Combine("out.zip");
        var sut = new ArchiveService();

        var count = await sut.CreateFromDirectoryAsync(src, zipPath, Array.Empty<string>());

        Assert.Equal(2, count);
        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, e => e.FullName == "project.json");
        Assert.Contains(zip.Entries, e => e.FullName == "Books/One/scene.novalist");
    }

    [Fact]
    public async Task CreateFromDirectoryAsync_SkipsExcludedDirectoriesAtAnyDepth()
    {
        using var temp = new TempDir();
        var src = temp.Combine("project");
        Write(Path.Combine(src, "keep.txt"), "keep");
        Write(Path.Combine(src, ".git", "HEAD"), "ref");
        Write(Path.Combine(src, "Books", ".git", "config"), "x");

        var zipPath = temp.Combine("out.zip");
        var count = await new ArchiveService()
            .CreateFromDirectoryAsync(src, zipPath, new[] { ".git" });

        Assert.Equal(1, count);
        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Single(zip.Entries);
        Assert.Equal("keep.txt", zip.Entries[0].FullName);
    }

    [Fact]
    public async Task CreateFromDirectoryAsync_NullExcludes_ArchivesEverything()
    {
        using var temp = new TempDir();
        var src = temp.Combine("project");
        Write(Path.Combine(src, "a.txt"), "a");

        var count = await new ArchiveService()
            .CreateFromDirectoryAsync(src, temp.Combine("out.zip"), null!);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CreateFromDirectoryAsync_CreatesMissingDestinationFolder()
    {
        using var temp = new TempDir();
        var src = temp.Combine("project");
        Write(Path.Combine(src, "a.txt"), "a");

        var zipPath = temp.Combine("nested", "deeper", "out.zip");
        await new ArchiveService().CreateFromDirectoryAsync(src, zipPath, Array.Empty<string>());

        Assert.True(File.Exists(zipPath));
    }

    [Fact]
    public async Task ExtractToDirectoryAsync_RoundTripsContent()
    {
        using var temp = new TempDir();
        var src = temp.Combine("project");
        Write(Path.Combine(src, "project.json"), "{\"a\":1}");
        Write(Path.Combine(src, "Books", "scene.novalist"), "<p>hello</p>");

        var zipPath = temp.Combine("out.zip");
        var sut = new ArchiveService();
        await sut.CreateFromDirectoryAsync(src, zipPath, Array.Empty<string>());

        var dest = temp.Combine("restored");
        var restored = await sut.ExtractToDirectoryAsync(zipPath, dest);

        Assert.Equal(2, restored);
        Assert.Equal("{\"a\":1}", File.ReadAllText(Path.Combine(dest, "project.json")));
        Assert.Equal("<p>hello</p>", File.ReadAllText(Path.Combine(dest, "Books", "scene.novalist")));
    }

    [Fact]
    public async Task ExtractToDirectoryAsync_OverwritesExistingFiles()
    {
        using var temp = new TempDir();
        var src = temp.Combine("project");
        Write(Path.Combine(src, "a.txt"), "new");

        var zipPath = temp.Combine("out.zip");
        var sut = new ArchiveService();
        await sut.CreateFromDirectoryAsync(src, zipPath, Array.Empty<string>());

        var dest = temp.Combine("restored");
        Write(Path.Combine(dest, "a.txt"), "old");

        await sut.ExtractToDirectoryAsync(zipPath, dest);

        Assert.Equal("new", File.ReadAllText(Path.Combine(dest, "a.txt")));
    }

    [Fact]
    public async Task ExtractToDirectoryAsync_SkipsDirectoryEntries()
    {
        using var temp = new TempDir();
        var zipPath = temp.Combine("dirs.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntry("folder/");
            var entry = zip.CreateEntry("folder/file.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("body");
        }

        var dest = temp.Combine("out");
        var restored = await new ArchiveService().ExtractToDirectoryAsync(zipPath, dest);

        Assert.Equal(1, restored);
        Assert.Equal("body", File.ReadAllText(Path.Combine(dest, "folder", "file.txt")));
    }

    [Fact]
    public async Task ExtractToDirectoryAsync_RefusesEntriesEscapingTheDestination()
    {
        using var temp = new TempDir();
        var zipPath = temp.Combine("evil.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("../escaped.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("pwned");
        }

        var dest = temp.Combine("out");
        var restored = await new ArchiveService().ExtractToDirectoryAsync(zipPath, dest);

        Assert.Equal(0, restored);
        Assert.False(File.Exists(temp.Combine("escaped.txt")));
    }

    [Theory]
    [InlineData("../out-sibling/escaped.txt")]
    [InlineData("..\\out-sibling\\escaped.txt")]
    [InlineData(".git/HEAD")]
    [InlineData(".GIT/HEAD")]
    public async Task RestoreProjectAsync_RejectsUnsafeEntriesBeforeChangingTheDestination(string entryName)
    {
        using var temp = new TempDir();
        var archive = temp.Combine("unsafe.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry(".novalist/project.json").Open()))
                writer.Write("{\"name\":\"Book\",\"books\":[{\"name\":\"One\"}]}");
            using (var writer = new StreamWriter(zip.CreateEntry(entryName).Open()))
                writer.Write("unsafe");
        }
        var destination = temp.Combine("out");
        Write(Path.Combine(destination, "keep.txt"), "original");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ArchiveService().RestoreProjectAsync(archive, destination, replaceExisting: true));

        Assert.Equal("original", File.ReadAllText(Path.Combine(destination, "keep.txt")));
        Assert.Single(Directory.GetFiles(destination, "*", SearchOption.AllDirectories));
        Assert.False(Directory.Exists(temp.Combine("out-sibling")));
    }

    [Fact]
    public async Task RestoreProjectAsync_InvalidArchiveDoesNotCreateNewDestination()
    {
        using var temp = new TempDir();
        var archive = temp.Combine("unrelated.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            zip.CreateEntry("readme.txt");
        var destination = temp.Combine("new-project");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ArchiveService().RestoreProjectAsync(archive, destination, replaceExisting: false));

        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public async Task RestoreProjectAsync_RemovesLaterFilesAndPreservesNestedGit()
    {
        using var temp = new TempDir();
        var source = temp.Combine("source");
        Write(Path.Combine(source, ".novalist", "project.json"),
            "{\"name\":\"Book\",\"books\":[{\"name\":\"One\"}]}");
        Write(Path.Combine(source, "Book", "scene.txt"), "original");
        var archive = temp.Combine("backup.zip");
        var sut = new ArchiveService();
        await sut.CreateFromDirectoryAsync(source, archive, BackupService.ExcludedDirectories);
        var destination = temp.Combine("out");
        Write(Path.Combine(destination, "Book", "scene.txt"), "changed");
        Write(Path.Combine(destination, "Book", ".git", "HEAD"), "ref");
        Write(Path.Combine(destination, "Later", "scene.txt"), "later");

        await sut.RestoreProjectAsync(archive, destination, replaceExisting: true);

        Assert.Equal("original", File.ReadAllText(Path.Combine(destination, "Book", "scene.txt")));
        Assert.Equal("ref", File.ReadAllText(Path.Combine(destination, "Book", ".git", "HEAD")));
        Assert.False(Directory.Exists(Path.Combine(destination, "Later")));
    }
}
