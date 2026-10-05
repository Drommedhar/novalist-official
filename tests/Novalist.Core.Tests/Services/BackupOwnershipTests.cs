using System.Security.Cryptography;
using System.Text;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public sealed class BackupOwnershipTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FileService _files = new();
    private readonly AppSettings _settings = new() { BackupEnabled = true, BackupRetentionCount = 1 };

    public BackupOwnershipTests() => _settings.BackupFolder = _dir.Combine("backups");
    public void Dispose() => _dir.Dispose();

    private BackupService Service(IProjectService project, IFileService? files = null, IArchiveService? archive = null)
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Settings.Returns(_settings);
        return new BackupService(project, files ?? _files, archive ?? new ArchiveService(), settings);
    }

    private async Task<ProjectService> ProjectAsync(string parent)
    {
        var project = new ProjectService(_files);
        await project.CreateProjectAsync(_dir.Combine(parent), "Novel", "Book");
        return project;
    }

    private static string Owner(string root)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return OperatingSystem.IsWindows() ? full.ToUpperInvariant() : full;
    }

    private static string Namespace(string root) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Owner(root)))).ToLowerInvariant();

    [Fact]
    public async Task SameNamedCopiedProjectsKeepSeparateListsRetentionRestoreAndDelete()
    {
        var first = await ProjectAsync("one");
        var second = await ProjectAsync("two");
        second.CurrentProject!.Id = first.CurrentProject!.Id;
        await second.SaveProjectAsync();
        var a = Service(first);
        var b = Service(second);

        var older = (await a.CreateAsync("first"))!;
        var other = (await b.CreateAsync("second"))!;
        var newer = (await a.CreateAsync("latest"))!;

        Assert.Equal(Path.Combine(_settings.BackupFolder, "Novel"), a.GetBackupFolder());
        Assert.NotEqual(a.GetBackupFolder(), b.GetBackupFolder());
        Assert.Equal(newer.Id, Assert.Single(await a.ListAsync()).Id);
        Assert.Equal(other.Id, Assert.Single(await b.ListAsync()).Id);
        Assert.False(File.Exists(older.Path));
        Assert.True(File.Exists(other.Path));
        Assert.False(await a.DeleteAsync(other.Id));
        Assert.False(await a.RestoreAsync(other.Id));
        Assert.True(File.Exists(other.Path));
        Assert.Equal(b.GetBackupFolder(), Service(second).GetBackupFolder());
    }

    [Fact]
    public async Task LegacyArchivesRemainUnownedUntouchedAndRecoverable()
    {
        var project = await ProjectAsync("one");
        var preferred = Path.Combine(_settings.BackupFolder, "Novel");
        Directory.CreateDirectory(preferred);
        var legacy = Path.Combine(preferred, "20260101-120000-manual.zip");
        await new ArchiveService().CreateFromDirectoryAsync(project.ProjectRoot!, legacy, BackupService.ExcludedDirectories);
        var bytes = File.ReadAllBytes(legacy);
        var service = Service(project);

        Assert.Empty(await service.ListAsync());
        var owned = service.GetBackupFolder();
        Assert.NotEqual(preferred, owned);
        await service.CreateAsync("manual");
        await service.CreateAsync("latest");
        await service.PruneAsync();
        Assert.Single(await service.ListAsync());
        Assert.False(await service.DeleteAsync("20260101-120000-manual"));
        Assert.False(await service.RestoreAsync("20260101-120000-manual"));
        Assert.Equal(bytes, File.ReadAllBytes(legacy));
        Assert.False(File.Exists(Path.Combine(preferred, BackupStorage.OwnerFileName)));

        var restored = await service.RestoreAsNewProjectAsync(legacy, _dir.Path, "Recovered");
        var copy = new ProjectService(_files);
        await copy.LoadProjectAsync(restored);
        Assert.Equal("Recovered", copy.CurrentProject!.Name);
        Assert.NotEqual(project.CurrentProject!.Id, copy.CurrentProject.Id);
        Assert.Equal(bytes, File.ReadAllBytes(legacy));
    }

    [Fact]
    public async Task ExistingUnmarkedNamespacesArePreservedAndEstablishedOwnershipSurvivesLegacyCleanup()
    {
        var project = await ProjectAsync("one");
        var preferred = Path.Combine(_settings.BackupFolder, "Novel");
        var unowned = Path.Combine(preferred, Namespace(project.ProjectRoot!));
        Directory.CreateDirectory(unowned);
        var legacy = Path.Combine(unowned, "20260101-120000-manual.zip");
        File.WriteAllText(legacy, "Keep this unowned archive");
        var service = Service(project);
        var owned = service.GetBackupFolder();

        Assert.Equal(unowned + "-2", owned);
        await service.CreateAsync("manual");
        Assert.Equal("Keep this unowned archive", File.ReadAllText(legacy));
        File.Delete(legacy);
        Assert.Equal(owned, Service(project).GetBackupFolder());
        Assert.Single(await Service(project).ListAsync());
    }

    [Fact]
    public async Task ExistingEmptyDirectoryIsNotImplicitlyAssignedToAProject()
    {
        var project = await ProjectAsync("one");
        var preferred = Path.Combine(_settings.BackupFolder, "Novel");
        Directory.CreateDirectory(preferred);
        var owned = Service(project).GetBackupFolder();

        Assert.Equal(Path.Combine(preferred, Namespace(project.ProjectRoot!)), owned);
        Assert.False(File.Exists(Path.Combine(preferred, BackupStorage.OwnerFileName)));
        Assert.Equal(Owner(project.ProjectRoot!), File.ReadAllText(Path.Combine(owned!, BackupStorage.OwnerFileName)));
    }

    [Fact]
    public async Task CanonicalPathsReuseOwnershipAndMovedBackupRootRetainsHistory()
    {
        var project = await ProjectAsync("one");
        var original = Service(project);
        var backup = (await original.CreateAsync("manual"))!;
        var equivalent = Substitute.For<IProjectService>();
        equivalent.ProjectRoot.Returns(Path.Combine(project.ProjectRoot!, ".") + Path.DirectorySeparatorChar);
        Assert.Equal(original.GetBackupFolder(), Service(equivalent).GetBackupFolder());

        var relocated = _dir.Combine("relocated");
        Directory.Move(_settings.BackupFolder, relocated);
        _settings.BackupFolder = relocated;
        var reopened = Service(project);
        Assert.Equal(Path.Combine(relocated, "Novel"), reopened.GetBackupFolder());
        Assert.Equal(backup.Id, Assert.Single(await reopened.ListAsync()).Id);
    }

    [Fact]
    public async Task ConcurrentResolversReserveDifferentSameNamedProjects()
    {
        var first = await ProjectAsync("one");
        var second = await ProjectAsync("two");
        var preferred = Path.Combine(_settings.BackupFolder, "Novel");
        var folders = await Task.WhenAll(
            new BackupStorage(_files).ResolveAsync(first.ProjectRoot!, preferred),
            new BackupStorage(_files).ResolveAsync(second.ProjectRoot!, preferred));

        Assert.NotEqual(folders[0], folders[1]);
        Assert.Equal(Owner(first.ProjectRoot!), File.ReadAllText(Path.Combine(folders[0], BackupStorage.OwnerFileName)));
        Assert.Equal(Owner(second.ProjectRoot!), File.ReadAllText(Path.Combine(folders[1], BackupStorage.OwnerFileName)));
        Assert.Empty(Directory.GetDirectories(_settings.BackupFolder, ".novalist-backup-claim-*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AtomicReservationRespectsTheWinningProcess(bool sameOwner)
    {
        var project = await ProjectAsync("one");
        var preferred = Path.Combine(_settings.BackupFolder, "Novel");
        var files = new InterceptingFiles(_files);
        files.BeforeMove = (_, destination) =>
        {
            files.BeforeMove = null;
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, BackupStorage.OwnerFileName),
                sameOwner ? Owner(project.ProjectRoot!) : "another-project");
            File.WriteAllText(Path.Combine(destination, "20260101-120000-manual.zip"), "Winner's archive");
        };

        var owned = await new BackupStorage(files).ResolveAsync(project.ProjectRoot!, preferred);

        Assert.Equal(sameOwner, string.Equals(preferred, owned, StringComparison.Ordinal));
        Assert.Equal("Winner's archive", File.ReadAllText(Path.Combine(preferred, "20260101-120000-manual.zip")));
        Assert.Empty(Directory.GetDirectories(_settings.BackupFolder, ".novalist-backup-claim-*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ReservationRecognizesOwnershipEstablishedAfterInitialDiscovery()
    {
        var project = await ProjectAsync("one");
        var preferred = Path.Combine(_settings.BackupFolder, "Novel");
        var marker = Path.Combine(preferred, BackupStorage.OwnerFileName);
        var files = new InterceptingFiles(_files);
        files.AfterExists = (path, exists) =>
        {
            if (path != marker || exists) return;
            files.AfterExists = null;
            Directory.CreateDirectory(preferred);
            File.WriteAllText(marker, Owner(project.ProjectRoot!));
        };

        Assert.Equal(preferred, await new BackupStorage(files).ResolveAsync(project.ProjectRoot!, preferred));
        Assert.Empty(Directory.GetDirectories(_settings.BackupFolder, ".novalist-backup-claim-*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task FailedReservationCleansTemporaryDirectoryAndDoesNotSwallowStorageErrors()
    {
        var project = await ProjectAsync("one");
        var preferred = Path.Combine(_settings.BackupFolder, "Novel");
        var files = new InterceptingFiles(_files)
        {
            BeforeMove = (_, _) => throw new IOException("Storage unavailable")
        };

        var error = await Assert.ThrowsAsync<IOException>(() =>
            new BackupStorage(files).ResolveAsync(project.ProjectRoot!, preferred));

        Assert.Equal("Storage unavailable", error.Message);
        Assert.False(Directory.Exists(preferred));
        Assert.Empty(Directory.GetDirectories(_settings.BackupFolder));
        Assert.Equal(preferred, await new BackupStorage(_files).ResolveAsync(project.ProjectRoot!, preferred));
    }

    [Fact]
    public async Task ForeignMarkerInExpectedNamespaceIsNeverOverwritten()
    {
        var project = await ProjectAsync("one");
        var preferred = Path.Combine(_settings.BackupFolder, "Novel");
        var occupied = Path.Combine(preferred, Namespace(project.ProjectRoot!));
        Directory.CreateDirectory(occupied);
        var marker = Path.Combine(occupied, BackupStorage.OwnerFileName);
        File.WriteAllText(marker, "different canonical root");

        Assert.Equal(occupied + "-2", Service(project).GetBackupFolder());
        Assert.Equal("different canonical root", File.ReadAllText(marker));
    }

    [Fact]
    public async Task CreateRetainsItsCapturedProjectWhenWorkspaceChangesDuringArchiving()
    {
        var first = await ProjectAsync("one");
        var second = await ProjectAsync("two");
        var older = (await Service(first).CreateAsync("manual"))!;
        var other = (await Service(second).CreateAsync("other"))!;
        var current = Substitute.For<IProjectService>();
        current.ProjectRoot.Returns(first.ProjectRoot);
        var realArchive = new ArchiveService();
        var archive = Substitute.For<IArchiveService>();
        archive.CreateFromDirectoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<string>>())
            .Returns(async call =>
            {
                var count = await realArchive.CreateFromDirectoryAsync(call.ArgAt<string>(0), call.ArgAt<string>(1),
                    call.ArgAt<IReadOnlyCollection<string>>(2));
                current.ProjectRoot.Returns(second.ProjectRoot);
                return count;
            });

        var created = (await Service(current, archive: archive).CreateAsync("manual"))!;

        Assert.Equal(first.ProjectRoot, archive.ReceivedCalls().Single().GetArguments()[0]);
        Assert.False(File.Exists(older.Path));
        Assert.Equal(created.Id, Assert.Single(await Service(first).ListAsync()).Id);
        Assert.Equal(other.Id, Assert.Single(await Service(second).ListAsync()).Id);
    }

    [Fact]
    public async Task RestoreKeepsSafetyArchiveAndRetentionBoundToItsOriginalProject()
    {
        var first = await ProjectAsync("one");
        var second = await ProjectAsync("two");
        var firstText = Path.Combine(first.ProjectRoot!, "prose.txt");
        var secondText = Path.Combine(second.ProjectRoot!, "prose.txt");
        File.WriteAllText(firstText, "Original prose");
        File.WriteAllText(secondText, "Other project");
        var backup = (await Service(first).CreateAsync("manual"))!;
        var other = (await Service(second).CreateAsync("manual"))!;
        File.WriteAllText(firstText, "Later prose");
        var current = Substitute.For<IProjectService>();
        current.ProjectRoot.Returns(first.ProjectRoot);
        var files = new InterceptingFiles(_files)
        {
            AfterRead = (_) => current.ProjectRoot.Returns(second.ProjectRoot)
        };

        Assert.True(await Service(current, files).RestoreAsync(backup.Id));

        Assert.Equal("Original prose", File.ReadAllText(firstText));
        Assert.Equal("Other project", File.ReadAllText(secondText));
        Assert.True(File.Exists(other.Path));
        var safety = Assert.Single(await Service(first).ListAsync());
        Assert.Equal("prerestore", safety.Trigger);
        var recovered = _dir.Combine("safety-copy");
        await new ArchiveService().ExtractToDirectoryAsync(safety.Path, recovered);
        Assert.Equal("Later prose", File.ReadAllText(Path.Combine(recovered, "prose.txt")));
    }

    [Fact]
    public async Task InvalidNestedDestinationStaysSideEffectFreeForDiscoveryAndListing()
    {
        var project = await ProjectAsync("one");
        _settings.BackupFolder = project.ProjectRoot!;
        var service = Service(project);
        var destination = service.GetBackupFolder();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ListAsync());
        Assert.False(Directory.Exists(destination));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync("manual"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PruneAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RestoreAsync("20260101-120000-manual"));
        Assert.False(Directory.Exists(destination));
        await Service(Substitute.For<IProjectService>()).PruneAsync();
    }

    [Fact]
    public async Task ListingHandlesAnOwnedFolderRemovedAfterResolution()
    {
        var project = await ProjectAsync("one");
        var preferred = Service(project).GetBackupFolder()!;
        var files = new InterceptingFiles(_files);
        files.AfterRead = (path) =>
        {
            if (path != Path.Combine(preferred, BackupStorage.OwnerFileName)) return;
            Directory.Delete(preferred, recursive: true);
        };

        Assert.Empty(await Service(project, files).ListAsync());
        Assert.False(Directory.Exists(preferred));
    }

    [Fact]
    public async Task SynchronousFolderDiscoveryDoesNotCaptureTheCallingContext()
    {
        var project = await ProjectAsync("one");
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new RejectingContext());
            Assert.NotNull(Service(project).GetBackupFolder());
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed class RejectingContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) =>
            throw new InvalidOperationException("Folder discovery captured the caller's context.");
    }

    private sealed class InterceptingFiles(IFileService inner) : IFileService
    {
        public Action<string, string>? BeforeMove { get; set; }
        public Action<string, bool>? AfterExists { get; set; }
        public Action<string>? AfterRead { get; set; }
        public Task MoveDirectoryAsync(string oldPath, string newPath)
        {
            BeforeMove?.Invoke(oldPath, newPath);
            return inner.MoveDirectoryAsync(oldPath, newPath);
        }
        public async Task<string> ReadTextAsync(string path)
        {
            var text = await inner.ReadTextAsync(path);
            AfterRead?.Invoke(path);
            return text;
        }
        public Task WriteTextAsync(string path, string content) => inner.WriteTextAsync(path, content);
        public Task<byte[]> ReadBytesAsync(string path) => inner.ReadBytesAsync(path);
        public Task WriteBytesAsync(string path, byte[] bytes) => inner.WriteBytesAsync(path, bytes);
        public async Task<bool> ExistsAsync(string path)
        {
            var exists = await inner.ExistsAsync(path);
            AfterExists?.Invoke(path, exists);
            return exists;
        }
        public Task<bool> DirectoryExistsAsync(string path) => inner.DirectoryExistsAsync(path);
        public Task CreateDirectoryAsync(string path) => inner.CreateDirectoryAsync(path);
        public Task<IReadOnlyList<string>> GetFilesAsync(string directory, string pattern = "*", bool recursive = false) =>
            inner.GetFilesAsync(directory, pattern, recursive);
        public Task<IReadOnlyList<string>> GetDirectoriesAsync(string directory) => inner.GetDirectoriesAsync(directory);
        public Task DeleteFileAsync(string path) => inner.DeleteFileAsync(path);
        public Task DeleteDirectoryAsync(string path, bool recursive = true) => inner.DeleteDirectoryAsync(path, recursive);
        public Task MoveFileAsync(string oldPath, string newPath) => inner.MoveFileAsync(oldPath, newPath);
        public Task<long> GetFileSizeAsync(string path) => inner.GetFileSizeAsync(path);
        public Task<DateTime> GetLastWriteTimeUtcAsync(string path) => inner.GetLastWriteTimeUtcAsync(path);
        public string CombinePath(params string[] parts) => inner.CombinePath(parts);
        public string GetFileName(string path) => inner.GetFileName(path);
        public string GetFileNameWithoutExtension(string path) => inner.GetFileNameWithoutExtension(path);
        public string GetDirectoryName(string path) => inner.GetDirectoryName(path);
    }
}
