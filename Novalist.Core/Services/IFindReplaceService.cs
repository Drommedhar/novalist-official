using Novalist.Core.Models;

namespace Novalist.Core.Services;

public interface IFindReplaceService
{
    /// <summary>Returns every match in scope. Cancellation safe.</summary>
    Task<IReadOnlyList<FindMatch>> FindAsync(FindOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces every match in scope. Auto-snapshots each affected scene
    /// before writing. Returns number of replacements made.
    /// </summary>
    Task<int> ReplaceAllAsync(FindOptions options, ISnapshotService? snapshotService = null, CancellationToken cancellationToken = default);
}
