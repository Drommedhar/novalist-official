using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>In-app version control over the project folder.</summary>
public sealed class GitRpc
{
    private readonly Workspace _workspace;
    private readonly GitService _git;
    private readonly IProcessRunner _process;
    private bool _initialized;
    private string? _repoRoot;

    public GitRpc(Workspace workspace, IProcessRunner? processRunner = null)
    {
        _workspace = workspace;
        _process = processRunner ?? new ProcessRunner();
        // Share one runner across both paths (GitService and the direct
        // pathspec/commit calls) so an injected runner - e.g. the mobile
        // UnavailableProcessRunner - disables Git everywhere, not just partially.
        _git = new GitService(_process);
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

    /// <summary>
    /// Scene IDs whose files have uncommitted Git changes, so the explorer can
    /// mark changed scenes (mirrors the desktop binder's change markers). Matches
    /// a scene's project-relative path against the changed pathspecs by suffix,
    /// which is robust whether the repo root is the project or a parent folder.
    /// </summary>
    [JsonRpcMethod("git/changedScenes")]
    public async Task<string[]> ChangedScenesAsync()
    {
        await EnsureInitializedAsync();
        var info = await _git.GetStatusAsync();
        var projects = _workspace.Projects;
        var book = projects.ActiveBook;
        if (info == null || book == null || projects.ProjectRoot == null)
            return [];

        var changed = info.ChangedFiles.Select(f => f.RelativePath.Replace('\\', '/')).ToArray();
        if (changed.Length == 0) return [];

        var manifest = projects.ScenesManifest;
        var ids = new List<string>();
        foreach (var chapter in book.Chapters)
        {
            var scenes = manifest?.Chapters.GetValueOrDefault(chapter.Guid) ?? [];
            foreach (var scene in scenes.Where(s => s.ArchivedAt == null))
            {
                var rel = Path.GetRelativePath(projects.ProjectRoot,
                    projects.GetSceneFilePath(chapter, scene)).Replace('\\', '/');
                if (changed.Any(c => c.EndsWith(rel, StringComparison.OrdinalIgnoreCase)))
                    ids.Add(scene.Id);
            }
        }
        return ids.ToArray();
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

    /// <summary>The most recent commits, newest first.</summary>
    [JsonRpcMethod("git/log")]
    public async Task<GitCommitDto[]> LogAsync(int limit)
    {
        await EnsureInitializedAsync();
        return [.. (await _git.GetLogAsync(limit)).Select(c => new GitCommitDto(
            c.Sha, c.ShortSha, c.Author, c.Date.ToString("o"), c.Subject))];
    }

    /// <summary>The paths one commit touched.</summary>
    [JsonRpcMethod("git/commitFiles")]
    public async Task<string[]> CommitFilesAsync(string sha)
    {
        await EnsureInitializedAsync();
        return [.. await _git.GetCommitFilesAsync(sha)];
    }

    /// <summary>
    /// A unified diff for one path - the change a commit made, or the working
    /// tree against HEAD when no commit is named.
    /// </summary>
    [JsonRpcMethod("git/diff")]
    public async Task<string> DiffAsync(string? sha, string relativePath)
    {
        await EnsureInitializedAsync();
        return await _git.GetDiffAsync(sha, relativePath);
    }

    [JsonRpcMethod("git/branches")]
    public async Task<GitBranchDto[]> BranchesAsync()
    {
        await EnsureInitializedAsync();
        return [.. (await _git.GetBranchesAsync()).Select(b => new GitBranchDto(b.Name, b.IsCurrent))];
    }

    [JsonRpcMethod("git/createBranch")]
    public async Task<string?> CreateBranchAsync(string name)
    {
        await EnsureInitializedAsync();
        return await _git.CreateBranchAsync(name);
    }

    [JsonRpcMethod("git/switchBranch")]
    public async Task<string?> SwitchBranchAsync(string name)
    {
        await EnsureInitializedAsync();
        return await _git.SwitchBranchAsync(name);
    }

    /// <summary>
    /// Turns the project folder into a repository. Everything else here needs
    /// one, and a writer had no way to make one without a terminal.
    /// </summary>
    [JsonRpcMethod("git/init")]
    public async Task<string?> InitAsync()
    {
        await EnsureInitializedAsync();
        var root = _workspace.Projects.ProjectRoot!;
        var error = await _git.InitRepositoryAsync(root);
        // The cached repo root was resolved before the repository existed.
        if (error == null) _repoRoot = null;
        return error;
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

/// <summary>One commit in the log. The date is ISO-8601 for the renderer to format.</summary>
public sealed record GitCommitDto(
    string Sha, string ShortSha, string Author, string Date, string Subject);

public sealed record GitBranchDto(string Name, bool IsCurrent);
