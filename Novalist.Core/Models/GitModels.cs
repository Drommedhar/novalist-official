namespace Novalist.Core.Models;

public enum GitFileStatus
{
    Unmodified,
    Modified,
    Added,
    Deleted,
    Renamed,
    Untracked,
    Ignored,
    Conflicted
}

public record GitFileEntry(string RelativePath, GitFileStatus IndexStatus, GitFileStatus WorkTreeStatus)
{
    /// <summary>
    /// Returns the most significant status for display purposes.
    /// WorkTree changes take precedence over index changes.
    /// </summary>
    public GitFileStatus DisplayStatus => WorkTreeStatus != GitFileStatus.Unmodified
        ? WorkTreeStatus
        : IndexStatus;

    /// <summary>
    /// Whether this entry is staged (in the index).
    /// </summary>
    public bool IsStaged => IndexStatus is not (GitFileStatus.Unmodified or GitFileStatus.Untracked or GitFileStatus.Ignored);
}

public record GitRepoInfo(
    string BranchName,
    bool HasRemote,
    int AheadBy,
    int BehindBy,
    IReadOnlyList<GitFileEntry> ChangedFiles
);
