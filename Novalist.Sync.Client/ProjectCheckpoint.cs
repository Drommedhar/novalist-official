using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Novalist.Sync.Client;

public sealed record CheckpointFile(string Path, long Length, string Sha256);
public sealed record CheckpointManifest(int Version, IReadOnlyList<CheckpointFile> Files, IReadOnlyList<CheckpointDocument>? Documents = null,
    IReadOnlyList<SharedFieldBinding>? Fields = null);

public static class ProjectCheckpoint
{
    public static Task CreateAsync(string root, Stream destination, Func<IReadOnlyList<string>> selectPaths, CancellationToken cancellationToken) =>
        CreateAsync(root, destination, selectPaths, new SharedProjectState([], []), cancellationToken);

    public static Task CreateAsync(string root, Stream destination, Func<IReadOnlyList<string>> selectPaths,
        IReadOnlyList<SharedDocumentState> documents, CancellationToken cancellationToken) =>
        CreateAsync(root, destination, selectPaths, new SharedProjectState(documents, []), cancellationToken);

    public static async Task CreateAsync(string root, Stream destination, Func<IReadOnlyList<string>> selectPaths,
        SharedProjectState state, CancellationToken cancellationToken)
    {
        var paths = selectPaths().Order(StringComparer.Ordinal).ToArray();
        ValidatePaths(paths);
        var sources = new List<(string Path, FileStream Stream)>();
        try
        {
            foreach (var path in paths)
                sources.Add((path, new FileStream(SharedProjectPath.Resolve(root, path), FileMode.Open, FileAccess.Read, FileShare.Read)));
            using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
            var files = new List<CheckpointFile>();
            foreach (var (path, source) in sources)
            {
                using var projected = await SharedProjectMetadata.ProjectAsync(path, source, cancellationToken);
                var content = projected ?? (Stream)source;
                await using var entry = archive.CreateEntry("files/" + path, CompressionLevel.Fastest).Open();
                var hash = await CopyAndHashAsync(content, entry, content.Length, cancellationToken);
                files.Add(new(path, content.Length, hash));
            }
            if (!paths.SequenceEqual(selectPaths().Order(StringComparer.Ordinal)))
                throw new IOException("The project changed while its checkpoint was being created. Try again.");
            var states = await CheckpointDocuments.WriteAsync(archive, files, state.Documents, cancellationToken);
            ValidateFields(files, states, state.Fields);
            await SharedCodexFields.ReadAsync(root, state.Fields, cancellationToken);
            await using var manifest = archive.CreateEntry("manifest.json", CompressionLevel.Fastest).Open();
            await JsonSerializer.SerializeAsync(manifest, new CheckpointManifest(3, files, states, state.Fields), cancellationToken: cancellationToken);
        }
        finally
        {
            foreach (var (_, stream) in sources) await stream.DisposeAsync();
        }
    }

    public static async Task<SharedProjectState> InstallAsync(Stream source, string destination, long maxTotalBytes, CancellationToken cancellationToken)
    {
        if (maxTotalBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxTotalBytes));
        destination = Path.GetFullPath(destination);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("The project destination already exists.");
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var manifest = await ReadManifestAsync(archive, maxTotalBytes, cancellationToken);
        var states = await CheckpointDocuments.ReadAsync(archive, manifest.Documents ?? [], cancellationToken);
        var parent = Path.GetDirectoryName(destination) ?? throw new IOException("Choose a project folder.");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".novalist-join-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var file in manifest.Files)
                await InstallFileAsync(archive, staging, file, cancellationToken);
            await CheckpointStorageReferences.ValidateAsync(staging, manifest.Files, cancellationToken);
            await SharedCodexFields.ReadAsync(staging, manifest.Fields ?? [], cancellationToken);
            await SharedProjectMetadata.FilterInstalledAsync(staging, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(staging, destination);
            return new(states, manifest.Fields ?? []);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static async Task<CheckpointManifest> ReadManifestAsync(ZipArchive archive, long maxTotalBytes, CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("Missing checkpoint manifest.");
        if (entry.Length > 8 * 1024 * 1024) throw new InvalidDataException("The checkpoint manifest is too large.");
        await using var stream = entry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<CheckpointManifest>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("Invalid checkpoint manifest.");
        var documents = manifest.Documents ?? [];
        if (manifest.Version is not (1 or 2 or 3) || (manifest.Version == 1 && documents.Count != 0) ||
            (manifest.Version < 3 && manifest.Fields is { Count: > 0 }) ||
            manifest.Files is null || archive.Entries.Count != manifest.Files.Count + documents.Count + 1)
            throw new InvalidDataException("Unsupported or incomplete checkpoint.");
        if (manifest.Files.Any(file => file is null)) throw new InvalidDataException("Missing checkpoint file description.");
        ValidatePaths(manifest.Files.Select(file => file.Path));
        long total = 0;
        foreach (var file in manifest.Files)
        {
            if (file.Length < 0 || file.Length > maxTotalBytes - total || file.Sha256 is null || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Invalid checkpoint size or content hash.");
            total += file.Length;
        }
        CheckpointDocuments.Validate(manifest.Files, documents, maxTotalBytes - total);
        ValidateFields(manifest.Files, documents, manifest.Fields ?? []);
        if (archive.Entries.Select(item => item.FullName).Distinct(StringComparer.Ordinal).Count() != archive.Entries.Count)
            throw new InvalidDataException("Duplicate checkpoint entries.");
        return manifest;
    }

    private static void ValidateFields(IReadOnlyList<CheckpointFile> files, IReadOnlyList<CheckpointDocument> documents, IReadOnlyList<SharedFieldBinding> fields)
    {
        SharedCodexFields.ValidateBindings(fields);
        var sources = files.ToDictionary(file => file.Path, StringComparer.Ordinal);
        var identities = documents.Select(document => document.ItemId).ToHashSet();
        foreach (var field in fields)
            if (!identities.Add(field.ItemId) || !sources.TryGetValue(field.Path, out var file) ||
                !string.Equals(file.Sha256, field.SourceSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A shared field does not match its checkpoint file.");
    }

    private static async Task InstallFileAsync(ZipArchive archive, string root, CheckpointFile file, CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry("files/" + file.Path) ?? throw new InvalidDataException("Missing checkpoint content.");
        if (entry.Length != file.Length) throw new InvalidDataException("Checkpoint content size mismatch.");
        var destination = SharedProjectPath.Resolve(root, file.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? throw new InvalidDataException("Invalid checkpoint directory."));
        await using var input = entry.Open();
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var hash = await CopyAndHashAsync(input, output, file.Length, cancellationToken);
        if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Checkpoint content hash mismatch.");
        await output.FlushAsync(cancellationToken);
        output.Flush(flushToDisk: true);
    }

    private static void ValidatePaths(IEnumerable<string> paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            SharedProjectPath.Validate(path);
            if (!seen.Add(path.Normalize())) throw new InvalidDataException("Duplicate shared project paths.");
        }
        if (!seen.Contains(".novalist/project.json")) throw new InvalidDataException("The checkpoint has no project metadata.");
    }

    private static async Task<string> CopyAndHashAsync(Stream input, Stream output, long expectedLength, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long length = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (count > expectedLength - length) throw new InvalidDataException("Checkpoint content exceeds its declared size.");
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            hash.AppendData(buffer, 0, count);
            length += count;
        }
        if (length != expectedLength) throw new InvalidDataException("Incomplete checkpoint content.");
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
