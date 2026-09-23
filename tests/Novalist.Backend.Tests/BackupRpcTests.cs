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
    public async Task Restore_KeptVersionAfterReopening_RestoresOnlyItsChaptersScenesCoverAndBanner()
    {
        // Regression for the Version 1 / Version 2 report. Use the real filesystem,
        // ZIP implementation and Keep Version RPC; no cloud sync is involved.
        var projects = _workspace.Projects;
        var projectRoot = projects.ProjectRoot!;
        var dashboard = new DashboardRpc(_workspace);
        var chapter1 = await projects.CreateChapterAsync("Chapter 1");
        var scene1 = await projects.CreateSceneAsync(chapter1.Guid, "Scene 1");
        await _workspace.WriteSceneAsync(chapter1.Guid, scene1.Id, "<p>Version 1 prose</p>", "Version 1 prose");
        var originalProse = await projects.ReadSceneContentAsync(chapter1, scene1);

        var cover1Source = Path.Combine(_root, "cover-v1.png");
        var banner1Source = Path.Combine(_root, "banner-v1.png");
        byte[] cover1Bytes = [0x89, 0x50, 0x4e, 0x47, 1];
        byte[] banner1Bytes = [0x89, 0x50, 0x4e, 0x47, 2];
        await File.WriteAllBytesAsync(cover1Source, cover1Bytes);
        await File.WriteAllBytesAsync(banner1Source, banner1Bytes);
        await dashboard.SetCoverAsync(cover1Source);
        await dashboard.SetBannerAsync(banner1Source);
        var cover1 = await dashboard.GetCoverAsync();
        var banner1 = await dashboard.GetBannerAsync();
        Assert.NotNull(cover1);
        Assert.NotNull(banner1);

        var version1 = await _rpc.CreateMilestoneAsync("Version 1");
        Assert.NotNull(version1);
        Assert.True(version1.IsMilestone);
        Assert.Equal("Version 1", version1.Name);

        var chapter2 = await projects.CreateChapterAsync("Chapter 2");
        var scene2 = await projects.CreateSceneAsync(chapter2.Guid, "Scene 2");
        await _workspace.WriteSceneAsync(chapter2.Guid, scene2.Id, "<p>Version 2 prose</p>", "Version 2 prose");
        var cover2Source = Path.Combine(_root, "cover-v2.png");
        var banner2Source = Path.Combine(_root, "banner-v2.png");
        await File.WriteAllBytesAsync(cover2Source, [0x89, 0x50, 0x4e, 0x47, 3]);
        await File.WriteAllBytesAsync(banner2Source, [0x89, 0x50, 0x4e, 0x47, 4]);
        await dashboard.SetCoverAsync(cover2Source);
        await dashboard.SetBannerAsync(banner2Source);
        var cover2 = await dashboard.GetCoverAsync();
        var banner2 = await dashboard.GetBannerAsync();
        Assert.NotEqual(cover1, cover2);
        Assert.NotEqual(banner1, banner2);

        var version2 = await _rpc.CreateMilestoneAsync("Version 2");
        Assert.NotNull(version2);
        Assert.True(version2.IsMilestone);
        Assert.Equal("Version 2", version2.Name);
        Assert.NotEqual(version1.Id, version2.Id);

        // Control: opening Version 1 in an empty folder must yield one chapter
        // and scene. This distinguishes a bad archive from an in-place restore bug.
        var cleanRoot = Path.Combine(_root, "clean-version-1");
        await _workspace.ArchiveService.ExtractToDirectoryAsync(version1.Path, cleanRoot);
        using (var clean = new Workspace(Path.Combine(_root, "clean-settings")))
        {
            await clean.OpenProjectAsync(cleanRoot);
            Assert.Equal(chapter1.Guid, Assert.Single(clean.Projects.GetChaptersOrdered()).Guid);
            Assert.Equal(scene1.Id, Assert.Single(clean.Projects.GetScenesForChapter(chapter1.Guid)).Id);
            Assert.Equal(originalProse, await clean.Projects.ReadSceneContentAsync(chapter1, scene1));
            Assert.Equal(cover1Bytes, await File.ReadAllBytesAsync(Path.Combine(cleanRoot, cover1)));
            Assert.Equal(banner1Bytes, await File.ReadAllBytesAsync(Path.Combine(cleanRoot, banner1)));
        }

        _workspace.CloseProject();
        Assert.False(projects.IsProjectLoaded);
        await _workspace.OpenProjectAsync(projectRoot);
        Assert.Equal(new[] { chapter1.Guid, chapter2.Guid }, projects.GetChaptersOrdered().Select(c => c.Guid));
        Assert.Equal(scene2.Id, Assert.Single(projects.GetScenesForChapter(chapter2.Guid)).Id);
        Assert.Equal(cover2, await dashboard.GetCoverAsync());
        Assert.Equal(banner2, await dashboard.GetBannerAsync());
        Assert.Contains(await _rpc.ListAsync(), b => b.Id == version1.Id);
        Assert.Contains(await _rpc.ListAsync(), b => b.Id == version2.Id);

        Assert.True(await _rpc.RestoreAsync(version1.Id));

        // Check the image references and bytes independently of the binder count:
        // a partial restore can bring back the artwork while retaining Chapter 2.
        Assert.Equal(cover1, await dashboard.GetCoverAsync());
        Assert.Equal(banner1, await dashboard.GetBannerAsync());
        Assert.Equal(cover1Bytes, await File.ReadAllBytesAsync(Path.Combine(projectRoot, cover1)));
        Assert.Equal(banner1Bytes, await File.ReadAllBytesAsync(Path.Combine(projectRoot, banner1)));
        Assert.False(File.Exists(Path.Combine(projectRoot, cover2!)));
        Assert.False(File.Exists(Path.Combine(projectRoot, banner2!)));
        Assert.Equal(originalProse, await projects.ReadSceneContentAsync(chapter1, scene1));
        Assert.Contains(await _rpc.ListAsync(), b => b.Trigger == "prerestore");

        var restoredChapters = projects.GetChaptersOrdered().Select(c => c.Guid).ToArray();
        var restoredScenes = projects.GetChaptersOrdered()
            .SelectMany(c => projects.GetScenesForChapter(c.Guid)).Select(s => s.Id).ToArray();
        _workspace.CloseProject();
        await _workspace.OpenProjectAsync(projectRoot);

        // Restoring an older version removes later chapters/scenes, including
        // after another close and reopen.
        Assert.Multiple(
            () => Assert.Equal(new[] { chapter1.Guid }, restoredChapters),
            () => Assert.Equal(new[] { scene1.Id }, restoredScenes),
            () => Assert.Equal(new[] { chapter1.Guid }, projects.GetChaptersOrdered().Select(c => c.Guid)),
            () => Assert.Equal(new[] { scene1.Id }, projects.GetChaptersOrdered()
                .SelectMany(c => projects.GetScenesForChapter(c.Guid)).Select(s => s.Id)));
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
    public async Task Restore_PreservesGitAndArchivesCurrentStateEvenWhenAutomaticBackupsAreOff()
    {
        var projects = _workspace.Projects;
        var first = await projects.CreateChapterAsync("First");
        var backup = await _rpc.CreateMilestoneAsync("First version");
        var later = await projects.CreateChapterAsync("Later");
        var git = Path.Combine(projects.ProjectRoot!, ".git", "HEAD");
        Directory.CreateDirectory(Path.GetDirectoryName(git)!);
        await File.WriteAllTextAsync(git, "ref: refs/heads/main");
        _workspace.Settings.Settings.BackupEnabled = false;
        await _workspace.Settings.SaveAsync();

        Assert.True(await _rpc.RestoreAsync(backup!.Id));

        Assert.Equal(first.Guid, Assert.Single(projects.GetChaptersOrdered()).Guid);
        Assert.Equal("ref: refs/heads/main", await File.ReadAllTextAsync(git));
        var safety = Assert.Single(await _rpc.ListAsync(), b => b.Trigger == "prerestore");
        var safetyRoot = Path.Combine(_root, "safety-copy");
        await _workspace.ArchiveService.ExtractToDirectoryAsync(safety.Path, safetyRoot);
        using var copy = new Workspace(Path.Combine(_root, "safety-settings"));
        await copy.OpenProjectAsync(safetyRoot);
        Assert.Equal(new[] { first.Guid, later.Guid }, copy.Projects.GetChaptersOrdered().Select(c => c.Guid));
    }

    [Fact]
    public async Task Restore_WithRetentionOne_DoesNotPruneTheTargetBeforeReadingIt()
    {
        var first = await _workspace.Projects.CreateChapterAsync("First");
        var backup = await _rpc.CreateAsync("manual");
        await _workspace.Projects.CreateChapterAsync("Later");
        _workspace.Settings.Settings.BackupRetentionCount = 1;
        await _workspace.Settings.SaveAsync();

        Assert.True(await _rpc.RestoreAsync(backup!.Id));

        Assert.Equal(first.Guid, Assert.Single(_workspace.Projects.GetChaptersOrdered()).Guid);
        Assert.Equal("prerestore", Assert.Single(await _rpc.ListAsync()).Trigger);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_InvalidArchive_LeavesExistingProjectFilesUntouched(bool validZip)
    {
        await _workspace.Projects.CreateChapterAsync("Keep me");
        var backup = await _rpc.CreateMilestoneAsync("Broken");
        if (validZip)
        {
            using var zip = System.IO.Compression.ZipFile.Open(backup!.Path, System.IO.Compression.ZipArchiveMode.Update);
            zip.GetEntry(".novalist/project.json")!.Delete();
        }
        else await File.WriteAllTextAsync(backup!.Path, "truncated ZIP");
        var before = Snapshot(_workspace.Projects.ProjectRoot!);

        await Assert.ThrowsAsync<InvalidDataException>(() => _rpc.RestoreAsync(backup.Id));

        AssertSnapshot(before, _workspace.Projects.ProjectRoot!);
        Assert.Contains(await _rpc.ListAsync(), b => b.Trigger == "prerestore");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestoreAsNewProject_OpensIndependentCopy_AndLeavesOriginalUntouched(bool closeOriginal)
    {
        var projects = _workspace.Projects;
        var originalRoot = projects.ProjectRoot!;
        var originalId = projects.CurrentProject!.Id;
        var chapter = await projects.CreateChapterAsync("First");
        var scene = await projects.CreateSceneAsync(chapter.Guid, "Original scene");
        await _workspace.WriteSceneAsync(chapter.Guid, scene.Id, "<p>Original prose</p>", "Original prose");
        var image = Path.Combine(_root, "cover.png");
        await File.WriteAllBytesAsync(image, [0x89, 0x50, 1]);
        var dashboard = new DashboardRpc(_workspace);
        await dashboard.SetCoverAsync(image);
        await dashboard.SetBannerAsync(image);
        var cover = await dashboard.GetCoverAsync();
        var backup = await _rpc.CreateMilestoneAsync("Version 1");
        await projects.CreateChapterAsync("Later");
        var before = Snapshot(originalRoot);
        if (closeOriginal) _workspace.CloseProject();

        var state = await _rpc.RestoreAsNewProjectAsync(backup!.Path, _root, "Restored copy");

        Assert.True(state.IsLoaded);
        Assert.Equal("Restored copy", state.ProjectName);
        Assert.NotEqual(originalId, projects.CurrentProject!.Id);
        Assert.Equal(Path.Combine(_root, "Restored copy"), state.ProjectPath);
        Assert.Equal(chapter.Guid, Assert.Single(projects.GetChaptersOrdered()).Guid);
        Assert.Equal(scene.Id, Assert.Single(projects.GetScenesForChapter(chapter.Guid)).Id);
        Assert.Contains("Original prose", await projects.ReadSceneContentAsync(chapter, scene));
        Assert.Equal(cover, await dashboard.GetCoverAsync());
        Assert.Equal(cover, await dashboard.GetBannerAsync());
        Assert.Equal(await File.ReadAllBytesAsync(image), await File.ReadAllBytesAsync(Path.Combine(state.ProjectPath!, cover!)));
        AssertSnapshot(before, originalRoot);
        Assert.True(File.Exists(backup.Path));

        await _workspace.WriteSceneAsync(chapter.Guid, scene.Id, "<p>Copy changed</p>", "Copy changed");
        AssertSnapshot(before, originalRoot);
    }

    [Fact]
    public async Task RestoreAsNewProject_RefusesExistingDestinationWithoutChangingEitherProject()
    {
        var backup = await _rpc.CreateMilestoneAsync("Version 1");
        var root = _workspace.Projects.ProjectRoot!;
        var before = Snapshot(root);

        await Assert.ThrowsAsync<IOException>(() => _rpc.RestoreAsNewProjectAsync(backup!.Path, _root, "BackupNovel"));

        Assert.Equal(root, _workspace.Projects.ProjectRoot);
        AssertSnapshot(before, root);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("..\\outside")]
    [InlineData("..")]
    [InlineData("")]
    public async Task RestoreAsNewProject_RejectsInvalidNames(string name)
    {
        var backup = await _rpc.CreateMilestoneAsync("Version 1");
        await Assert.ThrowsAsync<ArgumentException>(() => _rpc.RestoreAsNewProjectAsync(backup!.Path, _root, name));
    }

    [Fact]
    public async Task Create_RepeatedWithinOneSecond_WritesDistinctArchives()
    {
        var first = await _rpc.CreateMilestoneAsync("Version 1");
        var second = await _rpc.CreateMilestoneAsync("Version 1");
        Assert.NotEqual(first!.Id, second!.Id);
        Assert.Equal(2, (await _rpc.ListAsync()).Length);
    }

    private static Dictionary<string, byte[]> Snapshot(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes);

    private static void AssertSnapshot(Dictionary<string, byte[]> expected, string root)
    {
        var actual = Snapshot(root);
        Assert.Equal(expected.Keys.OrderBy(p => p), actual.Keys.OrderBy(p => p));
        foreach (var (path, bytes) in expected) Assert.Equal(bytes, actual[path]);
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
