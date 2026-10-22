using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Novalist.Sync.Client;

public sealed record CollaborationTransferProgress(string Phase, int CompletedFiles, int TotalFiles,
    long CompletedBytes, long TotalBytes, int ReusedFiles = 0);

public static class PeerBootstrapTransfer
{
    public static async Task<SharedProjectState> DownloadAsync(TcpPeerTransport transport, string destination,
        Stream reconstructedArchive, IProgress<CollaborationTransferProgress>? progress, CancellationToken ct)
    {
        if (!reconstructedArchive.CanRead || !reconstructedArchive.CanWrite || !reconstructedArchive.CanSeek)
            throw new ArgumentException("The local project snapshot must be readable, writable and seekable.", nameof(reconstructedArchive));
        var meter = new BootstrapProgress(progress);
        meter.Report("scanning");
        var manifest = await ReadManifestAsync(transport, ct);
        var matches = await CompareAsync(destination, manifest, meter, ct);
        var bytes = manifest.Files.Where(file => !matches.Contains(file.Path)).Sum(file => file.Length) +
            (manifest.Documents ?? []).Sum(document => document.Length) + (manifest.Baselines ?? []).Sum(baseline => baseline.Length);
        meter.Report("comparing", matches.Count, manifest.Files.Count, 0, bytes, matches.Count);
        reconstructedArchive.SetLength(0);
        using (var archive = new ZipArchive(reconstructedArchive, ZipArchiveMode.Create, leaveOpen: true))
        {
            meter.Report("transferring", matches.Count, manifest.Files.Count, 0, bytes, matches.Count);
            foreach (var file in manifest.Files)
            {
                await using var target = archive.CreateEntry("files/" + file.Path, CompressionLevel.Fastest).Open();
                if (matches.Contains(file.Path)) await CopyLocalAsync(destination, file, target, ct);
                else
                {
                    await DownloadEntryAsync(transport, "files/" + file.Path, file.Length, file.Sha256, target, meter.AddBytes, ct);
                    meter.CompleteFile();
                }
            }
            foreach (var document in manifest.Documents ?? [])
            {
                var name = $"state/{document.ItemId:N}";
                await using var target = archive.CreateEntry(name, CompressionLevel.Fastest).Open();
                await DownloadEntryAsync(transport, name, document.Length, document.Sha256, target, meter.AddBytes, ct);
            }
            foreach (var baseline in manifest.Baselines ?? [])
            {
                var name = CheckpointBaselines.EntryName(baseline.Path);
                await using var target = archive.CreateEntry(name, CompressionLevel.Fastest).Open();
                await DownloadEntryAsync(transport, name, baseline.Length, baseline.Sha256, target, meter.AddBytes, ct);
            }
            await using var metadata = archive.CreateEntry("manifest.json", CompressionLevel.Fastest).Open();
            await JsonSerializer.SerializeAsync(metadata, manifest, cancellationToken: ct);
        }
        meter.Phase("installing");
        reconstructedArchive.Position = 0;
        var state = await ProjectCheckpoint.InstallOrUpdateAsync(reconstructedArchive, destination, ct);
        reconstructedArchive.Position = 0;
        meter.Phase("complete");
        return state;
    }

    private static async Task<CheckpointManifest> ReadManifestAsync(TcpPeerTransport transport, CancellationToken ct)
    {
        var description = await transport.BeginBootstrapAsync(ct);
        using var bytes = new MemoryStream();
        await DownloadEntryAsync(transport, "manifest.json", description.ManifestLength, description.ManifestSha256, bytes, _ => { }, ct);
        bytes.Position = 0;
        var manifest = await JsonSerializer.DeserializeAsync<CheckpointManifest>(bytes, cancellationToken: ct)
            ?? throw new InvalidDataException("Missing project manifest.");
        ProjectCheckpoint.ValidateManifest(manifest, long.MaxValue);
        return manifest;
    }

    private static async Task<HashSet<string>> CompareAsync(string destination, CheckpointManifest manifest, BootstrapProgress meter, CancellationToken ct)
    {
        var matches = new HashSet<string>(StringComparer.Ordinal);
        var scanned = 0;
        foreach (var file in manifest.Files)
        {
            var path = SharedProjectPath.Resolve(destination, file.Path);
            if (File.Exists(path))
            {
                await using var input = File.OpenRead(path);
                using var projected = await SharedProjectMetadata.ProjectAsync(file.Path, input, ct);
                var content = projected ?? (Stream)input;
                if (content.Length == file.Length && string.Equals(file.Sha256,
                        Convert.ToHexStringLower(await SHA256.HashDataAsync(content, ct)), StringComparison.OrdinalIgnoreCase))
                    matches.Add(file.Path);
            }
            meter.Report("scanning", ++scanned, manifest.Files.Count, 0, 0, matches.Count);
        }
        return matches;
    }

    private static async Task CopyLocalAsync(string destination, CheckpointFile file, Stream target, CancellationToken ct)
    {
        await using var input = File.OpenRead(SharedProjectPath.Resolve(destination, file.Path));
        using var projected = await SharedProjectMetadata.ProjectAsync(file.Path, input, ct);
        var content = projected ?? (Stream)input;
        await content.CopyToAsync(target, ct);
    }

    private static async Task DownloadEntryAsync(TcpPeerTransport transport, string name, long length, string expectedHash,
        Stream target, Action<int> transferred, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long offset = 0;
        while (offset < length)
        {
            var bytes = await transport.ReadBootstrapAsync(name, offset, ct);
            if (bytes.Length != Math.Min(PeerBootstrapArchive.ChunkBytes, length - offset))
                throw new InvalidDataException("The shared file transfer is incomplete.");
            hash.AppendData(bytes);
            await target.WriteAsync(bytes, ct);
            offset += bytes.Length;
            transferred(bytes.Length);
        }
        if (!string.Equals(expectedHash, Convert.ToHexStringLower(hash.GetHashAndReset()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The shared file content does not match its SHA-256 hash.");
    }
}
