using System.IO.Compression;
using Novalist.Core.Tests.TestHelpers;
using Novalist.Mobile.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

public sealed class MobileExportFilesTests
{
    [Fact]
    public void RepeatedExportsWithTheSameTitleHaveSeparateFiles()
    {
        using var dir = new TempDir();
        var exports = new ExportFiles(dir.Path);
        var first = exports.Create("Chapter.docx");
        var second = exports.Create("Chapter.docx");
        File.WriteAllText(first, "first");
        File.WriteAllText(second, "second");

        Assert.NotEqual(first, second);
        Assert.Equal("Chapter.docx", Path.GetFileName(first));
        Assert.Equal(first, exports.PrepareShare(first));
        exports.Release(first);
        Assert.False(Directory.Exists(Path.GetDirectoryName(first)));
        Assert.Equal("second", File.ReadAllText(second));
        exports.Release(first); // Cancellation and cleanup can both release.
        exports.Release(second);
    }

    [Theory]
    [InlineData("../outside.docx")]
    [InlineData("/absolute.docx")]
    [InlineData("..\\outside.docx")]
    [InlineData("name\u0000with\ncontrols.docx")]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    public void ABookTitleCannotSelectTheOutputDirectory(string title)
    {
        using var dir = new TempDir();
        var exports = new ExportFiles(dir.Path);
        var path = exports.Create(title);
        Assert.Equal(Path.Combine(dir.Path, "exports"), Path.GetDirectoryName(Path.GetDirectoryName(path)));
        File.WriteAllText(path, "export");
        Assert.Equal(path, exports.PrepareShare(path));
        exports.Release(path);
    }

    [Fact]
    public void CompanionAssetsAreSharedTogetherWithTheirRelativePaths()
    {
        using var dir = new TempDir();
        var exports = new ExportFiles(dir.Path);
        var output = exports.Create("World.md");
        var assets = Path.Combine(Path.GetDirectoryName(output)!, "images");
        Directory.CreateDirectory(assets);
        File.WriteAllText(output, "![portrait](images/portrait.png)");
        File.WriteAllBytes(Path.Combine(assets, "portrait.png"), [1, 2, 3]);

        var shared = exports.PrepareShare(output);
        Assert.Equal("World.zip", Path.GetFileName(shared));
        using (var archive = ZipFile.OpenRead(shared))
        {
            Assert.Equal(2, archive.Entries.Count);
            Assert.NotNull(archive.GetEntry("images/portrait.png"));
            using var reader = new StreamReader(archive.GetEntry("World.md")!.Open());
            Assert.Equal("![portrait](images/portrait.png)", reader.ReadToEnd());
        }
        Assert.Equal(shared, exports.PrepareShare(output)); // A retry replaces its own archive.
        exports.Release(output);
        Assert.False(File.Exists(shared));
        Assert.False(Directory.Exists(assets));
    }

    [Fact]
    public void FailedExportsAndUnallocatedPathsCannotBeShared()
    {
        using var dir = new TempDir();
        var exports = new ExportFiles(dir.Path);
        var missing = exports.Create("failed.epub");
        Assert.Throws<FileNotFoundException>(() => exports.PrepareShare(missing));
        exports.Release(missing);
        var privateFile = dir.Combine("private.json");
        File.WriteAllText(privateFile, "private");
        Assert.Throws<InvalidOperationException>(() => exports.PrepareShare(privateFile));
        exports.Release(privateFile);
        Assert.True(File.Exists(privateFile));
        Assert.Throws<InvalidOperationException>(() => exports.PrepareShare(missing));
    }
}
