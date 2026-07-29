using System.IO.Compression;

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

                var target = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName));
                var root = Path.GetFullPath(destinationDirectory);

                // Refuse entries that would escape the destination (zip slip).
                if (!target.StartsWith(root, StringComparison.Ordinal))
                    continue;

                var dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                entry.ExtractToFile(target, overwrite: true);
                restored++;
            }
        }

        return Task.FromResult(restored);
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
