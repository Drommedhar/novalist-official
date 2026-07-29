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
}
