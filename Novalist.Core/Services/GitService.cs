using Novalist.Core.Models;

namespace Novalist.Core.Services;

public class GitService : IGitService
{
    private readonly IProcessRunner _processRunner;
    private string? _projectRoot;
    private string? _repoRoot;
    private bool _isGitInstalled;
    private Dictionary<string, GitFileEntry> _statusCache = new(StringComparer.OrdinalIgnoreCase);

    public GitService(IProcessRunner? processRunner = null)
    {
        _processRunner = processRunner ?? new ProcessRunner();
    }

    public bool IsGitRepo => _repoRoot != null;
    public bool IsGitInstalled => _isGitInstalled;

    public async Task InitializeAsync(string projectRoot)
    {
        _projectRoot = projectRoot;
        _repoRoot = null;
        _statusCache.Clear();

        _isGitInstalled = await CheckGitInstalledAsync();
        if (!_isGitInstalled)
            return;

        var (exitCode, output, _) = await RunGitAsync(projectRoot, "rev-parse", "--show-toplevel");
        if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
        {
            _repoRoot = output.Trim().Replace('/', Path.DirectorySeparatorChar);
        }
    }

    public async Task<GitRepoInfo?> GetStatusAsync()
    {
        if (_repoRoot == null)
            return null;

        var branchTask = GetBranchNameAsync();
        var remoteTask = HasRemoteAsync();
        var aheadBehindTask = GetAheadBehindAsync();
        var statusTask = GetChangedFilesAsync();

        await Task.WhenAll(branchTask, remoteTask, aheadBehindTask, statusTask);

        var (ahead, behind) = aheadBehindTask.Result;

        return new GitRepoInfo(
            branchTask.Result,
            remoteTask.Result,
            ahead,
            behind,
            statusTask.Result
        );
    }

    public async Task<string?> CommitAsync(IEnumerable<string> relativePaths, string message)
    {
        if (_repoRoot == null)
            return "Not a Git repository";

        var paths = relativePaths.ToList();
        if (paths.Count == 0)
            return "No files to commit";

        // Stage files
        var args = new List<string> { "add", "--" };
        args.AddRange(paths);
        var (exitCode, _, error) = await RunGitAsync(_repoRoot, args.ToArray());
        if (exitCode != 0)
            return $"Failed to stage files: {error}";

        // Commit
        (exitCode, _, error) = await RunGitAsync(_repoRoot, "commit", "-m", message);
        if (exitCode != 0)
            return $"Commit failed: {error}";

        return null;
    }

    public async Task<string?> PushAsync()
    {
        if (_repoRoot == null)
            return "Not a Git repository";

        var (exitCode, _, error) = await RunGitAsync(_repoRoot, "push");
        return exitCode != 0 ? $"Push failed: {error}" : null;
    }

    public async Task<string?> PullAsync()
    {
        if (_repoRoot == null)
            return "Not a Git repository";

        var (exitCode, _, error) = await RunGitAsync(_repoRoot, "pull");
        return exitCode != 0 ? $"Pull failed: {error}" : null;
    }

    public GitFileStatus GetFileStatus(string projectRelativePath)
    {
        if (_repoRoot == null || _projectRoot == null)
            return GitFileStatus.Unmodified;

        // Convert project-relative path to repo-relative path
        var fullPath = Path.Combine(_projectRoot, projectRelativePath);
        var repoRelative = Path.GetRelativePath(_repoRoot, fullPath);

        // Normalize separators for lookup
        var key = repoRelative.Replace(Path.DirectorySeparatorChar, '/');
        return _statusCache.TryGetValue(key, out var entry) ? entry.DisplayStatus : GitFileStatus.Unmodified;
    }

