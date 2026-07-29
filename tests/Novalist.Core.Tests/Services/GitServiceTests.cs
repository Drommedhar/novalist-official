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

    // -- History --

    [Fact]
    public async Task GetLogAsync_ParsesCommitsNewestFirst()
    {
        const string log =
            "abc123short1Mira Vance2026-01-02T10:00:00+00:00Fixed the bell\n"
            + "def456short2Halden2026-01-01T09:00:00+00:00First draft";
        var runner = new FakeProcessRunner(args =>
            args[0] == "log" ? (0, log, "") : DefaultResponder(args));
        var sut = await InitializedRepo(runner);

        var commits = await sut.GetLogAsync(10);

        Assert.Equal(2, commits.Count);
        Assert.Equal("abc123", commits[0].Sha);
        Assert.Equal("short1", commits[0].ShortSha);
        Assert.Equal("Mira Vance", commits[0].Author);
        Assert.Equal("Fixed the bell", commits[0].Subject);
        Assert.Equal(2026, commits[0].Date.Year);
    }

    [Fact]
    public async Task GetLogAsync_SkipsLinesItCannotRead()
    {
        var runner = new FakeProcessRunner(args =>
            args[0] == "log"
                ? (0, "not a record\nabcab2026-01-01T00:00:00Zsubject", "")
                : DefaultResponder(args));
        var sut = await InitializedRepo(runner);

        Assert.Single(await sut.GetLogAsync(10));
    }

    [Fact]
    public async Task GetLogAsync_GitFailing_IsAnEmptyLog()
    {
        var runner = new FakeProcessRunner(args =>
            args[0] == "log" ? (128, "", "fatal") : DefaultResponder(args));
        var sut = await InitializedRepo(runner);

        Assert.Empty(await sut.GetLogAsync(10));
    }

    [Fact]
    public async Task GetLogAsync_NoRepo_IsEmpty()
    {
        var runner = new FakeProcessRunner(args =>
            args[0] == "rev-parse" ? (128, "", "") : DefaultResponder(args));
        var sut = await InitializedRepo(runner);

        Assert.Empty(await sut.GetLogAsync(0));
    }

    [Fact]
    public async Task GetCommitFilesAsync_ListsThePathsTouched()
    {
        var runner = new FakeProcessRunner(args =>
            args[0] == "show" ? (0, "Books/one.html\nBooks/two.html\n", "") : DefaultResponder(args));
        var sut = await InitializedRepo(runner);

        Assert.Equal(["Books/one.html", "Books/two.html"], await sut.GetCommitFilesAsync("abc"));
    }

    [Fact]
    public async Task GetCommitFilesAsync_NoShaIsEmpty()
    {
        var sut = await InitializedRepo(new FakeProcessRunner(DefaultResponder));
        Assert.Empty(await sut.GetCommitFilesAsync("  "));
    }

    [Fact]
    public async Task GetCommitFilesAsync_GitFailingIsEmpty()
    {
        var runner = new FakeProcessRunner(args =>
            args[0] == "show" ? (128, "", "fatal") : DefaultResponder(args));
        var sut = await InitializedRepo(runner);

        Assert.Empty(await sut.GetCommitFilesAsync("abc"));
    }

    [Fact]
    public async Task GetDiffAsync_WithNoShaComparesTheWorkingTree()
    {
        string[]? seen = null;
        var runner = new FakeProcessRunner(args =>
        {
            if (args[0] == "diff") { seen = args; return (0, "@@ -1 +1 @@", ""); }
            return DefaultResponder(args);
        });
        var sut = await InitializedRepo(runner);

        Assert.Equal("@@ -1 +1 @@", await sut.GetDiffAsync(null, "a.html"));
        Assert.NotNull(seen);
        Assert.Equal(["diff", "HEAD", "--", "a.html"], seen);
    }

    [Fact]
    public async Task GetDiffAsync_WithAShaShowsThatCommit()
    {
        string[]? seen = null;
        var runner = new FakeProcessRunner(args =>
        {
            if (args[0] == "show") { seen = args; return (0, "diff body", ""); }
            return DefaultResponder(args);
        });
        var sut = await InitializedRepo(runner);

        Assert.Equal("diff body", await sut.GetDiffAsync("abc", "a.html"));
        Assert.NotNull(seen);
        Assert.Equal(["show", "abc", "--", "a.html"], seen);
    }

    [Fact]
    public async Task GetDiffAsync_NoPathIsEmpty()
    {
        var sut = await InitializedRepo(new FakeProcessRunner(DefaultResponder));
        Assert.Equal(string.Empty, await sut.GetDiffAsync(null, " "));
    }

    [Fact]
    public async Task GetDiffAsync_GitFailingIsEmpty()
    {
        var runner = new FakeProcessRunner(args =>
            args[0] == "diff" ? (128, "", "fatal") : DefaultResponder(args));
        var sut = await InitializedRepo(runner);

        Assert.Equal(string.Empty, await sut.GetDiffAsync(null, "a.html"));
    }

    // -- Branches --

    [Fact]
    public async Task GetBranchesAsync_MarksTheCheckedOutOne()
    {
        var runner = new FakeProcessRunner(args =>
            args[0] == "branch" ? (0, "main*\nrevision \n", "") : DefaultResponder(args));
        var sut = await InitializedRepo(runner);

        var branches = await sut.GetBranchesAsync();

        Assert.Equal(2, branches.Count);
        Assert.True(branches[0].IsCurrent);
        Assert.False(branches[1].IsCurrent);
        Assert.Equal("revision", branches[1].Name);
    }

    [Fact]
    public async Task GetBranchesAsync_GitFailingIsEmpty()
    {
        var runner = new FakeProcessRunner(args =>
            args[0] == "branch" ? (128, "", "fatal") : DefaultResponder(args));
        var sut = await InitializedRepo(runner);

        Assert.Empty(await sut.GetBranchesAsync());
    }

    [Fact]
    public async Task GetBranchesAsync_NoRepoIsEmpty()
    {
        var runner = new FakeProcessRunner(args =>
            args[0] == "rev-parse" ? (128, "", "") : DefaultResponder(args));
        var sut = await InitializedRepo(runner);

        Assert.Empty(await sut.GetBranchesAsync());
    }

    [Fact]
    public async Task CreateBranchAsync_CheckoutMinusB()
    {
        string[]? seen = null;
        var runner = new FakeProcessRunner(args =>
        {
            if (args[0] == "checkout") { seen = args; return (0, "", ""); }
            return DefaultResponder(args);
        });
        var sut = await InitializedRepo(runner);

        Assert.Null(await sut.CreateBranchAsync(" revision "));
        Assert.NotNull(seen);
        Assert.Equal(["checkout", "-b", "revision"], seen);
    }

    [Fact]
    public async Task CreateBranchAsync_ReportsWhatGitSaid()
    {
        var runner = new FakeProcessRunner(args =>
            args[0] == "checkout" ? (128, "", "already exists\n") : DefaultResponder(args));
        var sut = await InitializedRepo(runner);

        Assert.Equal("already exists", await sut.CreateBranchAsync("revision"));
    }

    [Fact]
    public async Task CreateBranchAsync_NeedsAName()
    {
        var sut = await InitializedRepo(new FakeProcessRunner(DefaultResponder));
        Assert.Equal("A branch needs a name", await sut.CreateBranchAsync("  "));
    }

    [Fact]
    public async Task CreateBranchAsync_NeedsARepo()
    {
        var noRepo = new FakeProcessRunner(args =>
            args[0] == "rev-parse" ? (128, "", "") : DefaultResponder(args));
        var sut = await InitializedRepo(noRepo);

        Assert.Equal("Not a Git repository", await sut.CreateBranchAsync("x"));
    }

    [Fact]
    public async Task SwitchBranchAsync_ChecksOutTheName()
    {
        string[]? seen = null;
        var runner = new FakeProcessRunner(args =>
        {
            if (args[0] == "checkout") { seen = args; return (0, "", ""); }
            return DefaultResponder(args);
        });
        var sut = await InitializedRepo(runner);

        Assert.Null(await sut.SwitchBranchAsync("main"));
        Assert.NotNull(seen);
        Assert.Equal(["checkout", "main"], seen);
    }

    [Fact]
    public async Task SwitchBranchAsync_NeedsAName()
    {
        var sut = await InitializedRepo(new FakeProcessRunner(DefaultResponder));
        Assert.Equal("A branch needs a name", await sut.SwitchBranchAsync(""));
    }

    [Fact]
    public async Task SwitchBranchAsync_NeedsARepo()
    {
        var noRepo = new FakeProcessRunner(args =>
            args[0] == "rev-parse" ? (128, "", "") : DefaultResponder(args));
        var sut = await InitializedRepo(noRepo);

        Assert.Equal("Not a Git repository", await sut.SwitchBranchAsync("x"));
    }

    [Fact]
    public async Task SwitchBranchAsync_ReportsWhatGitSaid()
    {
        var runner = new FakeProcessRunner(args =>
            args[0] == "checkout" ? (1, "", "would be overwritten\n") : DefaultResponder(args));
        var sut = await InitializedRepo(runner);

        Assert.Equal("would be overwritten", await sut.SwitchBranchAsync("main"));
    }

    // -- Making a repository --

    [Fact]
    public async Task InitRepositoryAsync_CreatesAndThenFindsTheRepo()
    {
        var created = false;
        var runner = new FakeProcessRunner(args =>
        {
            if (args[0] == "init") { created = true; return (0, "", ""); }
            if (args[0] == "rev-parse" && args.Length > 1 && args[1] == "--show-toplevel")
                return created ? (0, "/repo\n", "") : (128, "", "");
            return DefaultResponder(args);
        });
        var sut = new GitService(runner);
        await sut.InitializeAsync("/repo");
        Assert.False(sut.IsGitRepo);

        Assert.Null(await sut.InitRepositoryAsync("/repo"));
        Assert.True(sut.IsGitRepo);
    }

    [Fact]
    public async Task InitRepositoryAsync_AlreadyARepoSaysSo()
    {
        var sut = await InitializedRepo(new FakeProcessRunner(DefaultResponder));
        Assert.Equal("This project is already in a repository", await sut.InitRepositoryAsync("/repo"));
    }

    [Fact]
    public async Task InitRepositoryAsync_NoGitSaysSo()
    {
        var runner = new FakeProcessRunner(args => args[0] == "--version" ? (1, "", "") : (0, "", ""));
        var sut = await InitializedRepo(runner);

        Assert.Equal("Git is not installed", await sut.InitRepositoryAsync("/repo"));
    }

    [Fact]
    public async Task InitRepositoryAsync_ReportsWhatGitSaid()
    {
        var runner = new FakeProcessRunner(args =>
        {
            if (args[0] == "init") return (1, "", "permission denied\n");
            if (args[0] == "rev-parse" && args.Length > 1 && args[1] == "--show-toplevel")
                return (128, "", "");
            return DefaultResponder(args);
        });
        var sut = await InitializedRepo(runner);

        Assert.Equal("permission denied", await sut.InitRepositoryAsync("/repo"));
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
