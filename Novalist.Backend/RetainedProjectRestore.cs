#if NOVALIST_COLLABORATION
using Novalist.Core.Services;
using Novalist.Sync.Client;

namespace Novalist.Backend;

internal sealed class RetainedProjectRestore(string root, string staging, string recovery, SharedProjectState state,
    IReadOnlyList<string> originalPaths, byte[]? localSettings) : IDisposable
{
    internal string RecoveryPath => recovery;
    internal SharedProjectState State => state;

    internal static async Task<RetainedProjectRestore> PrepareAsync(string root, Stream checkpoint, string storage, CancellationToken ct)
    {
        var folder = Path.Combine(storage, "recovery", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var staging = Path.Combine(folder, "checkpoint");
        var state = await ProjectCheckpoint.InstallAsync(checkpoint, staging, RetainedProjectCopies.FreeSpace(root), ct);
        var recovery = Path.Combine(folder, "retained-project.zip");
        try
        {
            var paths = ProjectContentSelection.Read(root);
            await using (var backup = new FileStream(recovery, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await ProjectCheckpoint.CreateAsync(root, backup, () => ProjectContentSelection.Read(root), ct);
                await backup.FlushAsync(ct);
                backup.Flush(flushToDisk: true);
            }
            var settingsPath = SharedProjectPath.Resolve(root, ".novalist/settings.json");
            var settings = File.Exists(settingsPath) ? await File.ReadAllBytesAsync(settingsPath, ct) : null;
            if (settings is not null) await new FileService().WriteBytesAsync(Path.Combine(folder, "personal-settings.json"), settings);
            return new(root, staging, recovery, state, paths, settings);
        }
        catch { Directory.Delete(staging, recursive: true); throw; }
    }

    internal Task ApplyAsync(IFileService files, CancellationToken ct) => CopyAsync(staging, files, originalPaths, ct);

    internal async Task RollbackAsync(IFileService files)
    {
        var rollback = Path.Combine(Path.GetDirectoryName(recovery) ?? throw new InvalidDataException("The recovery folder is missing."), "rollback");
        await using var archive = File.OpenRead(recovery);
        await ProjectCheckpoint.InstallAsync(archive, rollback, RetainedProjectCopies.FreeSpace(root), CancellationToken.None);
        try
        {
            await CopyAsync(rollback, files, ProjectContentSelection.Read(staging), CancellationToken.None);
            if (localSettings is not null) await files.WriteBytesAsync(SharedProjectPath.Resolve(root, ".novalist/settings.json"), localSettings);
        }
        finally { Directory.Delete(rollback, recursive: true); }
    }

    private async Task CopyAsync(string source, IFileService files, IReadOnlyList<string> previous, CancellationToken ct)
    {
        var paths = ProjectContentSelection.Read(source);
        foreach (var relative in paths.OrderBy(path => path == ".novalist/project.json"))
        {
            ct.ThrowIfCancellationRequested();
            var bytes = await File.ReadAllBytesAsync(SharedProjectPath.Resolve(source, relative), ct);
            if (relative == ".novalist/settings.json" && localSettings is not null)
            {
                var shared = await SharedProjectFiles.FilterContentAsync(relative, localSettings, ct);
                var merged = SharedFileMerge.Apply(new(relative, shared, bytes), localSettings);
                bytes = merged.Content ?? throw new InvalidDataException("The shared project settings could not be restored.");
            }
            await files.WriteBytesAsync(SharedProjectPath.Resolve(root, relative), bytes);
        }
        foreach (var relative in previous.Except(paths, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var path = SharedProjectPath.Resolve(root, relative);
            if (await files.ExistsAsync(path)) await files.DeleteFileAsync(path);
            await PruneEmptyParentsAsync(relative, files);
        }
    }

    private async Task PruneEmptyParentsAsync(string relative, IFileService files)
    {
        while (relative.LastIndexOf('/') is var separator && separator > 0)
        {
            relative = relative[..separator];
            var path = SharedProjectPath.Resolve(root, relative);
            if (!Directory.Exists(path) || Directory.EnumerateFileSystemEntries(path).Any()) return;
            await files.DeleteDirectoryAsync(path, recursive: false);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
    }
}
#endif
