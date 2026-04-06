using System.Diagnostics;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public class GitService : IGitService
{
    private string? _projectRoot;
    private string? _repoRoot;
    private bool _isGitInstalled;
    private Dictionary<string, GitFileEntry> _statusCache = new(StringComparer.OrdinalIgnoreCase);

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

    private static async Task<bool> CheckGitInstalledAsync()
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

    private static async Task<(int ExitCode, string Output, string Error)> RunGitAsync(
        string? workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        if (!string.IsNullOrEmpty(workingDirectory))
            psi.WorkingDirectory = workingDirectory;

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process == null)
            return (-1, string.Empty, "Failed to start git process");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;

        return (process.ExitCode, output, error);
    }
}
