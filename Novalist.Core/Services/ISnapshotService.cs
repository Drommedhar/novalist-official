using Novalist.Core.Models;

namespace Novalist.Core.Services;

public interface ISnapshotService
{
    Task<SceneSnapshot> TakeAsync(ChapterData chapter, SceneData scene, string label);
    Task<IReadOnlyList<SceneSnapshot>> ListAsync(SceneData scene);
    Task<SceneSnapshot?> LoadAsync(SceneData scene, string snapshotId);
    Task<bool> RestoreAsync(ChapterData chapter, SceneData scene, string snapshotId);
    Task DeleteAsync(SceneData scene, string snapshotId);

    /// <summary>Every snapshot in the book, newest first, with its scene.</summary>
    Task<IReadOnlyList<ProjectSnapshot>> ListAllAsync();

    /// <summary>Renames one snapshot in place.</summary>
    Task<bool> RenameAsync(SceneData scene, string snapshotId, string label);

    /// <summary>
    /// Deletes snapshots nobody is going to want: everything past the newest
    /// <paramref name="keepPerScene"/> of each scene, everything older than
    /// <paramref name="olderThanDays"/>, and folders left behind by scenes that
    /// no longer exist. Returns how many files went.
    /// </summary>
    Task<int> PruneAsync(int keepPerScene, int olderThanDays, bool dropOrphans);

    /// <summary>
    /// Deletes every snapshot carrying an exact label, and returns how many
    /// went.
    ///
    /// One project-wide Replace All takes a snapshot of every scene it touches,
    /// which on a long book is hundreds at once. They were all labelled
    /// identically, so one run could not be told from the next and undoing the
    /// clutter of a single operation meant deleting folders on disk with the
    /// project closed.
    /// </summary>
    Task<int> DeleteByLabelAsync(string label);
}

/// <summary>One snapshot with the scene it belongs to, for the project-wide list.</summary>
public sealed record ProjectSnapshot(
    SceneSnapshot Snapshot,
    string ChapterGuid,
    string ChapterTitle,
    string SceneId,
    string SceneTitle);
