using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>Whole-project archiving over the RPC surface.</summary>
public sealed class BackupRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly BackupRpc _rpc;

    public BackupRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "BackupNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        // Keep archives inside the temp tree so the suite never writes to %APPDATA%.
        // Persisted, not just set in memory: reopening a project reloads settings
        // from disk, which is what restore does.
        _workspace.Settings.Settings.BackupFolder = Path.Combine(_root, "backups");
        _workspace.Settings.SaveAsync().GetAwaiter().GetResult();
        _rpc = new BackupRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task Create_WritesArchiveAndListsIt()
    {
        var created = await _rpc.CreateAsync("manual");

        Assert.NotNull(created);
        Assert.Equal("manual", created!.Trigger);
        Assert.True(created.SizeBytes > 0);
        Assert.True(File.Exists(created.Path));

        var list = await _rpc.ListAsync();
        Assert.Single(list);
        Assert.Equal(created.Id, list[0].Id);
    }

    [Fact]
    public async Task Create_Disabled_ReturnsNull()
    {
        _workspace.Settings.Settings.BackupEnabled = false;
        Assert.Null(await _rpc.CreateAsync("manual"));
    }

    [Fact]
    public async Task Folder_ReturnsPerProjectDirectory()
    {
        var folder = await _rpc.FolderAsync();
        Assert.Contains("BackupNovel", folder);
    }

    [Fact]
    public async Task IsDue_TrueBeforeAnyBackup_FalseImmediatelyAfter()
    {
        Assert.True(await _rpc.IsDueAsync());
        await _rpc.CreateAsync("interval");
        Assert.False(await _rpc.IsDueAsync());
    }

    [Fact]
    public async Task Prune_TrimsToRetentionCount()
    {
        _workspace.Settings.Settings.BackupRetentionCount = 100;
        var folder = await _rpc.FolderAsync();
        Directory.CreateDirectory(folder);
        foreach (var stamp in new[] { "20260101", "20260102", "20260103" })
            File.WriteAllText(Path.Combine(folder, $"{stamp}-100000-manual.zip"), "x");

        _workspace.Settings.Settings.BackupRetentionCount = 2;
        var remaining = await _rpc.PruneAsync();

        Assert.Equal(2, remaining.Length);
        Assert.Equal("20260103-100000-manual", remaining[0].Id);
    }

    [Fact]
    public async Task Restore_BringsBackDeletedContentAndReopensProject()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        await _workspace.WriteSceneAsync(chapter.Guid, scene.Id, "<p>original</p>", "original");

        var backup = await _rpc.CreateAsync("manual");
        Assert.NotNull(backup);

        await _workspace.WriteSceneAsync(chapter.Guid, scene.Id, "<p>clobbered</p>", "clobbered");

        Assert.True(await _rpc.RestoreAsync(backup!.Id));

        var restoredChapter = _workspace.Projects.GetChaptersOrdered()
            .First(c => c.Guid == chapter.Guid);
        var restoredScene = _workspace.Projects.GetScenesForChapter(chapter.Guid)
            .First(s => s.Id == scene.Id);
        var restored = await _workspace.Projects.ReadSceneContentAsync(restoredChapter, restoredScene);
        Assert.Contains("original", restored);
    }

    [Fact]
    public async Task Restore_ArchivesCurrentStateFirst()
    {
        var backup = await _rpc.CreateAsync("manual");
        await _rpc.RestoreAsync(backup!.Id);

        var list = await _rpc.ListAsync();
        Assert.Contains(list, b => b.Trigger == "prerestore");
    }

    [Fact]
    public async Task Restore_UnknownId_ReturnsFalse()
    {
        Assert.False(await _rpc.RestoreAsync("20260101-000000-nope"));
    }

    [Fact]
    public async Task Restore_NoProjectOpen_ReturnsFalse()
    {
        using var bare = new Workspace(Path.Combine(_root, "settings2"));
        var rpc = new BackupRpc(bare);
        Assert.False(await rpc.RestoreAsync("anything"));
    }

    [Fact]
    public async Task CreateMilestone_NamesTheArchiveAndSurvivesPruning()
    {
        var milestone = await _rpc.CreateMilestoneAsync("First draft");

        Assert.NotNull(milestone);
        Assert.True(milestone!.IsMilestone);
        Assert.Equal("First draft", milestone.Name);

        // Retention of one, and an ordinary archive to fill it: the milestone
        // still has to be there afterwards.
        await _rpc.CreateAsync("manual");
        _workspace.Settings.Settings.BackupRetentionCount = 1;
        var remaining = await _rpc.PruneAsync();

        Assert.Contains(remaining, b => b.Id == milestone.Id);
    }

    [Fact]
    public async Task Delete_RemovesAMilestoneRetentionWouldNotTouch()
    {
        var milestone = await _rpc.CreateMilestoneAsync("Sent to agent");

        Assert.True(await _rpc.DeleteAsync(milestone!.Id));
        Assert.DoesNotContain(await _rpc.ListAsync(), b => b.Id == milestone.Id);
        Assert.False(File.Exists(milestone.Path));
    }

    [Fact]
    public async Task Delete_UnknownId_ReturnsFalse()
        => Assert.False(await _rpc.DeleteAsync("20260101-000000-nope"));

    [Fact]
    public async Task CreateMilestone_NoProjectOpen_ReturnsNull()
    {
        using var bare = new Workspace(Path.Combine(_root, "settings3"));
        Assert.Null(await new BackupRpc(bare).CreateMilestoneAsync("Anything"));
    }

    [Fact]
    public async Task Create_ExcludesGitDirectoryFromTheArchive()
    {
        var gitDir = Path.Combine(_workspace.Projects.ProjectRoot!, ".git");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/main");

        var backup = await _rpc.CreateAsync("manual");

        using var zip = System.IO.Compression.ZipFile.OpenRead(backup!.Path);
        Assert.DoesNotContain(zip.Entries, e => e.FullName.Contains(".git/"));
    }
}
