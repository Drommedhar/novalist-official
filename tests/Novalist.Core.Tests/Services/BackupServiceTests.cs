using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class BackupServiceTests
{
    private const string Root = "/projects/MyNovel";
    private const string BackupRoot = "/appdata/Backups";

    /// <summary>
    /// Records what it was asked to archive and writes a placeholder through the
    /// in-memory file service so size and listing behave like the real thing.
    /// </summary>
    private sealed class FakeArchiveService : IArchiveService
    {
        private readonly InMemoryFileService _files;
        public readonly List<string> Created = new();
        public readonly List<(string Zip, string Dest)> Extracted = new();
        public IReadOnlyCollection<string> LastExcludes = Array.Empty<string>();

        public FakeArchiveService(InMemoryFileService files) => _files = files;

        public async Task<int> CreateFromDirectoryAsync(
            string sourceDirectory, string destinationZipPath, IReadOnlyCollection<string> excludedDirectoryNames)
        {
            LastExcludes = excludedDirectoryNames;
            Created.Add(destinationZipPath);
            await _files.WriteTextAsync(destinationZipPath, new string('z', 128));
            return 3;
        }

        public Task<int> ExtractToDirectoryAsync(string zipPath, string destinationDirectory)
        {
            Extracted.Add((zipPath, destinationDirectory));
            return Task.FromResult(3);
        }
    }

    private sealed class StubSettings : ISettingsService
    {
        public AppSettings Settings { get; } = new();
        public IEffectiveSettings Effective => Settings;
        public void SetActiveOverrides(SettingsOverrides? overrides) { }
        public Task LoadAsync() => Task.CompletedTask;
        public Task SaveAsync() => Task.CompletedTask;
        public void AddRecentProject(string name, string path, string coverImagePath = "") { }
        public void RemoveRecentProject(string path) { }
    }

    private static (BackupService Sut, InMemoryFileService Files, FakeArchiveService Archive, StubSettings Settings)
        Build(string? root = Root)
    {
        var project = Substitute.For<IProjectService>();
        project.ProjectRoot.Returns(root);
        var files = new InMemoryFileService();
        var archive = new FakeArchiveService(files);
        var settings = new StubSettings();
        var sut = new BackupService(project, files, archive, settings, BackupRoot);
        return (sut, files, archive, settings);
    }

    [Fact]
    public async Task CreateAsync_WritesArchiveUnderProjectNamedFolder()
    {
        var (sut, _, archive, _) = Build();

        var info = await sut.CreateAsync("manual");

        Assert.NotNull(info);
        Assert.Equal("manual", info!.Trigger);
        Assert.Equal(128, info.SizeBytes);
        Assert.Contains("MyNovel", info.Path);
        Assert.Single(archive.Created);
    }

    [Fact]
    public async Task CreateAsync_ExcludesGitDirectory()
    {
        var (sut, _, archive, _) = Build();
        await sut.CreateAsync("manual");
        Assert.Contains(".git", archive.LastExcludes);
    }

    [Fact]
    public async Task CreateAsync_NoProjectOpen_ReturnsNull()
    {
        var (sut, _, archive, _) = Build(root: null);
        Assert.Null(await sut.CreateAsync("manual"));
        Assert.Empty(archive.Created);
    }

    [Fact]
    public async Task CreateAsync_Disabled_ReturnsNull()
    {
        var (sut, _, archive, settings) = Build();
        settings.Settings.BackupEnabled = false;
        Assert.Null(await sut.CreateAsync("manual"));
        Assert.Empty(archive.Created);
    }

    [Theory]
    [InlineData("", "manual")]
    [InlineData("   ", "manual")]
    [InlineData("!!!", "manual")]
    [InlineData("On Open", "onopen")]
    [InlineData("INTERVAL", "interval")]
    public async Task CreateAsync_NormalisesTrigger(string given, string expected)
    {
        var (sut, _, _, _) = Build();
        var info = await sut.CreateAsync(given);
        Assert.Equal(expected, info!.Trigger);
    }

    [Fact]
    public async Task CreateAsync_UsesConfiguredFolderWhenSet()
    {
        var (sut, _, _, settings) = Build();
        settings.Settings.BackupFolder = "/elsewhere";
        var info = await sut.CreateAsync("manual");
        Assert.StartsWith(Path.Combine("/elsewhere", "MyNovel"), info!.Path);
    }

    [Fact]
    public async Task ListAsync_NoFolder_ReturnsEmpty()
    {
        var (sut, _, _, _) = Build();
        Assert.Empty(await sut.ListAsync());
    }

    [Fact]
    public async Task ListAsync_NoProject_ReturnsEmpty()
    {
        var (sut, _, _, _) = Build(root: null);
        Assert.Empty(await sut.ListAsync());
    }

    [Fact]
    public async Task ListAsync_OrdersNewestFirstAndSkipsForeignFiles()
    {
        var (sut, files, _, _) = Build();
        var folder = sut.GetBackupFolder()!;
        await files.CreateDirectoryAsync(folder);
        await files.WriteTextAsync(files.CombinePath(folder, "20260101-100000-manual.zip"), "a");
        await files.WriteTextAsync(files.CombinePath(folder, "20260301-100000-interval.zip"), "bb");
        await files.WriteTextAsync(files.CombinePath(folder, "not-a-backup.zip"), "x");

        var list = await sut.ListAsync();

        Assert.Equal(2, list.Count);
        Assert.Equal("20260301-100000-interval", list[0].Id);
        Assert.Equal("interval", list[0].Trigger);
        Assert.Equal("manual", list[1].Trigger);
    }

    [Fact]
    public async Task PruneAsync_KeepsRetentionCountNewest()
    {
        var (sut, files, _, settings) = Build();
        settings.Settings.BackupRetentionCount = 2;
        var folder = sut.GetBackupFolder()!;
        await files.CreateDirectoryAsync(folder);
        foreach (var stamp in new[] { "20260101", "20260102", "20260103", "20260104" })
            await files.WriteTextAsync(files.CombinePath(folder, $"{stamp}-100000-manual.zip"), "x");

        await sut.PruneAsync();

        var list = await sut.ListAsync();
        Assert.Equal(2, list.Count);
        Assert.Equal("20260104-100000-manual", list[0].Id);
        Assert.Equal("20260103-100000-manual", list[1].Id);
    }

    [Fact]
    public async Task PruneAsync_UnderRetention_DeletesNothing()
    {
        var (sut, files, _, settings) = Build();
        settings.Settings.BackupRetentionCount = 5;
        var folder = sut.GetBackupFolder()!;
        await files.CreateDirectoryAsync(folder);
        await files.WriteTextAsync(files.CombinePath(folder, "20260101-100000-manual.zip"), "x");

        await sut.PruneAsync();

        Assert.Single(await sut.ListAsync());
    }

    [Fact]
    public async Task CreateAsync_PrunesAfterWriting()
    {
        var (sut, files, _, settings) = Build();
        settings.Settings.BackupRetentionCount = 1;
        var folder = sut.GetBackupFolder()!;
        await files.CreateDirectoryAsync(folder);
        await files.WriteTextAsync(files.CombinePath(folder, "20200101-100000-manual.zip"), "old");

        await sut.CreateAsync("manual");

        Assert.Single(await sut.ListAsync());
    }

    [Fact]
    public async Task RestoreAsync_ExtractsOverProjectAndArchivesFirst()
    {
        var (sut, files, archive, _) = Build();
        var folder = sut.GetBackupFolder()!;
        await files.CreateDirectoryAsync(folder);
        var id = "20260101-100000-manual";
        await files.WriteTextAsync(files.CombinePath(folder, id + ".zip"), "x");

        Assert.True(await sut.RestoreAsync(id));

        Assert.Single(archive.Extracted);
        Assert.Equal(Root, archive.Extracted[0].Dest);
        Assert.Contains(archive.Created, p => p.Contains("prerestore"));
    }

    [Fact]
    public async Task RestoreAsync_UnknownId_ReturnsFalse()
    {
        var (sut, _, archive, _) = Build();
        Assert.False(await sut.RestoreAsync("nope"));
        Assert.Empty(archive.Extracted);
    }

    [Fact]
    public async Task RestoreAsync_NoProject_ReturnsFalse()
    {
        var (sut, _, _, _) = Build(root: null);
        Assert.False(await sut.RestoreAsync("anything"));
    }

    [Fact]
    public async Task IsDueAsync_NoBackupsYet_IsTrue()
    {
        var (sut, _, _, _) = Build();
        Assert.True(await sut.IsDueAsync(DateTime.UtcNow));
    }

    [Fact]
    public async Task IsDueAsync_Disabled_IsFalse()
    {
        var (sut, _, _, settings) = Build();
        settings.Settings.BackupEnabled = false;
        Assert.False(await sut.IsDueAsync(DateTime.UtcNow));
    }

    [Fact]
    public async Task IsDueAsync_ZeroInterval_IsFalse()
    {
        var (sut, _, _, settings) = Build();
        settings.Settings.BackupIntervalMinutes = 0;
        Assert.False(await sut.IsDueAsync(DateTime.UtcNow));
    }

    [Fact]
    public async Task IsDueAsync_RespectsElapsedTime()
    {
        var (sut, files, _, settings) = Build();
        settings.Settings.BackupIntervalMinutes = 30;
        var folder = sut.GetBackupFolder()!;
        await files.CreateDirectoryAsync(folder);
        await files.WriteTextAsync(files.CombinePath(folder, "20260101-120000-manual.zip"), "x");

        var last = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(await sut.IsDueAsync(last.AddMinutes(29)));
        Assert.True(await sut.IsDueAsync(last.AddMinutes(30)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    [InlineData(1, BackupService.MinIntervalMinutes)]
    [InlineData(30, 30)]
    [InlineData(99999, BackupService.MaxIntervalMinutes)]
    public void EffectiveInterval_IsClamped(int configured, int expected)
    {
        var (sut, _, _, settings) = Build();
        settings.Settings.BackupIntervalMinutes = configured;
        Assert.Equal(expected, sut.EffectiveInterval);
    }

    [Theory]
    [InlineData(0, BackupService.MinRetention)]
    [InlineData(-3, BackupService.MinRetention)]
    [InlineData(5, 5)]
    [InlineData(9999, BackupService.MaxRetention)]
    public void EffectiveRetention_IsClamped(int configured, int expected)
    {
        var (sut, _, _, settings) = Build();
        settings.Settings.BackupRetentionCount = configured;
        Assert.Equal(expected, sut.EffectiveRetention);
    }

    [Fact]
    public void GetBackupFolder_NoProject_IsNull()
    {
        var (sut, _, _, _) = Build(root: null);
        Assert.Null(sut.GetBackupFolder());
    }

    [Fact]
    public void GetBackupFolder_BlankProjectName_FallsBackToProject()
    {
        var project = Substitute.For<IProjectService>();
        project.ProjectRoot.Returns("/");
        var files = new InMemoryFileService();
        var sut = new BackupService(project, files, new FakeArchiveService(files), new StubSettings(), BackupRoot);
        Assert.Equal(Path.Combine(BackupRoot, "Project"), sut.GetBackupFolder());
    }

    [Fact]
    public void Constructor_WithoutRoot_UsesAppDataDefault()
    {
        var project = Substitute.For<IProjectService>();
        project.ProjectRoot.Returns(Root);
        var files = new InMemoryFileService();
        var sut = new BackupService(project, files, new FakeArchiveService(files), new StubSettings());
        Assert.Contains("Backups", sut.GetBackupFolder());
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("notadate-000000-manual")]
    public void ParseStamp_RejectsMalformed(string id) => Assert.Null(BackupService.ParseStamp(id));

    [Fact]
    public void ParseStamp_ReadsUtcTimestamp()
    {
        var parsed = BackupService.ParseStamp("20260714-093000-interval");
        Assert.Equal(new DateTime(2026, 7, 14, 9, 30, 0, DateTimeKind.Utc), parsed);
    }
}
