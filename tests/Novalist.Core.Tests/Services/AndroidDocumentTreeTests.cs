using System.Text;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Novalist.Mobile.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

public sealed partial class AndroidDocumentTreeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BatchReadsOverlapWhileWritesWaitForTheWholeBatch(bool existingOnly)
    {
        using var fixture = new DocumentFixture();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var peak = 0;
        var paths = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            File.WriteAllText(fixture.Provider.Combine($"{i}.txt"), $"text {i}");
            paths.Add(Path.Combine(fixture.Mount, $"{i}.txt"));
        }
        fixture.Tree.ReadOverride = path =>
        {
            var count = Interlocked.Increment(ref active);
            lock (paths) peak = Math.Max(peak, count);
            if (count >= 2) entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            try { return File.ReadAllBytes(path); }
            finally { Interlocked.Decrement(ref active); }
        };
        var reading = existingOnly ? fixture.Files.ReadExistingTextsAsync(paths) : fixture.Files.ReadTextsAsync(paths);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var writing = fixture.Files.WriteTextAsync(paths[0], "changed");
            Assert.False(writing.IsCompleted);
            release.Set();
            var result = await reading.WaitAsync(TimeSpan.FromSeconds(5));
            await writing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("text 0", result[paths[0]]);
            Assert.Equal(8, result.Count);
            Assert.InRange(peak, 2, 4);
            Assert.Equal("changed", File.ReadAllText(fixture.Provider.Combine("0.txt")));
        }
        finally { release.Set(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingTextBatchesOmitMissingFiles(bool external)
    {
        using var fixture = new DocumentFixture();
        var source = external ? fixture.Provider.Path : fixture.Local.Path;
        var root = external ? fixture.Mount : fixture.Local.Path;
        File.WriteAllText(Path.Combine(source, "existing.txt"), "available");
        var existing = Path.Combine(root, "existing.txt");

        var result = await fixture.Files.ReadExistingTextsAsync([existing, Path.Combine(root, "missing.txt")]);

        Assert.Equal("available", Assert.Single(result).Value);
        Assert.True(result.ContainsKey(existing));
    }

    [Fact]
    public async Task ExistingTextBatchesPropagateProviderErrorsAndCancellation()
    {
        using var fixture = new DocumentFixture();
        File.WriteAllText(fixture.Provider.Combine("existing.txt"), "available");
        var paths = new[] { Path.Combine(fixture.Mount, "existing.txt") };
        fixture.Tree.ReadOverride = _ => throw new IOException("Provider read failed");
        await Assert.ThrowsAsync<IOException>(() => fixture.Files.ReadExistingTextsAsync(paths));
        fixture.Tree.Granted = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Files.ReadExistingTextsAsync(paths));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Files.ReadExistingTextsAsync(paths, cancellation.Token));
    }

    [Fact]
    public async Task ReadingAListedFolderDoesNotRelistEveryAncestorForEveryFile()
    {
        using var fixture = new DocumentFixture();
        var directory = fixture.Provider.Combine("Books", "Characters");
        Directory.CreateDirectory(directory);
        for (var i = 0; i < 48; i++) File.WriteAllText(Path.Combine(directory, $"{i}.json"), "entry");
        var files = await fixture.Files.GetFilesAsync(Path.Combine(fixture.Mount, "Books", "Characters"));

        foreach (var file in files) Assert.Equal("entry", await fixture.Files.ReadTextAsync(file));

        Assert.InRange(fixture.Tree.ListCalls, 1, 3);
        File.WriteAllText(Path.Combine(directory, Path.GetFileName(files[0])), "external edit");
        Assert.Equal("external edit", await fixture.Files.ReadTextAsync(files[0]));
    }

    [Fact]
    public async Task DirectoryMetadataExpiresAndReopeningInvalidatesItImmediately()
    {
        using var fixture = new DocumentFixture();
        Assert.Empty(await fixture.Files.GetFilesAsync(fixture.Mount));
        File.WriteAllText(fixture.Provider.Combine("external.txt"), "external");
        fixture.Clock.Now += TimeSpan.FromSeconds(31);
        Assert.Single(await fixture.Files.GetFilesAsync(fixture.Mount));
        File.WriteAllText(fixture.Provider.Combine("second.txt"), "second");
        fixture.Mounts.Invalidate(fixture.Mount);
        Assert.Equal(2, (await fixture.Files.GetFilesAsync(fixture.Mount)).Count);
    }

    [Fact]
    public async Task WarmDirectoryMetadataDoesNotHideRevokedPermissionOrLocalMutations()
    {
        using var fixture = new DocumentFixture();
        var path = Path.Combine(fixture.Mount, "scene.txt");
        Assert.Empty(await fixture.Files.GetFilesAsync(fixture.Mount));
        await fixture.Files.WriteTextAsync(path, "scene");
        Assert.Single(await fixture.Files.GetFilesAsync(fixture.Mount));
        await fixture.Files.DeleteFileAsync(path);
        Assert.Empty(await fixture.Files.GetFilesAsync(fixture.Mount));
        fixture.Tree.Granted = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Files.GetFilesAsync(fixture.Mount));
    }

    [Fact]
    public async Task CachedEmptyFolderCannotAllowNonrecursiveDeletionOfAnExternalAddition()
    {
        using var fixture = new DocumentFixture();
        var folder = Path.Combine(fixture.Mount, "folder");
        await fixture.Files.CreateDirectoryAsync(folder);
        Assert.Empty(await fixture.Files.GetFilesAsync(folder));
        File.WriteAllText(fixture.Provider.Combine("folder", "external.txt"), "keep");

        await Assert.ThrowsAsync<IOException>(() => fixture.Files.DeleteDirectoryAsync(folder, false));

        Assert.Equal("keep", File.ReadAllText(fixture.Provider.Combine("folder", "external.txt")));
    }

    [Fact]
    public async Task CachedMissingDestinationCannotAllowAMoveOverAnExternalAddition()
    {
        using var fixture = new DocumentFixture();
        var source = Path.Combine(fixture.Mount, "source.txt");
        var target = Path.Combine(fixture.Mount, "target.txt");
        await fixture.Files.WriteTextAsync(source, "source");
        Assert.False(await fixture.Files.ExistsAsync(target));
        File.WriteAllText(fixture.Provider.Combine("target.txt"), "keep");

        await Assert.ThrowsAsync<IOException>(() => fixture.Files.MoveFileAsync(source, target));

        Assert.Equal("keep", File.ReadAllText(fixture.Provider.Combine("target.txt")));
        Assert.Equal("source", File.ReadAllText(fixture.Provider.Combine("source.txt")));
    }

    [Fact]
    public async Task WritesGoDirectlyToProviderAndMountSurvivesRestart()
    {
        using var fixture = new DocumentFixture();
        var path = Path.Combine(fixture.Mount, "Books", "Grüße.txt");
        await fixture.Files.WriteTextAsync(path, "中文 draft");
        Assert.Equal("中文 draft", File.ReadAllText(fixture.Provider.Combine("Books", "Grüße.txt")));
        Assert.False(Directory.Exists(fixture.Mount));

        var restored = fixture.Restore();
        Assert.Equal(fixture.Mount, restored.Register("content://provider/tree/project"));
        Assert.Equal("中文 draft", await new DocumentTreeFileService(restored, new FileService()).ReadTextAsync(path));
    }

    [Fact]
    public async Task OrdinaryPathsUseLocalFilesAndUnknownMountsNeverBecomeLocalFiles()
    {
        using var fixture = new DocumentFixture();
        var path = fixture.Local.Combine("local.txt");
        await fixture.Files.WriteTextAsync(path, "local");
        Assert.Equal("local", File.ReadAllText(path));
        var unknown = Path.Combine(fixture.MountRoot, "unknown", "draft.txt");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Files.WriteTextAsync(unknown, "lost"));
        Assert.False(File.Exists(unknown));
    }

    [Fact]
    public async Task RevokedAccessFailsWithoutReturningMissingOrWritingLocally()
    {
        using var fixture = new DocumentFixture();
        fixture.Tree.Granted = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Files.ExistsAsync(Path.Combine(fixture.Mount, "draft.txt")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Files.WriteTextAsync(Path.Combine(fixture.Mount, "draft.txt"), "lost"));
        Assert.False(Directory.Exists(fixture.Mount));
    }

    [Theory]
    [InlineData("write")]
    [InlineData("promote")]
    public async Task FailedSavePreservesOriginalAndRemovesTemporaryFile(string failure)
    {
        using var fixture = new DocumentFixture();
        var path = Path.Combine(fixture.Mount, "draft.txt");
        File.WriteAllText(fixture.Provider.Combine("draft.txt"), "original");
        fixture.Tree.FailOperation = failure;
        await Assert.ThrowsAsync<IOException>(() => fixture.Files.WriteTextAsync(path, "replacement"));
        Assert.Equal("original", File.ReadAllText(fixture.Provider.Combine("draft.txt")));
        Assert.Single(Directory.GetFiles(fixture.Provider.Path));
    }

    [Fact]
    public async Task InterruptedReplacementRestoresBackupBeforeReading()
    {
        using var fixture = new DocumentFixture();
        var pending = fixture.PendingSave();
        File.WriteAllText(fixture.Provider.Combine(pending.BackupName), "original");
        File.WriteAllText(fixture.Provider.Combine(pending.TemporaryName), "partial");
        Assert.Equal("original", await fixture.Files.ReadTextAsync(Path.Combine(fixture.Mount, "draft.txt")));
        Assert.True(File.Exists(fixture.Provider.Combine("draft.txt")));
    }

    [Fact]
    public async Task AccessChecksLeaveAnUnfinishedSaveUntouchedUntilAFileOperation()
    {
        using var fixture = new DocumentFixture();
        var pending = fixture.PendingSave();
        File.WriteAllText(fixture.Provider.Combine(pending.BackupName), "original");
        File.WriteAllText(fixture.Provider.Combine(pending.TemporaryName), "partial");

        Assert.NotNull(fixture.Mounts.Resolve(fixture.Mount));
        Assert.NotNull(fixture.Mounts.Resolve(Path.Combine(fixture.Mount, "draft.txt")));
        Assert.False(File.Exists(fixture.Provider.Combine("draft.txt")));
        Assert.Equal("original", File.ReadAllText(fixture.Provider.Combine(pending.BackupName)));
        Assert.Equal("partial", File.ReadAllText(fixture.Provider.Combine(pending.TemporaryName)));

        Assert.Equal("original", await fixture.Files.ReadTextAsync(Path.Combine(fixture.Mount, "draft.txt")));
        Assert.Single(Directory.GetFiles(fixture.Provider.Path));
    }

    [Fact]
    public async Task FailedRollbackKeepsOriginalForNextAccess()
    {
        using var fixture = new DocumentFixture();
        var path = Path.Combine(fixture.Mount, "draft.txt");
        File.WriteAllText(fixture.Provider.Combine("draft.txt"), "original");
        fixture.Tree.FailOperation = "rollback";
        await Assert.ThrowsAsync<AggregateException>(() => fixture.Files.WriteTextAsync(path, "replacement"));
        Assert.Equal("original", File.ReadAllText(Assert.Single(Directory.GetFiles(fixture.Provider.Path, ".novalist-backup-*"))));
        fixture.Tree.FailOperation = null;
        Assert.Equal("original", await fixture.Files.ReadTextAsync(path));
    }

    [Fact]
    public async Task CompletedReplacementCleansOldBackupOnNextAccess()
    {
        using var fixture = new DocumentFixture();
        var pending = fixture.PendingSave();
        File.WriteAllText(fixture.Provider.Combine(pending.BackupName), "original");
        File.WriteAllText(fixture.Provider.Combine("draft.txt"), "replacement");
        Assert.Equal("replacement", await fixture.Files.ReadTextAsync(Path.Combine(fixture.Mount, "draft.txt")));
        Assert.Single(Directory.GetFiles(fixture.Provider.Path));
    }

    [Fact]
    public async Task UserFilesWithTransactionPrefixesAreNotChangedOrHidden()
    {
        using var fixture = new DocumentFixture();
        File.WriteAllText(fixture.Provider.Combine(".novalist-backup-draft.txt"), "backup kept by user");
        File.WriteAllText(fixture.Provider.Combine(".novalist-write-draft.txt"), "user draft");
        await fixture.Files.WriteTextAsync(Path.Combine(fixture.Mount, "draft.txt"), "current");
        Assert.Equal(3, (await fixture.Files.GetFilesAsync(fixture.Mount)).Count);
        Assert.Equal("backup kept by user", File.ReadAllText(fixture.Provider.Combine(".novalist-backup-draft.txt")));
        Assert.Equal("user draft", await fixture.Files.ReadTextAsync(Path.Combine(fixture.Mount, ".novalist-write-draft.txt")));
    }

    [Fact]
    public async Task TextReaderHandlesExistingBomAndEmptyFiles()
    {
        using var fixture = new DocumentFixture();
        File.WriteAllText(fixture.Provider.Combine("draft.txt"), "Grüße", new UTF8Encoding(true));
        Assert.Equal("Grüße", await fixture.Files.ReadTextAsync(Path.Combine(fixture.Mount, "draft.txt")));
        await fixture.Files.WriteTextAsync(Path.Combine(fixture.Mount, "draft.txt"), "");
        Assert.Equal(0, await fixture.Files.GetFileSizeAsync(Path.Combine(fixture.Mount, "draft.txt")));
    }

    [Fact]
    public async Task FailedMoveCannotLeavePartialDestination()
    {
        using var fixture = new DocumentFixture();
        var source = Path.Combine(fixture.Mount, "book");
        var target = Path.Combine(fixture.Mount, "other", "renamed");
        await fixture.Files.WriteTextAsync(Path.Combine(source, "draft.txt"), "original");
        fixture.Tree.FailOperation = "write";
        await Assert.ThrowsAsync<IOException>(() => fixture.Files.MoveDirectoryAsync(source, target));
        Assert.Equal("original", await fixture.Files.ReadTextAsync(Path.Combine(source, "draft.txt")));
        Assert.False(await fixture.Files.DirectoryExistsAsync(target));
    }

    [Fact]
    public async Task ConcurrentMovesCannotOverwriteDestination()
    {
        using var fixture = new DocumentFixture();
        var first = Path.Combine(fixture.Mount, "first.txt");
        var second = Path.Combine(fixture.Mount, "second.txt");
        var target = Path.Combine(fixture.Mount, "target.txt");
        await fixture.Files.WriteTextAsync(first, "first");
        await fixture.Files.WriteTextAsync(second, "second");
        var attempts = new[] { fixture.Files.MoveFileAsync(first, target), fixture.Files.MoveFileAsync(second, target) };
        await Assert.ThrowsAsync<IOException>(() => Task.WhenAll(attempts));
        Assert.Equal(1, attempts.Count(attempt => attempt.IsCompletedSuccessfully));
        Assert.Equal(2, (await fixture.Files.GetFilesAsync(fixture.Mount)).Count);
    }

    [Fact]
    public async Task ListsNestedFilesWithPatternsAndMetadata()
    {
        using var fixture = new DocumentFixture();
        await fixture.Files.WriteBytesAsync(Path.Combine(fixture.Mount, "nested", "cover.png"), [1, 2, 3]);
        await fixture.Files.WriteTextAsync(Path.Combine(fixture.Mount, "nested", "draft.txt"), "draft");
        await fixture.Files.WriteTextAsync(Path.Combine(fixture.Mount, "root.txt"), "root");
        Assert.Equal([Path.Combine(fixture.Mount, "root.txt")], await fixture.Files.GetFilesAsync(fixture.Mount, "*.txt"));
        Assert.Equal(2, (await fixture.Files.GetFilesAsync(fixture.Mount, "*.txt", true)).Count);
        Assert.Equal([Path.Combine(fixture.Mount, "nested")], await fixture.Files.GetDirectoriesAsync(fixture.Mount));
        Assert.Equal(3, await fixture.Files.GetFileSizeAsync(Path.Combine(fixture.Mount, "nested", "cover.png")));
        Assert.Equal([1, 2, 3], await fixture.Files.ReadBytesAsync(Path.Combine(fixture.Mount, "nested", "cover.png")));
        Assert.Equal(DateTimeKind.Utc, (await fixture.Files.GetLastWriteTimeUtcAsync(Path.Combine(fixture.Mount, "root.txt"))).Kind);
    }

    [Fact]
    public async Task MovesFilesAndDirectoriesAcrossProviderAndLocalStorage()
    {
        using var fixture = new DocumentFixture();
        var source = Path.Combine(fixture.Mount, "book", "draft.txt");
        await fixture.Files.WriteTextAsync(source, "draft");
        await fixture.Files.MoveFileAsync(source, fixture.Local.Combine("draft.txt"));
        Assert.False(await fixture.Files.ExistsAsync(source));
        Assert.Equal("draft", File.ReadAllText(fixture.Local.Combine("draft.txt")));
        await fixture.Files.MoveFileAsync(fixture.Local.Combine("draft.txt"), source);
        await fixture.Files.MoveDirectoryAsync(Path.Combine(fixture.Mount, "book"), Path.Combine(fixture.Mount, "renamed"));
        Assert.False(await fixture.Files.DirectoryExistsAsync(Path.Combine(fixture.Mount, "book")));
        Assert.Equal("draft", await fixture.Files.ReadTextAsync(Path.Combine(fixture.Mount, "renamed", "draft.txt")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameParentMovesPublishByRenameWithoutRewritingFiles(bool directory)
    {
        using var fixture = new DocumentFixture();
        var source = Path.Combine(fixture.Mount, "staged");
        var destination = Path.Combine(fixture.Mount, "published");
        await fixture.Files.WriteTextAsync(directory ? Path.Combine(source, "draft.txt") : source, "completed draft");
        fixture.Tree.FailOperation = "write";

        if (directory) await fixture.Files.MoveDirectoryAsync(source, destination);
        else await fixture.Files.MoveFileAsync(source, destination);

        Assert.Equal("completed draft", await fixture.Files.ReadTextAsync(directory ? Path.Combine(destination, "draft.txt") : destination));
        Assert.False(await fixture.Files.ExistsAsync(source));
        Assert.False(await fixture.Files.DirectoryExistsAsync(source));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameParentRenameRefusesExistingDestination(bool directory)
    {
        using var fixture = new DocumentFixture();
        var source = Path.Combine(fixture.Mount, "staged");
        var destination = Path.Combine(fixture.Mount, "published");
        await fixture.Files.WriteTextAsync(directory ? Path.Combine(source, "draft.txt") : source, "source");
        await fixture.Files.WriteTextAsync(directory ? Path.Combine(destination, "draft.txt") : destination, "destination");

        await Assert.ThrowsAsync<IOException>(() => directory
            ? fixture.Files.MoveDirectoryAsync(source, destination) : fixture.Files.MoveFileAsync(source, destination));

        Assert.Equal("source", await fixture.Files.ReadTextAsync(directory ? Path.Combine(source, "draft.txt") : source));
        Assert.Equal("destination", await fixture.Files.ReadTextAsync(directory ? Path.Combine(destination, "draft.txt") : destination));
    }

    [Fact]
    public async Task ProviderChangedRenameNameCannotReportSaveSuccess()
    {
        using var fixture = new DocumentFixture();
        var path = Path.Combine(fixture.Mount, "draft.txt");
        await fixture.Files.WriteTextAsync(path, "original");
        fixture.Tree.FailOperation = "rename-name";
        await Assert.ThrowsAsync<IOException>(() => fixture.Files.WriteTextAsync(path, "replacement"));
        Assert.Equal("original", File.ReadAllText(fixture.Provider.Combine("draft.txt")));
        Assert.Equal("replacement", File.ReadAllText(fixture.Provider.Combine("draft.txt.adjusted")));
    }

    [Fact]
    public async Task DeletesRespectMissingPathsAndNonRecursiveDirectories()
    {
        using var fixture = new DocumentFixture();
        var folder = Path.Combine(fixture.Mount, "book");
        await fixture.Files.WriteTextAsync(Path.Combine(folder, "draft.txt"), "draft");
        await Assert.ThrowsAsync<IOException>(() => fixture.Files.DeleteDirectoryAsync(folder, false));
        await fixture.Files.DeleteDirectoryAsync(folder);
        Assert.False(await fixture.Files.DirectoryExistsAsync(folder));
        await fixture.Files.DeleteFileAsync(Path.Combine(folder, "absent.txt"));
        await Assert.ThrowsAsync<IOException>(() => fixture.Files.DeleteDirectoryAsync(fixture.Mount));
        await Assert.ThrowsAsync<IOException>(() => fixture.Files.MoveDirectoryAsync(fixture.Mount, fixture.Local.Combine("copy")));
        Assert.False(Directory.Exists(fixture.Local.Combine("copy")));
    }

    [Fact]
    public async Task ExistingMoveDestinationAndFailedCopyKeepSource()
    {
        using var fixture = new DocumentFixture();
        var source = Path.Combine(fixture.Mount, "source.txt");
        var target = Path.Combine(fixture.Mount, "target.txt");
        await fixture.Files.WriteTextAsync(source, "original");
        await fixture.Files.WriteTextAsync(target, "existing");
        await Assert.ThrowsAsync<IOException>(() => fixture.Files.MoveFileAsync(source, target));
        Assert.Equal("original", await fixture.Files.ReadTextAsync(source));
        Assert.Equal("existing", await fixture.Files.ReadTextAsync(target));
        fixture.Tree.FailOperation = "write";
        await Assert.ThrowsAsync<IOException>(() => fixture.Files.MoveFileAsync(source, Path.Combine(fixture.Mount, "nested", "new.txt")));
        Assert.Equal("original", await fixture.Files.ReadTextAsync(source));
    }

    [Fact]
    public async Task InvalidProviderNamesCannotEscapeTheMount()
    {
        using var fixture = new DocumentFixture();
        fixture.Tree.InvalidName = true;
        await Assert.ThrowsAsync<IOException>(() => fixture.Files.GetDirectoriesAsync(fixture.Mount));
        await Assert.ThrowsAsync<IOException>(() => fixture.Files.GetFilesAsync(fixture.Mount, "*", true));
    }

    [Fact]
    public async Task DuplicateProviderNamesFailInsteadOfChoosingOrOverwritingOne()
    {
        using var fixture = new DocumentFixture();
        fixture.Tree.DuplicateNames = true;
        await Assert.ThrowsAsync<IOException>(() => fixture.Files.GetFilesAsync(fixture.Mount));
        await Assert.ThrowsAsync<IOException>(() => fixture.Files.WriteTextAsync(Path.Combine(fixture.Mount, "duplicate.txt"), "replacement"));
        Assert.Empty(Directory.GetFiles(fixture.Provider.Path));
    }

    [Fact]
    public async Task MissingFilesFoldersAndWrongKindsHaveFileSystemSemantics()
    {
        using var fixture = new DocumentFixture();
        var missing = Path.Combine(fixture.Mount, "missing");
        Assert.False(await fixture.Files.ExistsAsync(missing));
        Assert.False(await fixture.Files.DirectoryExistsAsync(missing));
        Assert.Empty(await fixture.Files.GetFilesAsync(missing));
        Assert.Empty(await fixture.Files.GetDirectoriesAsync(missing));
        await Assert.ThrowsAsync<FileNotFoundException>(() => fixture.Files.ReadBytesAsync(missing));
        await fixture.Files.WriteTextAsync(missing, "file");
        await Assert.ThrowsAsync<IOException>(() => fixture.Files.CreateDirectoryAsync(missing));
        await Assert.ThrowsAsync<IOException>(() => fixture.Files.DeleteDirectoryAsync(missing));
        await fixture.Files.CreateDirectoryAsync(Path.Combine(fixture.Mount, "folder"));
        await Assert.ThrowsAsync<IOException>(() => fixture.Files.WriteTextAsync(Path.Combine(fixture.Mount, "folder"), "file"));
        await Assert.ThrowsAsync<IOException>(() => fixture.Files.DeleteFileAsync(Path.Combine(fixture.Mount, "folder")));
    }

    private sealed class DocumentFixture : IDisposable
    {
        public TestClock Clock { get; } = new();
        public TempDir Local { get; } = new();
        public TempDir Provider { get; } = new();
        public TestDocumentTree Tree { get; }
        public string MountRoot => Local.Combine("Documents");
        public DocumentTreeMounts Mounts { get; }
        public DocumentTreeFileService Files { get; }
        public string Mount { get; }
        public DocumentFixture()
        {
            Tree = new TestDocumentTree(Provider.Path);
            Mounts = Restore();
            Mount = Mounts.Register("content://provider/tree/project");
            Files = new DocumentTreeFileService(Mounts, new FileService(), Clock);
        }
        public DocumentTreeMounts Restore() => new(MountRoot, _ => Tree, Clock);
        public DocumentTreeTransactions.PendingWrite PendingSave()
            => (Mounts.Resolve(Mount) ?? throw new IOException("Missing mount")).Transactions.Begin(Provider.Path, "draft.txt", []);
        public void Dispose() { Local.Dispose(); Provider.Dispose(); }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TestDocumentTree(string root) : IDocumentTree
    {
        public Func<string, byte[]>? ReadOverride { get; set; }
        public Func<string, IReadOnlyList<DocumentTreeEntry>>? GetChildrenOverride { get; set; }
        public int ListCalls { get; private set; }
        public string RootId => root;
        public bool Granted { get; set; } = true;
        public string? FailOperation { get; set; }
        public bool InvalidName { get; set; }
        public bool DuplicateNames { get; set; }
        public void EnsureAccess()
        {
            if (!Granted) throw new UnauthorizedAccessException("Revoked");
        }
        public IReadOnlyList<DocumentTreeEntry> GetChildren(string parentId)
        {
            ListCalls++;
            if (GetChildrenOverride != null) return GetChildrenOverride(parentId);
            return InvalidName ? [new DocumentTreeEntry("invalid", "../escape", true, 0, DateTime.UnixEpoch)]
                : DuplicateNames ? [new DocumentTreeEntry("one", "duplicate.txt", false, 0, DateTime.UnixEpoch), new DocumentTreeEntry("two", "duplicate.txt", false, 0, DateTime.UnixEpoch)]
                : Directory.GetFileSystemEntries(parentId).Select(Entry).ToArray();
        }
        public DocumentTreeEntry Create(string parentId, string name, bool directory)
        {
            var path = Path.Combine(parentId, name);
            if (directory) Directory.CreateDirectory(path);
            else File.WriteAllBytes(path, []);
            return Entry(path);
        }
        public byte[] Read(string id) => ReadOverride?.Invoke(id) ?? File.ReadAllBytes(id);
        public void Write(string id, byte[] bytes)
        {
            File.WriteAllBytes(id, bytes);
            if (FailOperation == "write") throw new IOException("Interrupted write");
        }
        public DocumentTreeEntry Rename(string id, string name)
        {
            if (FailOperation is "promote" or "rollback" && Path.GetFileName(id).StartsWith(".novalist-write-", StringComparison.Ordinal))
                throw new IOException("Promotion failed");
            if (FailOperation == "rollback" && Path.GetFileName(id).StartsWith(".novalist-backup-", StringComparison.Ordinal))
                throw new IOException("Rollback failed");
            if (FailOperation == "rename-name" && Path.GetFileName(id).StartsWith(".novalist-write-", StringComparison.Ordinal))
                name += ".adjusted";
            var path = Path.Combine(Path.GetDirectoryName(id) ?? throw new IOException("Missing parent"), name);
            if (Directory.Exists(id)) Directory.Move(id, path);
            else File.Move(id, path);
            return Entry(path);
        }
        public void Delete(string id)
        {
            if (Directory.Exists(id)) Directory.Delete(id, true);
            else File.Delete(id);
        }
        private static DocumentTreeEntry Entry(string path)
            => new(path, Path.GetFileName(path), Directory.Exists(path), File.Exists(path) ? new FileInfo(path).Length : 0, File.GetLastWriteTimeUtc(path));
    }
}
