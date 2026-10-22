using System.Security.Cryptography;
using System.Text.Json;

namespace Novalist.Sync.Client;

public static partial class ProjectCheckpoint
{
    public static async Task<SharedProjectState> InstallOrUpdateAsync(Stream source, string destination, CancellationToken ct)
    {
        destination = Path.GetFullPath(destination);
        SharedProjectPath.Resolve(destination, ".novalist/project.json");
        if (!Directory.Exists(destination)) return await InstallAsync(source, destination, long.MaxValue, ct);
        var existingMetadata = SharedProjectPath.Resolve(destination, ".novalist/project.json");
        if (!File.Exists(existingMetadata) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new IOException("Choose an empty folder or an existing copy of this project.");
        var staging = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination))!, ".novalist-bootstrap-" + Guid.NewGuid().ToString("N"));
        try
        {
            var state = await InstallAsync(source, staging, long.MaxValue, ct);
            if (File.Exists(existingMetadata))
            {
                var existingId = await ReadProjectIdentityAsync(existingMetadata, ct);
                var incomingId = await ReadProjectIdentityAsync(SharedProjectPath.Resolve(staging, ".novalist/project.json"), ct);
                if (string.IsNullOrWhiteSpace(existingId) || existingId != incomingId)
                    throw new IOException("The selected folder belongs to a different project. Choose an empty folder or a copy of this project.");
            }
            await SharedProjectMetadata.MergeLocalSettingsAsync(destination, staging, ct);
            var changes = await PlanUpdateAsync(destination, staging, ct);
            if (changes.Count != 0) await ApplyUpdateAsync(destination, changes, ct);
            return state;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
    }

    private sealed record InstallChange(string Path, string Target, string? Replacement, bool HasOriginal);

    private static async Task<string?> ReadProjectIdentityAsync(string path, CancellationToken ct)
    {
        await using var input = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(input, cancellationToken: ct);
        return document.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
    }

    private static async Task<List<InstallChange>> PlanUpdateAsync(string destination, string staging, CancellationToken ct)
    {
        var incoming = ProjectContentSelection.Read(staging).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existing = File.Exists(SharedProjectPath.Resolve(destination, ".novalist/project.json"))
            ? ProjectContentSelection.Read(destination) : [];
        var changes = new List<InstallChange>();
        foreach (var relative in incoming.Concat(existing).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var target = SharedProjectPath.Resolve(destination, relative);
            var replacement = incoming.Contains(relative) ? SharedProjectPath.Resolve(staging, relative) : null;
            if (Directory.Exists(target)) throw new IOException("A local folder conflicts with a shared file.");
            var hasOriginal = File.Exists(target);
            if (hasOriginal && replacement is not null && await FilesMatchAsync(target, replacement, ct)) continue;
            changes.Add(new(relative, target, replacement, hasOriginal));
        }
        return changes;
    }

    private static async Task<bool> FilesMatchAsync(string first, string second, CancellationToken ct)
    {
        await using var left = File.OpenRead(first);
        await using var right = File.OpenRead(second);
        if (left.Length != right.Length) return false;
        var firstHash = await SHA256.HashDataAsync(left, ct);
        var secondHash = await SHA256.HashDataAsync(right, ct);
        return firstHash.AsSpan().SequenceEqual(secondHash);
    }

    private static async Task ApplyUpdateAsync(string destination, IReadOnlyList<InstallChange> changes, CancellationToken ct)
    {
        var metadata = SharedProjectPath.Resolve(destination, ".novalist");
        var recoveryParent = Path.Combine(metadata, "recovered-joins");
        if (new DirectoryInfo(recoveryParent).LinkTarget is not null ||
            Directory.Exists(recoveryParent) && (File.GetAttributes(recoveryParent) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Recovered project files cannot follow symbolic links.");
        var recovery = Path.Combine(recoveryParent, Guid.NewGuid().ToString("N"));
        foreach (var change in changes.Where(change => change.HasOriginal))
        {
            var backup = SharedProjectPath.Resolve(recovery, change.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            await using var input = File.OpenRead(change.Target);
            await using var output = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await input.CopyToAsync(output, ct);
            await output.FlushAsync(ct);
            output.Flush(flushToDisk: true);
        }
        ct.ThrowIfCancellationRequested();
        var applied = new List<InstallChange>();
        try
        {
            foreach (var change in changes)
            {
                SharedProjectPath.Resolve(destination, change.Path);
                if (change.Replacement is null) File.Delete(change.Target);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(change.Target)!);
                    File.Move(change.Replacement, change.Target, overwrite: true);
                }
                applied.Add(change);
            }
        }
        catch (Exception original) when (original is IOException or UnauthorizedAccessException)
        {
            var failures = new List<Exception> { original };
            foreach (var change in applied.AsEnumerable().Reverse())
            {
                try
                {
                    SharedProjectPath.Resolve(destination, change.Path);
                    if (change.HasOriginal) File.Copy(SharedProjectPath.Resolve(recovery, change.Path), change.Target, overwrite: true);
                    else File.Delete(change.Target);
                }
                catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException) { failures.Add(rollback); }
            }
            if (failures.Count != 1) throw new AggregateException("The local project could not be restored. Its original files remain in recovered joins.", failures);
            throw;
        }
    }
}
