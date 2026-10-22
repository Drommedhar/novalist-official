using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Novalist.Sync.Client;

public sealed record CheckpointFile(string Path, long Length, string Sha256);
public sealed record CheckpointManifest(int Version, IReadOnlyList<CheckpointFile> Files, IReadOnlyList<CheckpointDocument>? Documents = null,
    IReadOnlyList<SharedFieldBinding>? Fields = null, IReadOnlyList<CheckpointBaseline>? Baselines = null);

public static partial class ProjectCheckpoint
{
    public static async Task<IReadOnlyDictionary<string, byte[]>> ReadFilesAsync(Stream source, long maxTotalBytes, CancellationToken ct)
    {
        if (maxTotalBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxTotalBytes));
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var manifest = await ReadManifestAsync(archive, maxTotalBytes, ct);
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var file in manifest.Files)
        {
            var entry = archive.GetEntry("files/" + file.Path) ?? throw new InvalidDataException("Missing checkpoint content.");
            if (entry.Length != file.Length) throw new InvalidDataException("Checkpoint content size mismatch.");
            await using var input = entry.Open();
            using var output = new MemoryStream();
            var hash = await CopyAndHashAsync(input, output, file.Length, ct);
            if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Checkpoint content hash mismatch.");
            result.Add(file.Path, output.ToArray());
        }
        return result;
    }

    public static Task CreateAsync(string root, Stream destination, Func<IReadOnlyList<string>> selectPaths, CancellationToken cancellationToken) =>
        CreateAsync(root, destination, selectPaths, new SharedProjectState([], []), cancellationToken);

    public static Task CreateAsync(string root, Stream destination, Func<IReadOnlyList<string>> selectPaths,
        IReadOnlyList<SharedDocumentState> documents, CancellationToken cancellationToken) =>
        CreateAsync(root, destination, selectPaths, new SharedProjectState(documents, []), cancellationToken);

    public static async Task CreateAsync(string root, Stream destination, Func<IReadOnlyList<string>> selectPaths,
        SharedProjectState state, CancellationToken cancellationToken, IProgress<CollaborationTransferProgress>? progress = null)
    {
        var paths = selectPaths().Order(StringComparer.Ordinal).ToArray();
        ValidatePaths(paths);
        var sources = new List<(string Path, FileStream Stream)>();
        var meter = new BootstrapProgress(progress);
        meter.Report("scanning", total: paths.Length);
        try
        {
            foreach (var path in paths)
                sources.Add((path, new FileStream(SharedProjectPath.Resolve(root, path), FileMode.Open, FileAccess.Read, FileShare.Read)));
            using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
            var files = new List<CheckpointFile>();
            var totalBytes = sources.Sum(source => source.Stream.Length);
            long completedBytes = 0;
            foreach (var (path, source) in sources)
            {
                using var projected = await SharedProjectMetadata.ProjectAsync(path, source, cancellationToken);
                var content = projected ?? (Stream)source;
                totalBytes += content.Length - source.Length;
                meter.Report("comparing", files.Count, paths.Length, completedBytes, totalBytes);
                await using var entry = archive.CreateEntry("files/" + path, CompressionLevel.Fastest).Open();
                var hash = await CopyAndHashAsync(content, entry, content.Length, cancellationToken, meter.AddBytes);
                files.Add(new(path, content.Length, hash));
                completedBytes += content.Length;
                meter.CompleteFile();
            }
            if (!paths.SequenceEqual(selectPaths().Order(StringComparer.Ordinal)))
                throw new IOException("The project changed while its checkpoint was being created. Try again.");
            var states = await CheckpointDocuments.WriteAsync(archive, files, state.Documents, cancellationToken);
            var baselines = await CheckpointBaselines.WriteAsync(archive, root, files, state.FileBaselines, cancellationToken);
            ValidateFields(files, states, state.Fields);
            await SharedCodexFields.ReadAsync(root, state.Fields, cancellationToken);
            await using var manifest = archive.CreateEntry("manifest.json", CompressionLevel.Fastest).Open();
            await JsonSerializer.SerializeAsync(manifest, new CheckpointManifest(baselines.Count != 0 ? 5 : states.Any(document => document.TextField is not null) ? 4 : 3,
                files, states, state.Fields, baselines), cancellationToken: cancellationToken);
            meter.Phase("complete");
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
            var baselines = await CheckpointBaselines.ReadAsync(archive, staging, manifest.Baselines ?? [], cancellationToken);
            await CheckpointStorageReferences.ValidateAsync(staging, manifest.Files, cancellationToken);
            await SharedCodexFields.ReadAsync(staging, manifest.Fields ?? [], cancellationToken);
            await SharedProjectMetadata.FilterInstalledAsync(staging, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(staging, destination);
            return new(states, manifest.Fields ?? []) { FileBaselines = baselines };
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    internal static async Task<CheckpointManifest> ReadManifestAsync(ZipArchive archive, long maxTotalBytes, CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("Missing checkpoint manifest.");
        if (entry.Length > 8 * 1024 * 1024) throw new InvalidDataException("The checkpoint manifest is too large.");
        await using var stream = entry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<CheckpointManifest>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("Invalid checkpoint manifest.");
        ValidateManifest(manifest, maxTotalBytes);
        if (archive.Entries.Count != manifest.Files.Count + (manifest.Documents?.Count ?? 0) + (manifest.Baselines?.Count ?? 0) + 1 ||
            archive.Entries.Select(item => item.FullName).Distinct(StringComparer.Ordinal).Count() != archive.Entries.Count)
            throw new InvalidDataException("Duplicate or incomplete checkpoint entries.");
        return manifest;
    }

    internal static void ValidateManifest(CheckpointManifest manifest, long maxTotalBytes)
    {
        var documents = manifest.Documents ?? [];
        if (manifest.Version is not (1 or 2 or 3 or 4 or 5) || (manifest.Version == 1 && documents.Count != 0) ||
            (manifest.Version < 5 && manifest.Baselines is { Count: > 0 }) ||
            (manifest.Version < 4 && documents.Any(document => document.TextField is not null)) ||
            (manifest.Version < 3 && manifest.Fields is { Count: > 0 }) ||
            manifest.Files is null)
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
        CheckpointBaselines.Validate(manifest.Files, manifest.Baselines ?? [], maxTotalBytes - total - documents.Sum(document => document.Length));
        ValidateFields(manifest.Files, documents, manifest.Fields ?? []);
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

    private static async Task<string> CopyAndHashAsync(Stream input, Stream output, long expectedLength, CancellationToken cancellationToken,
        Action<int>? copied = null)
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
            copied?.Invoke(count);
        }
        if (length != expectedLength) throw new InvalidDataException("Incomplete checkpoint content.");
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