    public async Task<string?> DiscardChangesAsync(IEnumerable<string> relativePaths)
    {
        if (_repoRoot == null)
            return "Not a Git repository";

        var paths = relativePaths.ToList();
        if (paths.Count == 0)
            return null;

        // Separate untracked files from tracked files
        var untrackedPaths = new List<string>();
        var trackedPaths = new List<string>();
        foreach (var path in paths)
        {
            if (_statusCache.TryGetValue(path.Replace(Path.DirectorySeparatorChar, '/'), out var entry)
                && entry.WorkTreeStatus == GitFileStatus.Untracked)
            {
                untrackedPaths.Add(path);
            }
            else
            {
                trackedPaths.Add(path);
            }
        }

        // Restore tracked files
        if (trackedPaths.Count > 0)
        {
            var args = new List<string> { "checkout", "--" };
            args.AddRange(trackedPaths);
            var (exitCode, _, error) = await RunGitAsync(_repoRoot, args.ToArray());
            if (exitCode != 0)
                return $"Discard failed: {error}";
        }

        // Remove untracked files
        if (untrackedPaths.Count > 0)
        {
            var args = new List<string> { "clean", "-f", "--" };
            args.AddRange(untrackedPaths);
            var (exitCode, _, error) = await RunGitAsync(_repoRoot, args.ToArray());
            if (exitCode != 0)
                return $"Clean failed: {error}";
        }

        return null;
    }

