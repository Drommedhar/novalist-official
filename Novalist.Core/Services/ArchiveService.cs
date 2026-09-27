using System.IO.Compression;
using System.Text.Json;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>
/// ZIP implementation over <see cref="System.IO.Compression"/>. Walks the tree
/// itself rather than using <c>ZipFile.CreateFromDirectory</c> so directories can
/// be excluded, which is what keeps a git-tracked project from archiving its own
/// history on every interval.
/// </summary>
public sealed class ArchiveService : IArchiveService
{
    public Task<int> CreateFromDirectoryAsync(
        string sourceDirectory,
        string destinationZipPath,
        IReadOnlyCollection<string> excludedDirectoryNames)
    {
        var excluded = new HashSet<string>(
            excludedDirectoryNames ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

        var parent = Path.GetDirectoryName(destinationZipPath);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        var written = 0;
        using (var zip = ZipFile.Open(destinationZipPath, ZipArchiveMode.Create))
        {
            foreach (var file in EnumerateFiles(sourceDirectory, sourceDirectory, excluded))
            {
                var relative = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
                zip.CreateEntryFromFile(file, relative, CompressionLevel.Optimal);
                written++;
            }
        }

        return Task.FromResult(written);
    }

    public Task<int> ExtractToDirectoryAsync(string zipPath, string destinationDirectory)
        => Task.FromResult(Extract(zipPath, destinationDirectory, strict: false));

    private static int Extract(string zipPath, string destinationDirectory, bool strict)
    {
        Directory.CreateDirectory(destinationDirectory);

        var restored = 0;
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            foreach (var entry in zip.Entries)
            {
                // Directory entries have an empty name; nothing to write.
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                var name = entry.FullName.Replace('\\', '/');
                var target = Path.GetFullPath(Path.Combine(destinationDirectory, name));
                var root = Path.GetFullPath(destinationDirectory);

                // Refuse entries that would escape the destination (zip slip).
                if (!target.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar,
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
                    name.Split('/').Any(p => p == ".." || p.Equals(".git", StringComparison.OrdinalIgnoreCase)) ||
                    name.Contains(':') || (entry.ExternalAttributes >> 16 & 0xf000) == 0xa000)
                {
                    if (strict) throw new InvalidDataException("The backup contains an unsafe file path.");
                    continue;
                }

                var dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                entry.ExtractToFile(target, overwrite: true);
                restored++;
            }
        }

        return restored;
    }

    public Task RestoreProjectAsync(string zipPath, string destinationDirectory, bool replaceExisting)
    {
        var destination = Path.GetFullPath(destinationDirectory);
        if (!replaceExisting && (Directory.Exists(destination) || File.Exists(destination)))
            throw new IOException("Choose a new project folder. The destination already exists.");

        var staging = Path.Combine(replaceExisting ? Path.GetTempPath() : Path.GetDirectoryName(destination)!,
            ".novalist-restore-" + Guid.NewGuid().ToString("N"));
        var existing = new List<string>();
        var directories = new List<string>();
        try
        {
            // Finish reading the entire ZIP before touching the destination. An
            // unavailable cloud file, truncated ZIP or unrelated ZIP cannot erase it.
            Extract(zipPath, staging, strict: true);
            var metadataPath = Path.Combine(staging, ".novalist", "project.json");
            if (!File.Exists(metadataPath))
                throw new InvalidDataException("This ZIP is not a Novalist project backup.");
            var metadata = JsonSerializer.Deserialize<ProjectMetadata>(File.ReadAllText(metadataPath));
            if (metadata == null || string.IsNullOrWhiteSpace(metadata.Name) || metadata.Books == null || metadata.Books.Count == 0)
                throw new InvalidDataException("The backup's project metadata is invalid.");

            if (!replaceExisting)
            {
                // Same-volume rename is exclusive: even a folder created while
                // the ZIP was being staged must never be merged or overwritten.
                Directory.Move(staging, destination);
                return Task.CompletedTask;
            }

            if (Directory.Exists(destination)) Scan(destination);
            var restored = Directory.GetFiles(staging, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(staging, file)).ToHashSet(
                    OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            Directory.CreateDirectory(destination);
            foreach (var relative in restored)
            {
                var target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(staging, relative), target, overwrite: replaceExisting);
            }
            // Reconciliation treats surviving scene files as new external edits.
            // Remove them only once every archived file has been written successfully.
            foreach (var file in existing)
                if (!restored.Contains(Path.GetRelativePath(destination, file))) File.Delete(file);
            foreach (var directory in directories.OrderByDescending(d => d.Length))
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
        return Task.CompletedTask;

        void Scan(string directory)
        {
            if (new DirectoryInfo(directory).LinkTarget != null)
                throw new IOException("Cannot restore over a linked project folder.");
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                if (string.Equals(Path.GetFileName(path), ".git", StringComparison.OrdinalIgnoreCase)) continue;
                var attributes = File.GetAttributes(path);
                if (((attributes & FileAttributes.Directory) != 0
                        ? new DirectoryInfo(path).LinkTarget : new FileInfo(path).LinkTarget) != null)
                    throw new IOException("Cannot restore over linked files or folders.");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    Scan(path);
                    directories.Add(path);
                }
                else existing.Add(path);
            }
        }
    }

    private static IEnumerable<string> EnumerateFiles(
        string root, string current, HashSet<string> excluded)
    {
        foreach (var file in Directory.EnumerateFiles(current))
            yield return file;

        foreach (var dir in Directory.EnumerateDirectories(current))
        {
            var name = Path.GetFileName(dir);
            if (excluded.Contains(name))
                continue;

            foreach (var file in EnumerateFiles(root, dir, excluded))
                yield return file;
        }
    }
}
