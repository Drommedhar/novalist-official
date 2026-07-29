using Novalist.Core.Models;

namespace Novalist.Core.Services;

public interface IGitService
{
    /// <summary>
    /// Whether the project directory is inside a Git repository.
    /// </summary>
    bool IsGitRepo { get; }

    /// <summary>
    /// Whether Git is available on the system PATH.
    /// </summary>
    bool IsGitInstalled { get; }

    /// <summary>
    /// Initialize the service for the given project root directory.
    /// Discovers the enclosing Git repository, if any.
    /// </summary>
    Task InitializeAsync(string projectRoot);

    /// <summary>
    /// Returns full repository status including branch, remote info, and changed files.
    /// Returns null if not inside a Git repo.
    /// </summary>
    Task<GitRepoInfo?> GetStatusAsync();

    /// <summary>
    /// Stage the given files (relative to repo root) and create a commit.
    /// Returns null on success, or an error message on failure.
    /// </summary>
    Task<string?> CommitAsync(IEnumerable<string> relativePaths, string message);

    /// <summary>
    /// Push to the default remote.
    /// Returns null on success, or an error message on failure.
    /// </summary>
    Task<string?> PushAsync();

    /// <summary>
    /// Pull from the default remote.
    /// Returns null on success, or an error message on failure.
    /// </summary>
    Task<string?> PullAsync();

    /// <summary>
    /// Get the status of a specific file relative to the project root.
    /// </summary>
    GitFileStatus GetFileStatus(string projectRelativePath);

    /// <summary>
    /// Discard working tree changes for the given files (git checkout -- files).
    /// Returns null on success, or an error message on failure.
    /// </summary>
    Task<string?> DiscardChangesAsync(IEnumerable<string> relativePaths);

    /// <summary>
    /// The most recent commits, newest first. The one long-history path the
    /// product ships was write-only in the app: commits went in and nothing
    /// could read them back out.
    /// </summary>
    Task<IReadOnlyList<GitCommit>> GetLogAsync(int limit);

    /// <summary>Paths a commit touched, relative to the repository root.</summary>
    Task<IReadOnlyList<string>> GetCommitFilesAsync(string sha);

    /// <summary>
    /// A unified diff for one path. With <paramref name="sha"/> given, the
    /// change that commit made; without it, the working tree against HEAD.
    /// </summary>
    Task<string> GetDiffAsync(string? sha, string relativePath);

    /// <summary>Local branches, with the checked-out one marked.</summary>
    Task<IReadOnlyList<GitBranch>> GetBranchesAsync();

    /// <summary>
    /// Creates a branch and switches to it. Returns null on success, or the
    /// error git reported.
    /// </summary>
    Task<string?> CreateBranchAsync(string name);

    /// <summary>Switches to an existing branch. Null on success.</summary>
    Task<string?> SwitchBranchAsync(string name);

    /// <summary>
    /// Turns the project folder into a repository. Null on success. This is
    /// the one thing a writer cannot do from inside the app without it, and
    /// the point where every other Git feature becomes reachable.
    /// </summary>
    Task<string?> InitRepositoryAsync(string projectRoot);
}
