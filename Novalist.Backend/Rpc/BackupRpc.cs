using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Whole-project archiving with rotating retention.</summary>
public sealed class BackupRpc
{
    private readonly Workspace _workspace;

    public BackupRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private BackupService Service => new(
        _workspace.Projects,
        _workspace.FileService,
        _workspace.ArchiveService,
        _workspace.Settings);

    [JsonRpcMethod("backup/create")]
    public async Task<BackupDto?> CreateAsync(string trigger)
    {
        var info = await Service.CreateAsync(trigger);
        return info == null ? null : ToDto(info);
    }

    /// <summary>
    /// Archives the project under a name the writer chose. Named archives are
    /// milestones and survive retention.
    /// </summary>
    [JsonRpcMethod("backup/createMilestone")]
    public async Task<BackupDto?> CreateMilestoneAsync(string name)
    {
        var info = await Service.CreateAsync("milestone", name);
        return info == null ? null : ToDto(info);
    }

    [JsonRpcMethod("backup/delete")]
    public Task<bool> DeleteAsync(string backupId) => Service.DeleteAsync(backupId);

    [JsonRpcMethod("backup/list")]
    public async Task<BackupDto[]> ListAsync()
    {
        var backups = await Service.ListAsync();
        return backups.Select(ToDto).ToArray();
    }

    /// <summary>
    /// Restores an archive over the project folder and reopens the project so the
    /// renderer is not left holding models that no longer match what is on disk.
    /// </summary>
    [JsonRpcMethod("backup/restore")]
    public async Task<bool> RestoreAsync(string backupId)
    {
        var root = _workspace.Projects.ProjectRoot;
        if (string.IsNullOrWhiteSpace(root))
            return false;

        if (!await Service.RestoreAsync(backupId))
            return false;

        await _workspace.OpenProjectAsync(root);
        return true;
    }

    [JsonRpcMethod("backup/prune")]
    public async Task<BackupDto[]> PruneAsync()
    {
        await Service.PruneAsync();
        return await ListAsync();
    }

    /// <summary>
    /// Whether an interval backup is due. The renderer polls this rather than the
    /// core owning a timer, so a backup never fires while the app is in the
    /// background with no project open.
    /// </summary>
    [JsonRpcMethod("backup/isDue")]
    public Task<bool> IsDueAsync() => Service.IsDueAsync(DateTime.UtcNow);

    [JsonRpcMethod("backup/folder")]
    public Task<string> FolderAsync() =>
        Task.FromResult(Service.GetBackupFolder() ?? string.Empty);

    private static BackupDto ToDto(Core.Models.BackupInfo b) =>
        new(b.Id, b.Path, b.CreatedAt.ToString("o"), b.SizeBytes, b.Trigger, b.IsMilestone, b.Name);
}

public sealed record BackupDto(
    string Id, string Path, string CreatedAt, long SizeBytes, string Trigger,
    bool IsMilestone = false, string Name = "");
