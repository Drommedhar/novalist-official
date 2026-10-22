#if NOVALIST_COLLABORATION
using Novalist.Sync.Client;

namespace Novalist.Backend;

public sealed partial class Workspace
{
    private async Task<byte[]?> ReadFilteredSharedFileAsync(string path, string relative) => await FileService.ExistsAsync(path)
        ? await SharedProjectFiles.FilterContentAsync(relative, await FileService.ReadBytesAsync(path), CancellationToken.None) : null;

    private async Task PrepareSharedProjectionPathAsync(string relative)
    {
        if (sharedProjectFiles is null || sharedRoot is not { } root) return;
        var path = SharedProjectPath.Resolve(root, relative);
        var original = sharedDiskFiles.GetValueOrDefault(relative);
        var tracked = sharedPendingWrites.TryGetValue(relative, out var pending);
        var expected = tracked ? pending : original;
        var actual = await ReadFilteredSharedFileAsync(path, relative);
        if (SameFileBytes(expected, actual)) return;
        var content = await FileService.ExistsAsync(path) ? await FileService.ReadBytesAsync(path) : null;
        if (content is not null) await WriteSharedFileRecoveryAsync(root, Guid.NewGuid().ToString("N"), relative, content);
        var restored = SharedFileMerge.Apply(new(relative, actual, expected), content).Content;
        if (restored is null) await FileService.DeleteFileAsync(path);
        else await FileService.WriteBytesAsync(path, restored);
        if (original is null) sharedDiskFiles.Remove(relative);
        else sharedDiskFiles[relative] = original;
        if (tracked) sharedPendingWrites[relative] = pending;
    }

    private async Task ValidatePendingSharedFilesAsync(string root, Dictionary<string, byte[]> before,
        Dictionary<string, byte[]> current, CancellationToken ct)
    {
        var changed = before.Keys.Concat(current.Keys).Distinct(StringComparer.Ordinal)
            .Where(path => !SameFileBytes(before.GetValueOrDefault(path), current.GetValueOrDefault(path))).ToArray();
        var untracked = changed.Any(path => !sharedPendingWrites.TryGetValue(path, out var expected) ||
            !SameFileBytes(expected, current.GetValueOrDefault(path)));
        if (!sharedWriteRejected && !untracked) return;
        using var projection = BeginSharedProjection();
        var recovery = Guid.NewGuid().ToString("N");
        foreach (var relative in changed)
        {
            var path = SharedProjectPath.Resolve(root, relative);
            var content = await FileService.ExistsAsync(path) ? await FileService.ReadBytesAsync(path) : null;
            if (content is not null) await WriteSharedFileRecoveryAsync(root, recovery, relative, content);
            var original = before.GetValueOrDefault(relative);
            var restored = SharedFileMerge.Apply(new(relative, current.GetValueOrDefault(relative), original), content).Content;
            if (restored is null) await FileService.DeleteFileAsync(path);
            else await FileService.WriteBytesAsync(path, restored);
        }
        sharedDiskFiles = await ReadSharedFilesAsync(root, ct);
        sharedPendingWrites.Clear();
        sharedWriteRejected = false;
        await ReloadSharedWorkspaceAsync();
        if (untracked) throw new InvalidOperationException("Files changed outside the collaboration session. Their contents were preserved in Recovered edits; reopen them there before retrying.");
    }

    private async Task ReloadSharedWorkspaceAsync()
    {
        await Projects.ReloadSharedProjectAsync();
        Settings.SetActiveOverrides(Projects.ProjectSettings.Overrides);
    }
}
#endif
