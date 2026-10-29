using System.IO.Compression;
using System.Text;
using NSubstitute;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Novalist.Mobile.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

public sealed class MobileDocumentTreeArchiveTests
{
    private const string Metadata = "{\"name\":\"Novel\",\"books\":[{\"name\":\"Book\"}]}";

    [Fact]
    public async Task CancelledCloudBackupStopsReadingAndRemovesStagingWithoutPublishing()
    {
        using var temp = new TempDir();
        using var cancellation = new CancellationTokenSource();
        var files = Substitute.For<IFileService>();
        var root = temp.Combine("provider");
        var first = Path.Combine(root, "first.md");
        var second = Path.Combine(root, "second.md");
        files.DirectoryExistsAsync(root).Returns(true);
        files.GetFilesAsync(root, "*", false).Returns(new[] { first, second });
        files.ReadBytesAsync(first).Returns(_ => { cancellation.Cancel(); return new byte[] { 1 }; });
        var output = temp.Combine("backup.zip");
        var archives = new DocumentTreeArchiveService(files, path => path.StartsWith(root), temp.Path);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            archives.CreateFromDirectoryAsync(root, output, [], cancellation.Token));

        await files.DidNotReceive().ReadBytesAsync(second);
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.GetDirectories(temp.Path));
    }

    [Fact]
    public async Task BackupNeverOverwritesAnExistingDestination()
    {
        using var temp = new TempDir();
        var files = new InMemoryFileService();
        var output = temp.Combine("provider", "backup.zip");
        await files.WriteBytesAsync(output, [1, 2, 3]);
        var archives = new DocumentTreeArchiveService(files, _ => true, temp.Path);

        await Assert.ThrowsAsync<IOException>(() => archives.CreateFromDirectoryAsync("missing", output, []));

        Assert.Equal([1, 2, 3], await files.ReadBytesAsync(output));
    }

    [Fact]
    public async Task CancellationDuringArchivePublicationStillCompletesPromotionAndCleanup()
    {
        using var temp = new TempDir();
        using var cancellation = new CancellationTokenSource();
        var files = Substitute.For<IFileService>();
        var root = temp.Combine("source");
        var source = Path.Combine(root, "scene.txt");
        var output = temp.Combine("backup.zip");
        files.DirectoryExistsAsync(root).Returns(true);
        files.GetFilesAsync(root, "*", false).Returns(new[] { source });
        files.ReadBytesAsync(source).Returns(_ =>
        {
            Assert.Equal(cancellation.Token, DocumentTreeReadCancellation.Current);
            return Encoding.UTF8.GetBytes("preserved");
        });
        files.WriteBytesAsync(Arg.Any<string>(), Arg.Any<byte[]>()).Returns(_ =>
        {
            Assert.False(DocumentTreeReadCancellation.Current.CanBeCanceled);
            cancellation.Cancel();
            return Task.CompletedTask;
        });
        files.MoveFileAsync(Arg.Any<string>(), output).Returns(_ =>
        {
            Assert.False(DocumentTreeReadCancellation.Current.CanBeCanceled);
            return Task.CompletedTask;
        });
        var archives = new DocumentTreeArchiveService(files, _ => true, temp.Path);

        Assert.Equal(1, await archives.CreateFromDirectoryAsync(root, output, [], cancellation.Token));

        await files.Received(1).MoveFileAsync(Arg.Any<string>(), output);
        await files.Received(1).DeleteFileAsync(Arg.Any<string>());
        Assert.Empty(Directory.GetDirectories(temp.Path));
    }

    [Fact]
    public async Task RestoringProviderBackupToLocalProjectRejectsLinkedDestinationFiles()
    {
        using var temp = new TempDir();
        var files = new InMemoryFileService();
        var archive = temp.Combine("provider.zip");
        await files.WriteBytesAsync(archive, Zip((".novalist/project.json", Metadata), ("scene.md", "Replacement")));
        var destination = temp.Combine("local");
        Directory.CreateDirectory(destination);
        var protectedFile = temp.Combine("outside.md");
        await File.WriteAllTextAsync(protectedFile, "Keep original");
        File.CreateSymbolicLink(Path.Combine(destination, "scene.md"), protectedFile);
        var archives = new DocumentTreeArchiveService(files, path => path == archive, temp.Path);

        await Assert.ThrowsAsync<IOException>(() => archives.RestoreProjectAsync(archive, destination, true));

        Assert.Equal("Keep original", await File.ReadAllTextAsync(protectedFile));
    }

    [Fact]
    public async Task BackupReadsProviderFilesAndExcludesGit()
    {
        using var temp = new TempDir();
        var files = new InMemoryFileService();
        var root = temp.Combine("provider", "Novel");
        await AddAsync(files, Path.Combine(root, ".novalist", "project.json"), Metadata);
        await AddAsync(files, Path.Combine(root, "Book", "Grüße.md"), "Grüße, 世界");
        await AddAsync(files, Path.Combine(root, ".git", "config"), "private");
        var output = temp.Combine("provider", "backup.novalist");
        var archives = new DocumentTreeArchiveService(files, _ => true, temp.Path);

        Assert.Equal(2, await archives.CreateFromDirectoryAsync(root, output, [".git"]));

        using var zip = new ZipArchive(new MemoryStream(await files.ReadBytesAsync(output)));
        Assert.Equal([".novalist/project.json", "Book/Grüße.md"], zip.Entries.Select(e => e.FullName).Order().ToArray());
        using var reader = new StreamReader(zip.GetEntry("Book/Grüße.md")!.Open());
        Assert.Equal("Grüße, 世界", reader.ReadToEnd());
        Assert.Empty(Directory.GetDirectories(temp.Path));
    }

    [Fact]
    public async Task RestoreValidatesBeforePublishingAndRefusesExistingDestination()
    {
        using var temp = new TempDir();
        var files = new InMemoryFileService();
        var archive = temp.Combine("provider", "book.novalist");
        var destination = temp.Combine("provider", "Restored");
        await files.WriteBytesAsync(archive, Zip((".novalist/project.json", Metadata), ("Book/scene.md", "Original")));
        var archives = new DocumentTreeArchiveService(files, _ => true, temp.Path);

        await archives.RestoreProjectAsync(archive, destination, false);
        Assert.Equal("Original", await files.ReadTextAsync(Path.Combine(destination, "Book", "scene.md")));
        await Assert.ThrowsAsync<IOException>(() => archives.RestoreProjectAsync(archive, destination, false));
        Assert.Empty(Directory.GetDirectories(temp.Path));
    }

    [Theory]
    [InlineData("../escaped.md", "Bad")]
    [InlineData(".novalist/project.json", "invalid")]
    [InlineData("unrelated.txt", "Unrelated")]
    public async Task InvalidBackupLeavesExistingProjectIntact(string entry, string content)
    {
        using var temp = new TempDir();
        var files = new InMemoryFileService();
        var archive = temp.Combine("provider", "bad.novalist");
        var destination = temp.Combine("provider", "Existing");
        await AddAsync(files, Path.Combine(destination, "scene.md"), "Keep me");
        await files.WriteBytesAsync(archive, Zip((entry, content)));
        var archives = new DocumentTreeArchiveService(files, _ => true, temp.Path);

        await Assert.ThrowsAnyAsync<Exception>(() => archives.RestoreProjectAsync(archive, destination, true));

        Assert.Equal("Keep me", await files.ReadTextAsync(Path.Combine(destination, "scene.md")));
        Assert.Single(await files.GetFilesAsync(destination));
        Assert.Empty(Directory.GetDirectories(temp.Path));
    }

    [Fact]
    public async Task RestoreReconcilesFilesButPreservesGit()
    {
        using var temp = new TempDir();
        var files = new InMemoryFileService();
        var archive = temp.Combine("provider", "book.zip");
        var destination = temp.Combine("provider", "Novel");
        await AddAsync(files, Path.Combine(destination, "obsolete.md"), "Old");
        await AddAsync(files, Path.Combine(destination, ".git", "config"), "Keep git");
        await files.WriteBytesAsync(archive, Zip((".novalist/project.json", Metadata), ("scene.md", "Restored")));
        var archives = new DocumentTreeArchiveService(files, _ => true, temp.Path);

        await archives.RestoreProjectAsync(archive, destination, true);

        Assert.False(await files.ExistsAsync(Path.Combine(destination, "obsolete.md")));
        Assert.Equal("Keep git", await files.ReadTextAsync(Path.Combine(destination, ".git", "config")));
        Assert.Equal("Restored", await files.ReadTextAsync(Path.Combine(destination, "scene.md")));
    }

    [Fact]
    public async Task OrdinaryLocalProjectsKeepExistingArchiveBehavior()
    {
        using var temp = new TempDir();
        var source = temp.Combine("source");
        Directory.CreateDirectory(Path.Combine(source, ".novalist"));
        await File.WriteAllTextAsync(Path.Combine(source, ".novalist", "project.json"), Metadata);
        var archives = new DocumentTreeArchiveService(new Novalist.Core.Services.FileService(), _ => false, temp.Path);
        var archive = temp.Combine("local.zip");
        Assert.Equal(1, await archives.CreateFromDirectoryAsync(source, archive, []));
        Assert.Equal(1, await archives.ExtractToDirectoryAsync(archive, temp.Combine("extracted")));
        await archives.RestoreProjectAsync(archive, temp.Combine("restored"), false);
        Assert.Equal(Metadata, await File.ReadAllTextAsync(temp.Combine("restored", ".novalist", "project.json")));
    }

    private static async Task AddAsync(InMemoryFileService files, string path, string value)
    {
        await files.CreateDirectoryAsync(Path.GetDirectoryName(path)!);
        await files.WriteTextAsync(path, value);
    }

    private static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            foreach (var entry in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(entry.Name).Open(), new UTF8Encoding(false));
                writer.Write(entry.Text);
            }
        return buffer.ToArray();
    }
}
