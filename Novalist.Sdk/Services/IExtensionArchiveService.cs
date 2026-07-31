namespace Novalist.Sdk.Services;

/// <summary>One stored version of a scene.</summary>
public sealed class SnapshotInfo
{
    public string Id { get; init; } = string.Empty;
    public string ChapterGuid { get; init; } = string.Empty;
    public string SceneId { get; init; } = string.Empty;

    /// <summary>What the writer called it, or empty for an automatic one.</summary>
    public string Label { get; init; } = string.Empty;

    public DateTime CreatedAt { get; init; }
    public int WordCount { get; init; }
}

/// <summary>A file inside the project folder.</summary>
public sealed class ProjectFileInfo
{
    /// <summary>Path relative to the project root, with forward slashes.</summary>
    public string RelativePath { get; init; } = string.Empty;

    public long SizeBytes { get; init; }
    public DateTime ModifiedAt { get; init; }
}

/// <summary>
/// The project as files and stored versions, for backup, comparison and
/// archive tooling.
///
/// Novalist has kept scene snapshots since early on and an extension could not
/// see one, so version tooling had to build a second store beside the one the
/// writer already uses - two histories of the same book, neither aware of the
/// other. The same went for drafts: an extension could only read the active
/// one, which makes comparing two drafts, the single most obvious thing to
/// want, impossible to write outside core.
///
/// Everything here is read-only except taking and restoring a snapshot, both
/// of which are what the writer can already do by hand.
/// </summary>
public interface IExtensionArchiveService
{
    /// <summary>Stored versions of a scene, newest first.</summary>
    Task<IReadOnlyList<SnapshotInfo>> ListSnapshotsAsync(string chapterGuid, string sceneId);

    /// <summary>The prose a snapshot holds, or null when the id is unknown.</summary>
    Task<string?> ReadSnapshotAsync(string chapterGuid, string sceneId, string snapshotId);

    /// <summary>
    /// Stores the scene as it stands now and returns the snapshot's id. What a
    /// pass should do before it rewrites anything.
    /// </summary>
    Task<string?> TakeSnapshotAsync(string chapterGuid, string sceneId, string label);

    /// <summary>
    /// Puts a stored version back. The state being replaced is snapshotted
    /// first, so a restore is itself undoable.
    /// </summary>
    /// <returns>False when the scene or the snapshot is unknown.</returns>
    Task<bool> RestoreSnapshotAsync(string chapterGuid, string sceneId, string snapshotId);

    /// <summary>
    /// Chapters of a draft of the active book, in order - including a draft
    /// that is not the open one. Empty when the draft is unknown.
    /// </summary>
    Task<IReadOnlyList<ChapterInfo>> GetChaptersOfDraftAsync(string draftId);

    /// <summary>
    /// A scene's prose from a draft that need not be the open one, or null.
    /// This is what a comparison between two drafts is made of.
    /// </summary>
    Task<string?> ReadSceneOfDraftAsync(string draftId, string chapterGuid, string sceneId);

    /// <summary>
    /// Every file in the project folder, for archive and backup tooling.
    /// Paths are relative to the project root so they mean the same thing after
    /// the folder moves.
    /// </summary>
    IReadOnlyList<ProjectFileInfo> ListProjectFiles();

    /// <summary>
    /// Reads a project file as bytes. Null when it is not there, or when the
    /// path points outside the project - a relative path that climbs out would
    /// otherwise let an extension read anything on the machine.
    /// </summary>
    Task<byte[]?> ReadProjectFileAsync(string relativePath);
}
