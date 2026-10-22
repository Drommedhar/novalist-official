#if NOVALIST_COLLABORATION
using Novalist.Sync.Client;

namespace Novalist.Backend;

public sealed partial class Workspace
{
    private SharedProjectFiles? sharedProjectFiles;
    private Dictionary<string, byte[]> sharedDiskFiles = new(StringComparer.Ordinal);
    private bool projectingSharedFiles;

    internal async Task AttachSharedProjectAsync(Guid sessionId, CollaborationSession session,
        SharedProjectState state, Stream archive, CancellationToken ct)
    {
        archive.Position = 0;
        var baseline = await ProjectCheckpoint.ReadFilesAsync(archive, long.MaxValue, ct);
        (state, baseline) = await ReconcileSharedSourceFormattingAsync(state, baseline, ct);
        await AttachSharedProjectAsync(sessionId, session, state, ct);
        var files = new SharedProjectFiles(session, ApplySharedProjectFilesAsync);
        if (DispatchSharedUpdate is { } dispatch) files.DispatchRemote = dispatch;
        try
        {
            sharedProjectFiles = files;
            sharedDiskFiles = await ReadSharedFilesAsync(sharedRoot ?? throw new InvalidOperationException("The shared project is closed."), ct);
            await files.StartAsync(baseline, state, ct);
            sharedDiskFiles = await ReadSharedFilesAsync(sharedRoot ?? throw new InvalidOperationException("The shared project is closed."), ct);
        }
        catch
        {
            sharedProjectFiles = null;
            await files.DisposeAsync();
            await DetachSharedScenesAsync();
            throw;
        }
    }

    internal async Task FlushSharedProjectFilesAsync()
    {
        if (projectingSharedFiles || sharedProjectFiles is not { } files || sharedRoot is not { } root) return;
        var ct = CancellationToken.None;
        var canonical = await files.SnapshotAsync(ct);
        var current = await ReadSharedFilesAsync(root, ct);
        var previous = new Dictionary<string, byte[]>(sharedDiskFiles, StringComparer.Ordinal);
        await ValidatePendingSharedFilesAsync(root, previous, current, ct);
        var changes = new List<SharedFileChange>();
        foreach (var path in previous.Keys.Concat(current.Keys).Distinct(StringComparer.Ordinal))
        {
            var before = previous.GetValueOrDefault(path);
            var after = current.GetValueOrDefault(path);
            var reference = canonical.GetValueOrDefault(path);
            if (before is not null) before = MaskSeparatelySharedContent(path, before, reference);
            if (after is not null) after = MaskSeparatelySharedContent(path, after, reference);
            if (!SameSharedFileContent(path, before, after)) changes.Add(new(path, before, after));
        }
        if (changes.Count > 0)
        {
            try
            {
                var bindings = await CaptureAddedBindingsAsync(changes, ct);
                await files.CommitAsync(changes, bindings, ct);
            }
            catch
            {
                sharedWriteRejected = true;
                await ValidatePendingSharedFilesAsync(root, new(sharedDiskFiles, StringComparer.Ordinal), await ReadSharedFilesAsync(root, ct), ct);
                throw;
            }
        }
        sharedDiskFiles = await ReadSharedFilesAsync(root, ct);
        sharedPendingWrites.Clear();
    }

    private async Task ApplySharedProjectFilesAsync(SharedProjectProjection projection, CancellationToken ct)
    {
        using var projectionScope = BeginSharedProjection();
        var root = sharedRoot ?? throw new InvalidOperationException("The shared project is closed.");
        projectingSharedFiles = true;
        try
        {
            foreach (var recovery in projection.Recoveries)
                await WriteSharedFileRecoveryAsync(root, recovery.Id, recovery.Path, recovery.Content);
            foreach (var change in projection.Changes.OrderBy(change => change.Path == ".novalist/project.json"))
            {
                await PrepareSharedProjectionPathAsync(change.Path);
                var path = SharedProjectPath.Resolve(root, change.Path);
                var current = await FileService.ExistsAsync(path) ? await FileService.ReadBytesAsync(path) : null;
                var structural = current is null ? null : MaskSeparatelySharedContent(change.Path, current, change.Before);
                var merged = SharedFileMerge.Apply(change, structural);
                if (merged.Conflict && current is not null)
                    await WriteSharedFileRecoveryAsync(root, projection.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture), change.Path, current);
                if (merged.Content is { } content)
                    await FileService.WriteBytesAsync(path, current is null ? content : MaskSeparatelySharedContent(change.Path, content, current));
                else await FileService.DeleteFileAsync(path);
            }
            await ReloadSharedWorkspaceAsync();
            await AddSharedProjectBindingsAsync(projection.AddedState, ct);
            foreach (var change in projection.Changes)
            {
                var path = SharedProjectPath.Resolve(root, change.Path);
                if (await FileService.ExistsAsync(path)) sharedDiskFiles[change.Path] = await SharedProjectFiles.FilterContentAsync(
                    change.Path, await FileService.ReadBytesAsync(path), ct);
                else sharedDiskFiles.Remove(change.Path);
            }
            if (Coordinator?.Notify is { } notify)
                await notify("collaboration/projectUpdated", new { sessionId = sharedSession, state = BuildState() });
        }
        finally { projectingSharedFiles = false; }
    }

    private async Task WriteSharedFileRecoveryAsync(string root, string id, string source, byte[] content)
    {
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content));
        var path = SharedProjectPath.Resolve(root, $"Recovered edits/{sharedSession:N}/{id}/{hash}/{Path.GetFileName(source)}");
        await FileService.WriteBytesAsync(path, content);
    }

    private async Task DetachSharedProjectFilesAsync()
    {
        if (sharedProjectFiles is { } files)
        {
            await FlushSharedProjectFilesAsync();
            await files.DisposeAsync();
        }
        sharedProjectFiles = null;
        sharedDiskFiles.Clear();
        sharedPendingWrites.Clear();
        sharedWriteRejected = false;
    }

    private static bool SameFileBytes(byte[]? left, byte[]? right) =>
        left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);
}
#endif
