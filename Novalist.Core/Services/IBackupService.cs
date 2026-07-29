using Novalist.Core.Models;

namespace Novalist.Core.Services;

public interface IBackupService
{
    /// <summary>
    /// Archives the open project. <paramref name="trigger"/> is recorded in the
    /// archive name ("open", "close", "interval", "manual"). Returns null when no
    /// project is open or backups are disabled. Prunes to the retention count
    /// after a successful write.
    /// </summary>
    Task<BackupInfo?> CreateAsync(string trigger);

    /// <summary>
    /// Archives the open project under a name the writer chose. A named archive
    /// is a milestone: it is exempt from retention, so "draft two" is still
    /// there months later. Taken even when automatic backups are switched off,
    /// because asking for one is deliberate.
    /// </summary>
    Task<BackupInfo?> CreateAsync(string trigger, string? milestoneName);

    /// <summary>Archives for the open project, newest first.</summary>
    Task<IReadOnlyList<BackupInfo>> ListAsync();

    /// <summary>
    /// Restores an archive over the project folder. Takes a "pre-restore" archive
    /// first so the restore itself is undoable. Returns false when the id is
    /// unknown or no project is open.
    /// </summary>
    Task<bool> RestoreAsync(string backupId);

    /// <summary>
    /// Deletes archives beyond the retention count, oldest first. Milestones are
    /// not counted and never deleted.
    /// </summary>
    Task PruneAsync();

    /// <summary>
    /// Deletes one archive by id. Returns false when the id is unknown. This is
    /// the only way to remove a milestone, since retention will not.
    /// </summary>
    Task<bool> DeleteAsync(string backupId);

    /// <summary>
    /// True when enough time has passed since the last archive for an interval
    /// backup to be due. False when backups or the interval are disabled.
    /// </summary>
    Task<bool> IsDueAsync(DateTime utcNow);

    /// <summary>Absolute folder the open project's archives live in.</summary>
    string? GetBackupFolder();
}
