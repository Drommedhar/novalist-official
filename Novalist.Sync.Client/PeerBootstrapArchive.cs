using System.IO.Compression;
using System.Security.Cryptography;

namespace Novalist.Sync.Client;

internal sealed class PeerBootstrapArchive : IDisposable
{
    internal const int ChunkBytes = 128 * 1024;
    private readonly FileStream source;
    private readonly ZipArchive archive;
    private readonly HashSet<string> entries;
    private Stream? current;
    private string? currentEntry;
    private long position;
    public PeerBootstrapDescription Description { get; }

    private PeerBootstrapArchive(FileStream source, ZipArchive archive, CheckpointManifest manifest, PeerBootstrapDescription description)
    {
        this.source = source;
        this.archive = archive;
        Description = description;
        entries = manifest.Files.Select(file => "files/" + file.Path)
            .Concat((manifest.Documents ?? []).Select(document => $"state/{document.ItemId:N}"))
            .Append("manifest.json").ToHashSet(StringComparer.Ordinal);
    }

    internal static async Task<PeerBootstrapArchive> OpenAsync(string path, long sequence, CancellationToken ct)
    {
        var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        ZipArchive? archive = null;
        try
        {
            archive = new(source, ZipArchiveMode.Read, leaveOpen: true);
            var manifest = await ProjectCheckpoint.ReadManifestAsync(archive, long.MaxValue, ct);
            var entry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("Missing project manifest.");
            await using var content = entry.Open();
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(content, ct));
            return new(source, archive, manifest, new(entry.Length, hash, sequence));
        }
        catch { archive?.Dispose(); source.Dispose(); throw; }
    }

    internal async Task<byte[]> ReadAsync(string name, long offset, CancellationToken ct)
    {
        if (!entries.Contains(name) || offset < 0) throw new InvalidDataException("Invalid bootstrap content request.");
        var entry = archive.GetEntry(name) ?? throw new InvalidDataException("Missing bootstrap content.");
        if (offset >= entry.Length) throw new InvalidDataException("Invalid bootstrap content position.");
        if (currentEntry != name || position != offset)
        {
            if (offset != 0) throw new InvalidDataException("Bootstrap file chunks must be read in order.");
            current?.Dispose();
            current = entry.Open();
            currentEntry = name;
            position = 0;
        }
        var bytes = new byte[(int)Math.Min(ChunkBytes, entry.Length - offset)];
        await current!.ReadExactlyAsync(bytes, ct);
        position += bytes.Length;
        return bytes;
    }

    public void Dispose()
    {
        current?.Dispose();
        archive.Dispose();
        source.Dispose();
    }
}