    public async Task<IReadOnlyList<GitCommit>> GetLogAsync(int limit)
    {
        if (_repoRoot == null) return [];

        // A record separator no commit subject will contain, so a subject with
        // a tab or a pipe in it cannot split the line.
        var (exitCode, output, _) = await RunGitAsync(
            _repoRoot, "log", $"-n{(limit <= 0 ? 50 : limit)}", "--date=iso-strict",
            "--pretty=format:%H\u001f%h\u001f%an\u001f%ad\u001f%s");
        if (exitCode != 0) return [];

        var commits = new List<GitCommit>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\u001f');
            if (parts.Length < 5) continue;
            DateTimeOffset.TryParse(parts[3], out var date);
            commits.Add(new GitCommit(parts[0], parts[1], parts[2], date, parts[4].TrimEnd('\r')));
        }
        return commits;
    }

    public async Task<IReadOnlyList<string>> GetCommitFilesAsync(string sha)
    {
        if (_repoRoot == null || string.IsNullOrWhiteSpace(sha)) return [];

        var (exitCode, output, _) = await RunGitAsync(
            _repoRoot, "show", "--name-only", "--pretty=format:", sha);
        if (exitCode != 0) return [];

        return [.. output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Length > 0)];
    }

    public async Task<string> GetDiffAsync(string? sha, string relativePath)
    {
        if (_repoRoot == null || string.IsNullOrWhiteSpace(relativePath)) return string.Empty;

        var (exitCode, output, _) = string.IsNullOrWhiteSpace(sha)
            ? await RunGitAsync(_repoRoot, "diff", "HEAD", "--", relativePath)
            : await RunGitAsync(_repoRoot, "show", sha, "--", relativePath);
        return exitCode == 0 ? output : string.Empty;
    }

    public async Task<IReadOnlyList<GitBranch>> GetBranchesAsync()
    {
        if (_repoRoot == null) return [];

        var (exitCode, output, _) = await RunGitAsync(
            _repoRoot, "branch", "--format=%(refname:short)\u001f%(HEAD)");
        if (exitCode != 0) return [];

        var branches = new List<GitBranch>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.TrimEnd('\r').Split('\u001f');
            if (parts.Length < 2 || parts[0].Length == 0) continue;
            branches.Add(new GitBranch(parts[0], parts[1].Trim() == "*"));
        }
        return branches;
    }

    public async Task<string?> CreateBranchAsync(string name)
    {
        if (_repoRoot == null) return "Not a Git repository";
        if (string.IsNullOrWhiteSpace(name)) return "A branch needs a name";

        var (exitCode, _, error) = await RunGitAsync(_repoRoot, "checkout", "-b", name.Trim());
        return exitCode == 0 ? null : error.Trim();
    }

    public async Task<string?> SwitchBranchAsync(string name)
    {
        if (_repoRoot == null) return "Not a Git repository";
        if (string.IsNullOrWhiteSpace(name)) return "A branch needs a name";

        var (exitCode, _, error) = await RunGitAsync(_repoRoot, "checkout", name.Trim());
        return exitCode == 0 ? null : error.Trim();
    }

    public async Task<string?> InitRepositoryAsync(string projectRoot)
    {
        if (!_isGitInstalled) return "Git is not installed";
        if (_repoRoot != null) return "This project is already in a repository";

        var (exitCode, _, error) = await RunGitAsync(projectRoot, "init");
        if (exitCode != 0) return error.Trim();

        await InitializeAsync(projectRoot);
        return null;
    }

    private async Task<string> GetBranchNameAsync()
    {
        var (exitCode, output, _) = await RunGitAsync(_repoRoot!, "branch", "--show-current");
        if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
            return output.Trim();

        // Detached HEAD — get short SHA
        (exitCode, output, _) = await RunGitAsync(_repoRoot!, "rev-parse", "--short", "HEAD");
        return exitCode == 0 ? $"({output.Trim()})" : "(unknown)";
    }

    private async Task<bool> HasRemoteAsync()
    {
        var (exitCode, output, _) = await RunGitAsync(_repoRoot!, "remote");
        return exitCode == 0 && !string.IsNullOrWhiteSpace(output);
    }

    private async Task<(int Ahead, int Behind)> GetAheadBehindAsync()
    {
        var (exitCode, output, _) = await RunGitAsync(_repoRoot!, "rev-list", "--count", "--left-right", "@{u}...HEAD");
        if (exitCode != 0)
            return (0, 0);

        var parts = output.Trim().Split('\t');
        if (parts.Length == 2
            && int.TryParse(parts[0], out var behind)
            && int.TryParse(parts[1], out var ahead))
        {
            return (ahead, behind);
        }

        return (0, 0);
    }

    private async Task<IReadOnlyList<GitFileEntry>> GetChangedFilesAsync()
    {
        var (exitCode, output, _) = await RunGitAsync(_repoRoot!, "status", "--porcelain=v1", "-uall");
        if (exitCode != 0)
            return Array.Empty<GitFileEntry>();

        var entries = new List<GitFileEntry>();
        _statusCache.Clear();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 4)
                continue;

            var indexChar = line[0];
            var workTreeChar = line[1];
            var path = line[3..].Trim();

            // Handle renames: "R  old -> new"
            if (path.Contains(" -> "))
                path = path[(path.IndexOf(" -> ", StringComparison.Ordinal) + 4)..];

            // Remove surrounding quotes if present
            if (path.StartsWith('"') && path.EndsWith('"'))
                path = path[1..^1];

            var indexStatus = ParseStatusChar(indexChar);
            var workTreeStatus = ParseStatusChar(workTreeChar);

            var entry = new GitFileEntry(path, indexStatus, workTreeStatus);
            entries.Add(entry);
            _statusCache[path] = entry;
        }

        return entries;
    }

    private static GitFileStatus ParseStatusChar(char c) => c switch
    {
        ' ' => GitFileStatus.Unmodified,
        'M' => GitFileStatus.Modified,
        'A' => GitFileStatus.Added,
        'D' => GitFileStatus.Deleted,
        'R' => GitFileStatus.Renamed,
        '?' => GitFileStatus.Untracked,
        '!' => GitFileStatus.Ignored,
        'U' => GitFileStatus.Conflicted,
        'C' => GitFileStatus.Added, // Copied
        _ => GitFileStatus.Unmodified
    };

    private async Task<bool> CheckGitInstalledAsync()
    {
        try
        {
            var (exitCode, _, _) = await RunGitAsync(null, "--version");
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private Task<(int ExitCode, string Output, string Error)> RunGitAsync(
        string? workingDirectory, params string[] args)
        => _processRunner.RunAsync("git", workingDirectory, args);
}
