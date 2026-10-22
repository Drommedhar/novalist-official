using System.IO.Compression;
using System.Security.Cryptography;

namespace Novalist.Sync.Client;

public sealed record SharedDocumentState(Guid ItemId, string Path, byte[] State)
{
    public string? SourceSha256 { get; init; }
    public SharedTextField? TextField { get; init; }
}
public sealed record SharedTextField(string Key, string? SectionId = null, string? Chapter = null, string? Scene = null, string? ResearchItemId = null, string? StateOverrideId = null);
public sealed record CheckpointDocument(Guid ItemId, string Path, long Length, string Sha256)
{
    public SharedTextField? TextField { get; init; }
}

internal static class CheckpointDocuments
{
    private const int MaxStateBytes = 64 * 1024 * 1024;

    internal static void Validate(IReadOnlyList<CheckpointFile> files, IReadOnlyList<CheckpointDocument> documents, long quota)
    {
        var paths = files.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        var identities = new HashSet<Guid>();
        var bindings = new HashSet<(string, SharedTextField?)>();
        foreach (var document in documents)
        {
            if (document is null || document.ItemId == Guid.Empty || !identities.Add(document.ItemId) ||
                !paths.Contains(document.Path) || !bindings.Add((document.Path, document.TextField)) ||
                document.Length is <= 0 or > MaxStateBytes || document.Length > quota ||
                document.Sha256 is null || document.Sha256.Length != 64 || !document.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Invalid shared document checkpoint.");
            quota -= document.Length;
            SharedProseFields.Validate(document.TextField);
        }
    }

    internal static async Task<IReadOnlyList<CheckpointDocument>> WriteAsync(ZipArchive archive,
        IReadOnlyList<CheckpointFile> files, IReadOnlyList<SharedDocumentState> documents, CancellationToken ct)
    {
        if (documents.Any(document => document is null || document.State is null))
            throw new InvalidDataException("Missing shared document state.");
        var descriptions = documents.Select(document => new CheckpointDocument(document.ItemId, document.Path,
            document.State.Length, Convert.ToHexStringLower(SHA256.HashData(document.State))) { TextField = document.TextField }).ToArray();
        Validate(files, descriptions, long.MaxValue);
        var sources = files.ToDictionary(file => file.Path, StringComparer.Ordinal);
        foreach (var document in documents)
            if (document.SourceSha256 is { } expected &&
                !string.Equals(sources[document.Path].Sha256, expected, StringComparison.OrdinalIgnoreCase))
                throw new IOException("A scene changed after its shared state was captured. Try sharing again.");
        foreach (var document in documents)
        {
            await using var entry = archive.CreateEntry(EntryName(document.ItemId), CompressionLevel.Fastest).Open();
            await entry.WriteAsync(document.State, ct);
        }
        return descriptions;
    }

    internal static async Task<IReadOnlyList<SharedDocumentState>> ReadAsync(ZipArchive archive,
        IReadOnlyList<CheckpointDocument> documents, CancellationToken ct)
    {
        var states = new List<SharedDocumentState>();
        foreach (var document in documents)
        {
            var entry = archive.GetEntry(EntryName(document.ItemId)) ?? throw new InvalidDataException("Missing shared document state.");
            if (entry.Length != document.Length) throw new InvalidDataException("Shared document state size mismatch.");
            await using var stream = entry.Open();
            var state = new byte[checked((int)document.Length)];
            await stream.ReadExactlyAsync(state, ct);
            if (await stream.ReadAsync(new byte[1], ct) != 0 ||
                !string.Equals(Convert.ToHexStringLower(SHA256.HashData(state)), document.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Shared document state hash mismatch.");
            states.Add(new(document.ItemId, document.Path, state) { TextField = document.TextField });
        }
        return states;
    }

    private static string EntryName(Guid id) => $"state/{id:N}";
}
