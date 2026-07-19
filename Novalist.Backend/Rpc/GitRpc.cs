using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>In-app version control over the project folder.</summary>
public sealed class GitRpc
{
    private readonly Workspace _workspace;
    private readonly GitService _git = new();
    private readonly IProcessRunner _process;
    private bool _initialized;
    private string? _repoRoot;

    public GitRpc(Workspace workspace, IProcessRunner? processRunner = null)
    {
        _workspace = workspace;
        _process = processRunner ?? new ProcessRunner();
    }

    private async Task EnsureInitializedAsync()
    {
        var root = _workspace.Projects.ProjectRoot
            ?? throw new InvalidOperationException("No project open.");
        if (!_initialized)
        {
            await _git.InitializeAsync(root);
            _initialized = true;
        }
    }

    /// <summary>
    /// Resolves the enclosing repository root (cached). Returns null when the
    /// project is not inside a Git repository.
    /// </summary>
    private async Task<string?> EnsureRepoRootAsync()
    {
        await EnsureInitializedAsync();
        if (_repoRoot != null)
            return _repoRoot;

        var root = _workspace.Projects.ProjectRoot!;
        var (exitCode, output, _) = await _process.RunAsync("git", root, "rev-parse", "--show-toplevel");
        if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
            _repoRoot = output.Trim().Replace('/', Path.DirectorySeparatorChar);
        return _repoRoot;
    }

    [JsonRpcMethod("git/status")]
    public async Task<GitStatusDto?> StatusAsync()
    {
        await EnsureInitializedAsync();
        var info = await _git.GetStatusAsync();
        if (info == null) return null;
        return new GitStatusDto(
            info.BranchName,
            info.HasRemote,
            info.AheadBy,
            info.BehindBy,
            info.ChangedFiles
                .Select(f => new GitFileDto(f.RelativePath, f.DisplayStatus.ToString(), f.IsStaged))
                .ToArray());
    }

    /// <summary>Whether Git is available on the system PATH.</summary>
    [JsonRpcMethod("git/installed")]
    public async Task<bool> IsInstalledAsync()
    {
        await EnsureInitializedAsync();
        return _git.IsGitInstalled;
    }

    [JsonRpcMethod("git/commit")]
    public async Task<string?> CommitAsync(string[] relativePaths, string message)
    {
        await EnsureInitializedAsync();
        return await _git.CommitAsync(relativePaths, message);
    }

    /// <summary>Commit whatever is currently staged in the index.</summary>
    [JsonRpcMethod("git/commitStaged")]
    public async Task<string?> CommitStagedAsync(string message)
    {
        var repoRoot = await EnsureRepoRootAsync();
        if (repoRoot == null) return "Not a Git repository";
        var (exitCode, _, error) = await _process.RunAsync("git", repoRoot, "commit", "-m", message);
        return exitCode != 0 ? error.Trim() : null;
    }

    /// <summary>Stage the given files (git add).</summary>
    [JsonRpcMethod("git/stage")]
    public Task<string?> StageAsync(string[] relativePaths) =>
        RunPathspecAsync(relativePaths, "add");

    /// <summary>Unstage the given files (git reset).</summary>
    [JsonRpcMethod("git/unstage")]
    public Task<string?> UnstageAsync(string[] relativePaths) =>
        RunPathspecAsync(relativePaths, "reset", "-q");

    /// <summary>Stage every change in the working tree (git add -A).</summary>
    [JsonRpcMethod("git/stageAll")]
    public async Task<string?> StageAllAsync()
    {
        var repoRoot = await EnsureRepoRootAsync();
        if (repoRoot == null) return "Not a Git repository";
        var (exitCode, _, error) = await _process.RunAsync("git", repoRoot, "add", "-A");
        return exitCode != 0 ? error.Trim() : null;
    }

    /// <summary>Unstage the whole index (git reset).</summary>
    [JsonRpcMethod("git/unstageAll")]
    public async Task<string?> UnstageAllAsync()
    {
        var repoRoot = await EnsureRepoRootAsync();
        if (repoRoot == null) return "Not a Git repository";
        var (exitCode, _, error) = await _process.RunAsync("git", repoRoot, "reset", "-q");
        return exitCode != 0 ? error.Trim() : null;
    }

    [JsonRpcMethod("git/push")]
    public async Task<string?> PushAsync()
    {
        await EnsureInitializedAsync();
        return await _git.PushAsync();
    }

    [JsonRpcMethod("git/pull")]
    public async Task<string?> PullAsync()
    {
        await EnsureInitializedAsync();
        return await _git.PullAsync();
    }

    [JsonRpcMethod("git/discard")]
    public async Task<string?> DiscardAsync(string[] relativePaths)
    {
        await EnsureInitializedAsync();
        return await _git.DiscardChangesAsync(relativePaths);
    }

    private async Task<string?> RunPathspecAsync(string[] relativePaths, params string[] verb)
    {
        var repoRoot = await EnsureRepoRootAsync();
        if (repoRoot == null) return "Not a Git repository";
        if (relativePaths.Length == 0) return null;
        var args = new List<string>(verb) { "--" };
        args.AddRange(relativePaths);
        var (exitCode, _, error) = await _process.RunAsync("git", repoRoot, args.ToArray());
        return exitCode != 0 ? error.Trim() : null;
    }
}

public sealed record GitStatusDto(
    string BranchName,
    bool HasRemote,
    int AheadBy,
    int BehindBy,
    IReadOnlyList<GitFileDto> ChangedFiles);

public sealed record GitFileDto(string RelativePath, string Status, bool IsStaged);
