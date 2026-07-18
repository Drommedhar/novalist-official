using Novalist.Backend;
using Novalist.Backend.Rpc;
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

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
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
}
