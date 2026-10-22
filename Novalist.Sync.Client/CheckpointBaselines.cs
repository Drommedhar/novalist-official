using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Novalist.Sync.Client;

public sealed record CheckpointBaseline(string Path, long Length, string Sha256, long ResultLength, string ResultSha256);

internal static class CheckpointBaselines
{
    private const int HeaderLength = 17;
    private const int MaxBaselineBytes = 64 * 1024 * 1024;

    internal static string EntryName(string path) => "baseline/" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(path)));

    internal static void Validate(IReadOnlyList<CheckpointFile> files, IReadOnlyList<CheckpointBaseline> baselines, long quota)
    {
        var paths = files.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var baseline in baselines)
        {
            if (baseline is null || !paths.Contains(baseline.Path) || !seen.Add(baseline.Path) ||
                baseline.Length < HeaderLength || baseline.Length > MaxBaselineBytes + HeaderLength ||
                baseline.Length > quota ||
                baseline.ResultLength < 0 || baseline.ResultLength > MaxBaselineBytes || !IsHash(baseline.Sha256) || !IsHash(baseline.ResultSha256))
                throw new InvalidDataException("Invalid canonical project merge state.");
            quota -= baseline.Length;
        }
    }

    private static bool IsHash(string hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    internal static async Task<IReadOnlyList<CheckpointBaseline>> WriteAsync(ZipArchive archive, string root,
        IReadOnlyList<CheckpointFile> files, IReadOnlyDictionary<string, byte[]>? baselines, CancellationToken ct)
    {
        var descriptions = new List<CheckpointBaseline>();
        if (baselines is null) return descriptions;
        var sources = files.ToDictionary(file => file.Path, StringComparer.Ordinal);
        foreach (var (path, original) in baselines)
        {
            if (!sources.TryGetValue(path, out var source) || original is null || original.Length > MaxBaselineBytes)
                throw new InvalidDataException("A canonical project file is missing or too large.");
            var current = await SharedProjectFiles.FilterContentAsync(path, await File.ReadAllBytesAsync(SharedProjectPath.Resolve(root, path), ct), ct);
            var canonical = await SharedProjectFiles.FilterContentAsync(path, original, ct);
            if (!string.Equals(Hash(current), source.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The project changed while its canonical merge state was captured.");
            if (current.AsSpan().SequenceEqual(canonical)) continue;
            var patch = CreatePatch(current, canonical);
            descriptions.Add(new(path, patch.Length, Hash(patch), canonical.Length, Hash(canonical)));
            await using var entry = archive.CreateEntry(EntryName(path), CompressionLevel.Fastest).Open();
            await entry.WriteAsync(patch, ct);
        }
        Validate(files, descriptions, long.MaxValue);
        return descriptions;
    }

    private static byte[] CreatePatch(byte[] current, byte[] canonical)
    {
        var prefix = 0;
        while (prefix < Math.Min(current.Length, canonical.Length) && current[prefix] == canonical[prefix]) prefix++;
        var suffix = 0;
        while (suffix < Math.Min(current.Length, canonical.Length) - prefix && current[^(suffix + 1)] == canonical[^(suffix + 1)]) suffix++;
        var patch = new byte[HeaderLength + canonical.Length - prefix - suffix];
        patch[0] = 1;
        BinaryPrimitives.WriteInt64BigEndian(patch.AsSpan(1), prefix);
        BinaryPrimitives.WriteInt64BigEndian(patch.AsSpan(9), suffix);
        canonical.AsSpan(prefix, canonical.Length - prefix - suffix).CopyTo(patch.AsSpan(HeaderLength));
        return patch;
    }

    internal static async Task<IReadOnlyDictionary<string, byte[]>?> ReadAsync(ZipArchive archive, string root,
        IReadOnlyList<CheckpointBaseline> descriptions, CancellationToken ct)
    {
        if (descriptions.Count == 0) return null;
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var description in descriptions)
        {
            var entry = archive.GetEntry(EntryName(description.Path)) ?? throw new InvalidDataException("Missing canonical project merge state.");
            if (entry.Length != description.Length) throw new InvalidDataException("Canonical project merge state size mismatch.");
            var patch = new byte[checked((int)entry.Length)];
            await using (var stream = entry.Open()) await stream.ReadExactlyAsync(patch, ct);
            if (!string.Equals(Hash(patch), description.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Canonical project merge state hash mismatch.");
            var current = await File.ReadAllBytesAsync(SharedProjectPath.Resolve(root, description.Path), ct);
            var canonical = ApplyPatch(current, patch, description.ResultLength);
            if (!string.Equals(Hash(canonical), description.ResultSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Canonical project content hash mismatch.");
            result.Add(description.Path, canonical);
        }
        return result;
    }

    private static byte[] ApplyPatch(byte[] current, byte[] patch, long length)
    {
        var prefix = BinaryPrimitives.ReadInt64BigEndian(patch.AsSpan(1));
        var suffix = BinaryPrimitives.ReadInt64BigEndian(patch.AsSpan(9));
        if (patch[0] != 1 || prefix < 0 || suffix < 0 || prefix > current.Length || suffix > current.Length - prefix ||
            prefix + suffix + patch.Length - HeaderLength != length)
            throw new InvalidDataException("Invalid canonical project merge patch.");
        var result = new byte[checked((int)length)];
        current.AsSpan(0, (int)prefix).CopyTo(result);
        patch.AsSpan(HeaderLength).CopyTo(result.AsSpan((int)prefix));
        current.AsSpan(current.Length - (int)suffix).CopyTo(result.AsSpan(result.Length - (int)suffix));
        return result;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
