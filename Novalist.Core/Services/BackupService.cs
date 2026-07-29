using System.Globalization;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>
/// Whole-project archiving with rotating retention.
///
/// Archives are written outside the project folder on purpose: a backup stored
/// inside the project is destroyed by exactly the accident it exists to survive,
/// and it would be picked up by git and by file sync. <c>.git</c> is excluded
/// because it is already version control, so archiving it doubles the size for
/// no recovery value the user does not already have.
/// </summary>
public sealed class BackupService : IBackupService
{
    /// <summary>Directories never archived, matched by name at any depth.</summary>
    public static readonly string[] ExcludedDirectories = [".git"];

    internal const int MinIntervalMinutes = 5;
    internal const int MaxIntervalMinutes = 1440;
    internal const int MinRetention = 1;
    internal const int MaxRetention = 100;
    internal const int MaxLabelLength = 60;

    private const string Stamp = "yyyyMMdd-HHmmss";

    private readonly IProjectService _projectService;
    private readonly IFileService _fileService;
    private readonly IArchiveService _archiveService;
    private readonly ISettingsService _settingsService;
    private readonly string _defaultRoot;

    public BackupService(
        IProjectService projectService,
        IFileService fileService,
        IArchiveService archiveService,
        ISettingsService settingsService,
        string? defaultBackupRoot = null)
    {
        _projectService = projectService;
        _fileService = fileService;
        _archiveService = archiveService;
        _settingsService = settingsService;
        _defaultRoot = defaultBackupRoot ?? _fileService.CombinePath(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Novalist",
            "Backups");
    }

    private AppSettings Settings => _settingsService.Settings;

    internal int EffectiveInterval =>
        Settings.BackupIntervalMinutes <= 0
            ? 0
            : Math.Clamp(Settings.BackupIntervalMinutes, MinIntervalMinutes, MaxIntervalMinutes);

    internal int EffectiveRetention =>
        Math.Clamp(Settings.BackupRetentionCount, MinRetention, MaxRetention);

    public string? GetBackupFolder()
    {
        var root = _projectService.ProjectRoot;
        return string.IsNullOrWhiteSpace(root) ? null : ResolveBackupFolder(root);
    }

    /// <summary>
    /// Folder for a project root already known to be non-empty. Split out so
    /// callers that have validated the root do not need a second null branch
    /// that can never be taken.
    /// </summary>
    private string ResolveBackupFolder(string projectRoot)
    {
        var configured = Settings.BackupFolder;
        var baseDir = string.IsNullOrWhiteSpace(configured) ? _defaultRoot : configured;
        return _fileService.CombinePath(baseDir, SafeFolderName(_fileService.GetFileName(projectRoot)));
    }

    public Task<BackupInfo?> CreateAsync(string trigger) => CreateAsync(trigger, null);

    public async Task<BackupInfo?> CreateAsync(string trigger, string? milestoneName)
    {
        var projectRoot = _projectService.ProjectRoot;

        // A milestone is deliberate, so it is taken even when the automatic
        // backups behind it are switched off. Refusing "keep this version"
        // because a rotating schedule is disabled would be the wrong reading of
        // both settings.
        var milestone = !string.IsNullOrWhiteSpace(milestoneName);
        if (string.IsNullOrWhiteSpace(projectRoot) || (!Settings.BackupEnabled && !milestone))
            return null;

        var folder = ResolveBackupFolder(projectRoot);
        await _fileService.CreateDirectoryAsync(folder);

        var safeTrigger = milestone
            ? BackupInfo.MilestonePrefix + SafeLabel(milestoneName!)
            : SafeTrigger(trigger);
        var id = $"{DateTime.UtcNow.ToString(Stamp, CultureInfo.InvariantCulture)}-{safeTrigger}";
        var path = _fileService.CombinePath(folder, id + ".zip");

        await _archiveService.CreateFromDirectoryAsync(projectRoot, path, ExcludedDirectories);

        var info = new BackupInfo
        {
            Id = id,
            Path = path,
            CreatedAt = ParseStamp(id) ?? DateTime.UtcNow,
            SizeBytes = await _fileService.GetFileSizeAsync(path),
            Trigger = safeTrigger
        };

        await PruneAsync();
        return info;
    }

