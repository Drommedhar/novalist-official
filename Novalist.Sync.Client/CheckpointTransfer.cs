using System.Security.Cryptography;
using System.Text.Json;

namespace Novalist.Sync.Client;

public sealed record CheckpointReference(Guid ManifestId, string Sha256);

public static class CheckpointTransfer
{
    private const int ChunkSize = 1024 * 1024;
    private const int ManifestLimit = 8 * 1024 * 1024;
    private sealed record Manifest(int Version, long Length, string Sha256, IReadOnlyList<Guid> Chunks);

    public static async Task<CheckpointReference> PublishAsync(CollaborationSession session, Stream archive, CancellationToken cancellationToken)
    {
        if (!session.IsConnected) throw new InvalidOperationException("Connect before publishing a shared project.");
        var chunks = new List<Guid>();
        var buffer = new byte[ChunkSize];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long length = 0;
        int count;
        while ((count = await archive.ReadAtLeastAsync(buffer, ChunkSize, throwOnEndOfStream: false, cancellationToken)) != 0)
        {
            var id = Guid.NewGuid();
            session.Save(id, buffer.AsSpan(0, count).ToArray());
            await session.SynchronizeAsync(createProject: false, cancellationToken);
            chunks.Add(id);
            hash.AppendData(buffer, 0, count);
            length = checked(length + count);
        }
        if (length == 0) throw new InvalidDataException("The project checkpoint is empty.");
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new Manifest(1, length, Convert.ToHexStringLower(hash.GetHashAndReset()), chunks));
        if (manifest.Length > ManifestLimit) throw new InvalidDataException("The checkpoint has too many chunks.");
        var manifestId = Guid.NewGuid();
        session.Save(manifestId, manifest);
        await session.SynchronizeAsync(createProject: false, cancellationToken);
        return new(manifestId, Convert.ToHexStringLower(SHA256.HashData(manifest)));
    }

    public static async Task DownloadAsync(CollaborationSession session, CheckpointReference reference, Stream destination,
        long maxTotalBytes, CancellationToken cancellationToken)
    {
        var manifestBytes = ReadImmutableItem(session, reference.ManifestId, ManifestLimit);
        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(manifestBytes)), reference.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The project checkpoint manifest changed.");
        var manifest = JsonSerializer.Deserialize<Manifest>(manifestBytes) ?? throw new InvalidDataException("Invalid checkpoint manifest.");
        ValidateManifest(manifest, maxTotalBytes);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long length = 0;
        foreach (var id in manifest.Chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = ReadImmutableItem(session, id, ChunkSize);
            if (chunk.Length != Math.Min(ChunkSize, manifest.Length - length)) throw new InvalidDataException("Incomplete project checkpoint chunk.");
            await destination.WriteAsync(chunk, cancellationToken);
            hash.AppendData(chunk);
            length += chunk.Length;
        }
        if (!string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The downloaded project checkpoint failed its content hash check.");
    }

    private static byte[] ReadImmutableItem(CollaborationSession session, Guid id, int maxLength)
    {
        var values = session.ReadItem(id);
        if (values.Count != 1 || values[0].Length > maxLength) throw new InvalidDataException("Missing or conflicting project checkpoint content.");
        return values[0];
    }

    private static void ValidateManifest(Manifest manifest, long maxTotalBytes)
    {
        if (manifest.Version != 1 || manifest.Length <= 0 || manifest.Length > maxTotalBytes || manifest.Chunks is null ||
            manifest.Sha256 is null || manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid checkpoint version, size or hash.");
        if (manifest.Chunks.Count != (manifest.Length - 1) / ChunkSize + 1 || manifest.Chunks.Any(id => id == Guid.Empty) ||
            manifest.Chunks.Distinct().Count() != manifest.Chunks.Count)
            throw new InvalidDataException("Invalid project checkpoint chunks.");
    }
}
