using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class GitServiceTests
{
    // Standard responder: git installed, repo at /repo.
    private static (int, string, string) DefaultResponder(string[] args) => args[0] switch
    {
        "--version" => (0, "git version 2.40", ""),
        "rev-parse" when args.Length > 1 && args[1] == "--show-toplevel" => (0, "/repo\n", ""),
        _ => (0, "", "")
    };

    private static async Task<GitService> InitializedRepo(FakeProcessRunner runner)
    {
        var sut = new GitService(runner);
        await sut.InitializeAsync("/repo");
        return sut;
    }

    [Fact]
    public void DefaultConstructor_UsesRealRunner()
    {
        var sut = new GitService();
        Assert.False(sut.IsGitRepo);
    }

    [Fact]
    public async Task InitializeAsync_GitNotInstalled_StopsEarly()
    {
        var runner = new FakeProcessRunner(args => args[0] == "--version" ? (1, "", "") : (0, "", ""));
        var sut = await InitializedRepo(runner);
        Assert.False(sut.IsGitInstalled);
        Assert.False(sut.IsGitRepo);
    }

    [Fact]
    public async Task InitializeAsync_NotARepo_LeavesRepoRootNull()
    {
        var runner = new FakeProcessRunner(args => args[0] switch
        {
            "--version" => (0, "v", ""),
            "rev-parse" => (128, "", "fatal: not a git repository"),
            _ => (0, "", "")
        });
        var sut = await InitializedRepo(runner);
        Assert.True(sut.IsGitInstalled);
        Assert.False(sut.IsGitRepo);
    }

    [Fact]
    public async Task InitializeAsync_Repo_SetsRepoRoot()
    {
        var sut = await InitializedRepo(new FakeProcessRunner(DefaultResponder));
        Assert.True(sut.IsGitRepo);
    }

    [Fact]
    public async Task GetStatusAsync_NotRepo_ReturnsNull()
    {
        var sut = new GitService(new FakeProcessRunner(_ => (1, "", "")));
        await sut.InitializeAsync("/x");
        Assert.Null(await sut.GetStatusAsync());
    }

    [Fact]
    public async Task GetStatusAsync_AggregatesBranchRemoteAheadBehindFiles()
    {
        var runner = new FakeProcessRunner(args => args[0] switch
        {
            "--version" => (0, "v", ""),
            "rev-parse" when args[1] == "--show-toplevel" => (0, "/repo", ""),
            "branch" => (0, "main\n", ""),
            "remote" => (0, "origin\n", ""),
            "rev-list" => (0, "3\t2\n", ""),       // behind=3 ahead=2
            "status" => (0, "M  a.txt\n?? b.txt\n", ""),
            _ => (0, "", "")
        });
        var sut = await InitializedRepo(runner);

        var info = await sut.GetStatusAsync();

        Assert.NotNull(info);
        Assert.Equal("main", info!.BranchName);
        Assert.True(info.HasRemote);
        Assert.Equal(2, info.AheadBy);
        Assert.Equal(3, info.BehindBy);
        Assert.Equal(2, info.ChangedFiles.Count);
    }

    [Fact]
    public async Task GetStatusAsync_DetachedHead_UsesShortSha()
    {
        var runner = new FakeProcessRunner(args => args[0] switch
        {
            "--version" => (0, "v", ""),
            "rev-parse" when args[1] == "--show-toplevel" => (0, "/repo", ""),
            "branch" => (0, "  ", ""),                 // empty -> detached
            "rev-parse" => (0, "abc1234\n", ""),        // short sha
            "remote" => (1, "", ""),
            "rev-list" => (1, "", ""),
            "status" => (0, "", ""),
            _ => (0, "", "")
        });
        var sut = await InitializedRepo(runner);

        var info = await sut.GetStatusAsync();
        Assert.Equal("(abc1234)", info!.BranchName);
        Assert.False(info.HasRemote);
        Assert.Equal(0, info.AheadBy);
        Assert.Equal(0, info.BehindBy);
    }

    [Fact]
    public async Task GetStatusAsync_BranchAndShaBothFail_Unknown()
    {
        var runner = new FakeProcessRunner(args => args[0] switch
        {
            "--version" => (0, "v", ""),
            "rev-parse" when args.Length > 1 && args[1] == "--show-toplevel" => (0, "/repo", ""),
            "branch" => (1, "", ""),
            "rev-parse" => (1, "", ""),
            "rev-list" => (0, "garbage-no-tab", ""),    // malformed -> (0,0)
            _ => (0, "", "")
        });
        var sut = await InitializedRepo(runner);
        var info = await sut.GetStatusAsync();
        Assert.Equal("(unknown)", info!.BranchName);
        Assert.Equal(0, info.AheadBy);
    }

    [Fact]
    public async Task GetStatusAsync_StatusCommandFails_NoFiles()
    {
        var runner = new FakeProcessRunner(args => args[0] switch
        {
            "--version" => (0, "v", ""),
            "rev-parse" when args[1] == "--show-toplevel" => (0, "/repo", ""),
            "branch" => (0, "main", ""),
            "remote" => (0, "", ""),
            "rev-list" => (0, "0\t0", ""),
            "status" => (1, "", "error"),
            _ => (0, "", "")
        });
        var sut = await InitializedRepo(runner);
        var info = await sut.GetStatusAsync();
        Assert.Empty(info!.ChangedFiles);
    }

    [Fact]
    public async Task GetStatusAsync_ParsesRenamesQuotesAndSkipsShortLines()
    {
        var runner = new FakeProcessRunner(args => args[0] switch
        {
            "--version" => (0, "v", ""),
            "rev-parse" when args[1] == "--show-toplevel" => (0, "/repo", ""),
            "branch" => (0, "main", ""),
            "remote" => (0, "", ""),
            "rev-list" => (0, "0\t0", ""),
            "status" => (0, "R  old.txt -> new.txt\nA  \"quoted file.txt\"\nXY\n D del.txt\n", ""),
            _ => (0, "", "")
        });
        var sut = await InitializedRepo(runner);
        var info = await sut.GetStatusAsync();

        Assert.Contains(info!.ChangedFiles, f => f.RelativePath == "new.txt");
        Assert.Contains(info.ChangedFiles, f => f.RelativePath == "quoted file.txt");
        Assert.Contains(info.ChangedFiles, f => f.RelativePath == "del.txt");
        Assert.DoesNotContain(info.ChangedFiles, f => f.RelativePath == "XY");
    }

    [Fact]
    public async Task CommitAsync_NotRepo_ReturnsError()
    {
        var sut = new GitService(new FakeProcessRunner(_ => (1, "", "")));
        await sut.InitializeAsync("/x");
        Assert.Equal("Not a Git repository", await sut.CommitAsync(new[] { "a" }, "m"));
    }

    [Fact]
    public async Task CommitAsync_NoFiles_ReturnsError()
    {
        var sut = await InitializedRepo(new FakeProcessRunner(DefaultResponder));
        Assert.Equal("No files to commit", await sut.CommitAsync(Array.Empty<string>(), "m"));
    }

    [Fact]
    public async Task CommitAsync_StageFails_ReturnsError()
    {
        var runner = new FakeProcessRunner(args => args[0] switch
        {
            "--version" => (0, "v", ""),
            "rev-parse" => (0, "/repo", ""),
            "add" => (1, "", "permission denied"),
            _ => (0, "", "")
        });
        var sut = await InitializedRepo(runner);
        Assert.Contains("Failed to stage", await sut.CommitAsync(new[] { "a.txt" }, "m"));
    }

    [Fact]
    public async Task CommitAsync_CommitFails_ReturnsError()
    {
        var runner = new FakeProcessRunner(args => args[0] switch
        {
            "--version" => (0, "v", ""),
            "rev-parse" => (0, "/repo", ""),
            "add" => (0, "", ""),
            "commit" => (1, "", "nothing to commit"),
            _ => (0, "", "")
        });
        var sut = await InitializedRepo(runner);
        Assert.Contains("Commit failed", await sut.CommitAsync(new[] { "a.txt" }, "m"));
    }

    [Fact]
    public async Task CommitAsync_Success_ReturnsNull()
    {
        var runner = new FakeProcessRunner(args => args[0] switch
        {
            "--version" => (0, "v", ""),
            "rev-parse" => (0, "/repo", ""),
            "add" => (0, "", ""),
            "commit" => (0, "", ""),
            _ => (0, "", "")
        });
        var sut = await InitializedRepo(runner);
        Assert.Null(await sut.CommitAsync(new[] { "a.txt" }, "m"));
    }

    [Fact]
    public async Task PushAsync_VariesByExit()
    {
        var ok = await InitializedRepo(new FakeProcessRunner(DefaultResponder));
        Assert.Null(await ok.PushAsync());

        var fail = await InitializedRepo(new FakeProcessRunner(args => args[0] switch
        {
            "--version" => (0, "v", ""),
            "rev-parse" => (0, "/repo", ""),
            "push" => (1, "", "rejected"),
            _ => (0, "", "")
        }));
        Assert.Contains("Push failed", await fail.PushAsync());
    }

    [Fact]
    public async Task PushAsync_NotRepo_ReturnsError()
    {
        var sut = new GitService(new FakeProcessRunner(_ => (1, "", "")));
        await sut.InitializeAsync("/x");
        Assert.Equal("Not a Git repository", await sut.PushAsync());
    }

    [Fact]
    public async Task PullAsync_VariesByExit()
    {
        var ok = await InitializedRepo(new FakeProcessRunner(DefaultResponder));
        Assert.Null(await ok.PullAsync());

        var fail = await InitializedRepo(new FakeProcessRunner(args => args[0] switch
        {
            "--version" => (0, "v", ""),
            "rev-parse" => (0, "/repo", ""),
            "pull" => (1, "", "conflict"),
            _ => (0, "", "")
        }));
        Assert.Contains("Pull failed", await fail.PullAsync());
    }

    [Fact]
    public async Task PullAsync_NotRepo_ReturnsError()
    {
        var sut = new GitService(new FakeProcessRunner(_ => (1, "", "")));
        await sut.InitializeAsync("/x");
        Assert.Equal("Not a Git repository", await sut.PullAsync());
    }

    [Fact]
    public void GetFileStatus_NotRepo_ReturnsUnmodified()
    {
        var sut = new GitService(new FakeProcessRunner(DefaultResponder));
        Assert.Equal(GitFileStatus.Unmodified, sut.GetFileStatus("a.txt"));
    }

    [Fact]
    public async Task GetFileStatus_CacheHitAndMiss()
    {
        var runner = new FakeProcessRunner(args => args[0] switch
        {
            "--version" => (0, "v", ""),
            "rev-parse" when args[1] == "--show-toplevel" => (0, "/repo", ""),
            "branch" => (0, "main", ""),
            "remote" => (0, "", ""),
            "rev-list" => (0, "0\t0", ""),
            "status" => (0, "M  a.txt\n", ""),
            _ => (0, "", "")
        });
        var sut = new GitService(runner);
        await sut.InitializeAsync("/repo");
        await sut.GetStatusAsync(); // populates cache

        Assert.Equal(GitFileStatus.Modified, sut.GetFileStatus("a.txt"));
        Assert.Equal(GitFileStatus.Unmodified, sut.GetFileStatus("missing.txt"));
    }

    [Fact]
    public async Task DiscardChangesAsync_NotRepo_ReturnsError()
    {
        var sut = new GitService(new FakeProcessRunner(_ => (1, "", "")));
        await sut.InitializeAsync("/x");
        Assert.Equal("Not a Git repository", await sut.DiscardChangesAsync(new[] { "a" }));
    }

    [Fact]
    public async Task DiscardChangesAsync_EmptyPaths_ReturnsNull()
    {
        var sut = await InitializedRepo(new FakeProcessRunner(DefaultResponder));
        Assert.Null(await sut.DiscardChangesAsync(Array.Empty<string>()));
    }

    [Fact]
    public async Task DiscardChangesAsync_RestoresTracked()
    {
        var sut = await InitializedRepo(new FakeProcessRunner(DefaultResponder));
        Assert.Null(await sut.DiscardChangesAsync(new[] { "a.txt" }));
    }

    [Fact]
    public async Task DiscardChangesAsync_TrackedCheckoutFails()
    {
        var runner = new FakeProcessRunner(args => args[0] switch
        {
            "--version" => (0, "v", ""),
            "rev-parse" => (0, "/repo", ""),
            "checkout" => (1, "", "boom"),
            _ => (0, "", "")
        });
        var sut = await InitializedRepo(runner);
        Assert.Contains("Discard failed", await sut.DiscardChangesAsync(new[] { "a.txt" }));
    }

    [Fact]
    public async Task DiscardChangesAsync_CleansUntracked()
    {
        var runner = new FakeProcessRunner(args => args[0] switch
        {
            "--version" => (0, "v", ""),
            "rev-parse" when args[1] == "--show-toplevel" => (0, "/repo", ""),
            "branch" => (0, "main", ""),
            "remote" => (0, "", ""),
            "rev-list" => (0, "0\t0", ""),
            "status" => (0, "?? new.txt\n", ""),
            "clean" => (0, "", ""),
            _ => (0, "", "")
        });
        var sut = new GitService(runner);
        await sut.InitializeAsync("/repo");
        await sut.GetStatusAsync(); // cache marks new.txt untracked

        Assert.Null(await sut.DiscardChangesAsync(new[] { "new.txt" }));
        Assert.Contains(runner.Calls, c => c.Length > 0 && c[0] == "clean");
    }

    [Fact]
    public async Task DiscardChangesAsync_CleanUntrackedFails()
    {
        var runner = new FakeProcessRunner(args => args[0] switch
        {
            "--version" => (0, "v", ""),
            "rev-parse" when args[1] == "--show-toplevel" => (0, "/repo", ""),
            "branch" => (0, "main", ""),
            "remote" => (0, "", ""),
            "rev-list" => (0, "0\t0", ""),
            "status" => (0, "?? new.txt\n", ""),
            "clean" => (1, "", "boom"),
            _ => (0, "", "")
        });
        var sut = new GitService(runner);
        await sut.InitializeAsync("/repo");
        await sut.GetStatusAsync();
        Assert.Contains("Clean failed", await sut.DiscardChangesAsync(new[] { "new.txt" }));
    }

    [Fact]
    public async Task CheckGitInstalled_RunnerThrows_TreatedAsNotInstalled()
    {
        var runner = new FakeProcessRunner(DefaultResponder) { Throw = true };
        var sut = new GitService(runner);
        await sut.InitializeAsync("/repo");
        Assert.False(sut.IsGitInstalled);
    }

    [Theory]
    [InlineData("M  a.txt", GitFileStatus.Modified)]
    [InlineData("A  a.txt", GitFileStatus.Added)]
    [InlineData("D  a.txt", GitFileStatus.Deleted)]
    [InlineData("C  a.txt", GitFileStatus.Added)]
    [InlineData("R  a.txt", GitFileStatus.Renamed)]
    [InlineData("!! a.txt", GitFileStatus.Ignored)]
    [InlineData("UU a.txt", GitFileStatus.Conflicted)]
    [InlineData("ZZ a.txt", GitFileStatus.Unmodified)]
    public async Task GetChangedFiles_ParsesIndexStatusChars(string statusLine, GitFileStatus expectedIndex)
    {
        var runner = new FakeProcessRunner(args => args[0] switch
        {
            "--version" => (0, "v", ""),
            "rev-parse" when args[1] == "--show-toplevel" => (0, "/repo", ""),
            "branch" => (0, "main", ""),
            "remote" => (0, "", ""),
            "rev-list" => (0, "0\t0", ""),
            "status" => (0, statusLine + "\n", ""),
            _ => (0, "", "")
        });
        var sut = new GitService(runner);
        await sut.InitializeAsync("/repo");
        var info = await sut.GetStatusAsync();
        Assert.Equal(expectedIndex, info!.ChangedFiles[0].IndexStatus);
    }
}

public class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_RealProcess_CapturesOutput()
    {
        // 'dotnet --version' is available on the build/test machine.
        var runner = new ProcessRunner();
        var (exit, output, _) = await runner.RunAsync("dotnet", null, "--version");
        Assert.Equal(0, exit);
        Assert.False(string.IsNullOrWhiteSpace(output));
    }

    [Fact]
    public async Task RunAsync_WithWorkingDirectory()
    {
        var runner = new ProcessRunner();
        var (exit, _, _) = await runner.RunAsync("dotnet", Path.GetTempPath(), "--version");
        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task RunAsync_Cancellation_KillsProcessAndThrows()
    {
        var runner = new ProcessRunner();
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // pre-cancelled -> WaitForExitAsync throws, process is killed

        var (file, args) = OperatingSystem.IsWindows()
            ? ("cmd", new[] { "/c", "ping", "-n", "30", "127.0.0.1" })
            : ("sleep", new[] { "30" });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(file, null, cts.Token, args));
    }
}
