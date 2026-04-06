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
}