    public async Task<IReadOnlyList<BackupInfo>> ListAsync()
    {
        var folder = GetBackupFolder();
        if (folder == null || !await _fileService.DirectoryExistsAsync(folder))
            return Array.Empty<BackupInfo>();

        var files = await _fileService.GetFilesAsync(folder, "*.zip");
        var result = new List<BackupInfo>(files.Count);
        foreach (var file in files)
        {
            var id = _fileService.GetFileNameWithoutExtension(file);
            var created = ParseStamp(id);
            if (created == null)
                continue;

            result.Add(new BackupInfo
            {
                Id = id,
                Path = file,
                CreatedAt = created.Value,
                SizeBytes = await _fileService.GetFileSizeAsync(file),
                Trigger = TriggerOf(id)
            });
        }

        return result.OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id).ToList();
    }

    public async Task<bool> RestoreAsync(string backupId)
    {
        var projectRoot = _projectService.ProjectRoot;
        if (string.IsNullOrWhiteSpace(projectRoot))
            return false;

        var backups = await ListAsync();
        var target = backups.FirstOrDefault(
            b => string.Equals(b.Id, backupId, StringComparison.OrdinalIgnoreCase));
        if (target == null)
            return false;

        // Archive the current state first so restoring is itself undoable, even
        // when the user restores the wrong archive.
        await CreateAsync("prerestore");

        await _archiveService.ExtractToDirectoryAsync(target.Path, projectRoot);
        return true;
    }

    public async Task PruneAsync()
    {
        // Milestones are outside retention entirely - they neither fill the
        // quota nor get rotated out. A named version that quietly disappeared
        // after ten more saves would be worse than never offering to keep it.
        var backups = (await ListAsync()).Where(b => !b.IsMilestone).ToList();
        var keep = EffectiveRetention;
        if (backups.Count <= keep)
            return;

        foreach (var stale in backups.Skip(keep))
            await _fileService.DeleteFileAsync(stale.Path);
    }

    public async Task<bool> DeleteAsync(string backupId)
    {
        var target = (await ListAsync()).FirstOrDefault(
            b => string.Equals(b.Id, backupId, StringComparison.OrdinalIgnoreCase));
        if (target == null)
            return false;

        await _fileService.DeleteFileAsync(target.Path);
        return true;
    }

    public async Task<bool> IsDueAsync(DateTime utcNow)
    {
        if (!Settings.BackupEnabled)
            return false;

        var interval = EffectiveInterval;
        if (interval <= 0)
            return false;

        var backups = await ListAsync();
        if (backups.Count == 0)
            return true;

        return utcNow - backups[0].CreatedAt >= TimeSpan.FromMinutes(interval);
    }

    /// <summary>
    /// Recovers the UTC timestamp from an archive id. Returns null for a file
    /// the user dropped into the folder that does not follow the naming scheme,
    /// which is how those get skipped rather than crashing the list.
    /// </summary>
    internal static DateTime? ParseStamp(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length < Stamp.Length)
            return null;

        return DateTime.TryParseExact(
            id[..Stamp.Length],
            Stamp,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static string TriggerOf(string id)
    {
        var dash = id.IndexOf('-', Stamp.Length);
        return dash >= 0 && dash + 1 < id.Length ? id[(dash + 1)..] : string.Empty;
    }

    private static string SafeTrigger(string trigger)
    {
        if (string.IsNullOrWhiteSpace(trigger))
            return "manual";

        var cleaned = new string(trigger.Where(char.IsLetterOrDigit).ToArray());
        return cleaned.Length == 0 ? "manual" : cleaned.ToLowerInvariant();
    }

    /// <summary>
    /// A milestone name reduced to what can live in a file name, keeping the
    /// writer's capitals: "Draft two, revised" becomes "Draft-two-revised". The
    /// name is stored in the archive name rather than an index so that it
    /// survives the archive being copied somewhere else.
    /// </summary>
    private static string SafeLabel(string name)
    {
        var cleaned = new string(name
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ')
            .ToArray());
        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var label = string.Join('-', words);
        return label.Length <= MaxLabelLength ? label : label[..MaxLabelLength].TrimEnd('-');
    }

    private static string SafeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Project";

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return cleaned.Length == 0 ? "Project" : cleaned;
    }
}
