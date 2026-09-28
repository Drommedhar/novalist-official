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

    [JsonRpcMethod("backup/create")]
    public async Task<BackupDto?> CreateAsync(string trigger)
    {
        var info = await (await _workspace.GetBackupServiceAsync()).CreateAsync(trigger);
        return info == null ? null : ToDto(info);
    }

    /// <summary>
    /// Archives the project under a name the writer chose. Named archives are
    /// milestones and survive retention.
    /// </summary>
    [JsonRpcMethod("backup/createMilestone")]
    public async Task<BackupDto?> CreateMilestoneAsync(string name)
    {
        var info = await (await _workspace.GetBackupServiceAsync()).CreateAsync("milestone", name);
        return info == null ? null : ToDto(info);
    }

    [JsonRpcMethod("backup/delete")]
    public async Task<bool> DeleteAsync(string backupId) =>
        await (await _workspace.GetBackupServiceAsync()).DeleteAsync(backupId);

    [JsonRpcMethod("backup/list")]
    public async Task<BackupDto[]> ListAsync()
    {
        var backups = await (await _workspace.GetBackupServiceAsync()).ListAsync();
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

        if (!await (await _workspace.GetBackupServiceAsync()).RestoreAsync(backupId))
            return false;

        await _workspace.OpenProjectAsync(root);
        return true;
    }

    [JsonRpcMethod("backup/prune")]
    public async Task<BackupDto[]> PruneAsync()
    {
        await (await _workspace.GetBackupServiceAsync()).PruneAsync();
        return await ListAsync();
    }

    [JsonRpcMethod("backup/restoreAsNewProject")]
    public async Task<ProjectStateDto> RestoreAsNewProjectAsync(string archivePath, string parentDirectory, string projectName)
    {
        var root = await (await _workspace.GetBackupServiceAsync())
            .RestoreAsNewProjectAsync(archivePath, parentDirectory, projectName);
        return await _workspace.OpenProjectAsync(root);
    }

    /// <summary>
    /// Whether an interval backup is due. The renderer polls this rather than the
    /// core owning a timer, so a backup never fires while the app is in the
    /// background with no project open.
    /// </summary>
    [JsonRpcMethod("backup/isDue")]
    public async Task<bool> IsDueAsync() =>
        await (await _workspace.GetBackupServiceAsync()).IsDueAsync(DateTime.UtcNow);

    [JsonRpcMethod("backup/folder")]
    public async Task<string> FolderAsync() =>
        (await _workspace.GetBackupServiceAsync()).GetBackupFolder() ?? string.Empty;

    private static BackupDto ToDto(Core.Models.BackupInfo b) =>
        new(b.Id, b.Path, b.CreatedAt.ToString("o"), b.SizeBytes, b.Trigger, b.IsMilestone, b.Name);
}

public sealed record BackupDto(
    string Id, string Path, string CreatedAt, long SizeBytes, string Trigger,
    bool IsMilestone = false, string Name = "");
