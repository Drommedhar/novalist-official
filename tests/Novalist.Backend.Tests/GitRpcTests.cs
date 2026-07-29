using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class GitRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly GitRpc _rpc;

    public GitRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-git-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "GitNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new GitRpc(_workspace);
        RunGit("init");
        RunGit("config user.email test@example.org");
        RunGit("config user.name Tester");
    }

    private void RunGit(string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", args)
        {
            WorkingDirectory = _workspace.Projects.ProjectRoot!,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        process.WaitForExit();
    }

    public void Dispose() => TestHelpers.TempDir.ForceDelete(_root);

    [Fact]
    public async Task UnavailableProcessRunner_DegradesGitToUnavailable()
    {
        // Mobile wiring: with an UnavailableProcessRunner, `git --version` and the
        // repo probe both fail, so Git reports not installed and no status - even
        // though this fixture created a real repo on disk.
        var rpc = new GitRpc(_workspace, new UnavailableProcessRunner());

        Assert.False(await rpc.IsInstalledAsync());
        Assert.Null(await rpc.StatusAsync());
    }

    [Fact]
    public async Task ChangedScenes_MarksScenesWithUncommittedEdits()
    {
        // No project scene changes committed yet: the freshly created scene file
        // is uncommitted, so it shows as changed.
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        await _workspace.WriteSceneAsync(chapter.Guid, scene.Id, "<p>alpha</p>", "alpha");

        var changed = await _rpc.ChangedScenesAsync();
        Assert.Contains(scene.Id, changed);

        // After committing everything, nothing is changed.
        var all = (await _rpc.StatusAsync())!.ChangedFiles.Select(f => f.RelativePath).ToArray();
        await _rpc.CommitAsync(all, "commit scene");
        Assert.Empty(await _rpc.ChangedScenesAsync());

        // Re-editing the scene marks it changed again.
        await _workspace.WriteSceneAsync(chapter.Guid, scene.Id, "<p>alpha beta</p>", "alpha beta");
        Assert.Contains(scene.Id, await _rpc.ChangedScenesAsync());
    }

    [Fact]
    public async Task Status_Commit_Discard_FullFlow()
    {
        var status = await _rpc.StatusAsync();
        Assert.NotNull(status);
        Assert.False(status.HasRemote);
        Assert.NotEmpty(status.ChangedFiles);

        var files = status.ChangedFiles.Select(f => f.RelativePath).ToArray();
        var commitError = await _rpc.CommitAsync(files, "Initial commit");
        Assert.Null(commitError);

        var clean = await _rpc.StatusAsync();
        Assert.NotNull(clean);
        Assert.Empty(clean.ChangedFiles);

        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        var dirty = await _rpc.StatusAsync();
        Assert.NotNull(dirty);
        Assert.NotEmpty(dirty.ChangedFiles);

        var discardError = await _rpc.DiscardAsync(
            dirty.ChangedFiles.Select(f => f.RelativePath).ToArray());
        Assert.Null(discardError);
        _ = chapter;
    }

    [Fact]
    public async Task PushAndPullWithoutRemote_ReturnErrors()
    {
        Assert.NotNull(await _rpc.PushAsync());
        Assert.NotNull(await _rpc.PullAsync());
    }

    [Fact]
    public async Task IsInstalled_ReturnsTrue()
    {
        Assert.True(await _rpc.IsInstalledAsync());
    }

    [Fact]
    public async Task StageUnstage_TogglesIndexState()
    {
        // Establish HEAD so index state is meaningful.
        var initial = await _rpc.StatusAsync();
        await _rpc.CommitAsync(initial!.ChangedFiles.Select(f => f.RelativePath).ToArray(), "Initial");

        var chapter = await _workspace.Projects.CreateChapterAsync("Staged");
        var toStage = (await _rpc.StatusAsync())!.ChangedFiles.Select(f => f.RelativePath).ToArray();
        Assert.NotEmpty(toStage);

        // First stage call also resolves the repo root.
        Assert.Null(await _rpc.StageAsync(toStage));
        var staged = await _rpc.StatusAsync();
        Assert.Contains(staged!.ChangedFiles, f => f.IsStaged);

        // Second call exercises the cached repo-root path.
        Assert.Null(await _rpc.UnstageAsync(toStage));
        var unstaged = await _rpc.StatusAsync();
        Assert.DoesNotContain(unstaged!.ChangedFiles, f => f.IsStaged);
        _ = chapter;
    }

    [Fact]
    public async Task StageAll_UnstageAll_CommitStaged()
    {
        var initial = await _rpc.StatusAsync();
        await _rpc.CommitAsync(initial!.ChangedFiles.Select(f => f.RelativePath).ToArray(), "Initial");

        await _workspace.Projects.CreateChapterAsync("Bulk");
        Assert.NotEmpty((await _rpc.StatusAsync())!.ChangedFiles);

        Assert.Null(await _rpc.StageAllAsync());
        Assert.Contains((await _rpc.StatusAsync())!.ChangedFiles, f => f.IsStaged);

        Assert.Null(await _rpc.UnstageAllAsync());
        Assert.DoesNotContain((await _rpc.StatusAsync())!.ChangedFiles, f => f.IsStaged);

        // Commit with nothing staged surfaces git's error message.
        Assert.NotNull(await _rpc.CommitStagedAsync("empty"));

        Assert.Null(await _rpc.StageAllAsync());
        Assert.Null(await _rpc.CommitStagedAsync("Staged commit"));
        Assert.Empty((await _rpc.StatusAsync())!.ChangedFiles);
    }

    [Fact]
    public async Task Stage_EmptyPaths_IsNoop_AndBadPath_ReturnsError()
    {
        Assert.Null(await _rpc.StageAsync([]));
        Assert.NotNull(await _rpc.StageAsync(["definitely-not-a-real-file.xyz"]));
    }

    [Fact]
    public async Task StagingOnNonRepo_ReturnsNotARepoError()
    {
        var root = Path.Combine(Path.GetTempPath(), "nl-git-norepo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var workspace = new Workspace(Path.Combine(root, "settings"));
            await workspace.Projects.CreateProjectAsync(root, "NoRepo", "Book");
            await workspace.OpenProjectAsync(workspace.Projects.ProjectRoot!);
            var rpc = new GitRpc(workspace);

            Assert.Equal("Not a Git repository", await rpc.StageAsync(["a"]));
            Assert.Equal("Not a Git repository", await rpc.UnstageAsync(["a"]));
            Assert.Equal("Not a Git repository", await rpc.StageAllAsync());
            Assert.Equal("Not a Git repository", await rpc.UnstageAllAsync());
            Assert.Equal("Not a Git repository", await rpc.CommitStagedAsync("m"));
            // Outside a repo there is no status, so no scenes are marked changed.
            Assert.Empty(await rpc.ChangedScenesAsync());
        }
        finally
        {
            TestHelpers.TempDir.ForceDelete(root);
        }
    }

    [Fact]
    public async Task LogListsCommitsNewestFirst()
    {
        await File.WriteAllTextAsync(Path.Combine(_workspace.Projects.ProjectRoot!, "a.txt"), "one");
        RunGit("add -A");
        RunGit("commit -m first");
        await File.WriteAllTextAsync(Path.Combine(_workspace.Projects.ProjectRoot!, "a.txt"), "two");
        RunGit("add -A");
        RunGit("commit -m second");

        var log = await _rpc.LogAsync(10);

        Assert.True(log.Length >= 2);
        Assert.Equal("second", log[0].Subject);
        Assert.NotEmpty(log[0].ShortSha);
    }

    [Fact]
    public async Task CommitFilesAndDiffReadOneCommit()
    {
        await File.WriteAllTextAsync(Path.Combine(_workspace.Projects.ProjectRoot!, "a.txt"), "one");
        RunGit("add -A");
        RunGit("commit -m first");
        var sha = (await _rpc.LogAsync(1))[0].Sha;

        var files = await _rpc.CommitFilesAsync(sha);

        Assert.Contains("a.txt", files);
        Assert.Contains("one", await _rpc.DiffAsync(sha, "a.txt"));
    }

    [Fact]
    public async Task DiffWithNoShaShowsTheWorkingTree()
    {
        var path = Path.Combine(_workspace.Projects.ProjectRoot!, "a.txt");
        await File.WriteAllTextAsync(path, "one");
        RunGit("add -A");
        RunGit("commit -m first");
        await File.WriteAllTextAsync(path, "two");

        Assert.Contains("two", await _rpc.DiffAsync(null, "a.txt"));
    }

    [Fact]
    public async Task BranchesListTheCurrentOneAndSwitchingWorks()
    {
        await File.WriteAllTextAsync(Path.Combine(_workspace.Projects.ProjectRoot!, "a.txt"), "one");
        RunGit("add -A");
        RunGit("commit -m first");

        Assert.Null(await _rpc.CreateBranchAsync("revision"));
        var branches = await _rpc.BranchesAsync();
        Assert.Contains(branches, b => b.Name == "revision" && b.IsCurrent);

        var previous = branches.First(b => !b.IsCurrent).Name;
        Assert.Null(await _rpc.SwitchBranchAsync(previous));
        Assert.Contains(await _rpc.BranchesAsync(), b => b.Name == previous && b.IsCurrent);
    }

    [Fact]
    public async Task InitOnAFolderThatIsAlreadyARepositorySaysSo()
        => Assert.Equal("This project is already in a repository", await _rpc.InitAsync());
}
