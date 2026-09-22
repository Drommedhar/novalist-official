using System.IO.Compression;

namespace Novalist.Mobile.Services;

/// <summary>
/// One private directory per export. Companion assets (for example Markdown
/// images) travel together in a zip, and simultaneous exports never collide.
/// Only files allocated by this instance may be shared or removed.
/// </summary>
public sealed class ExportFiles(string cacheDirectory)
{
    private readonly Dictionary<string, string> _pending = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public string Create(string suggestedName)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat("/\\:").ToHashSet();
        var name = new string(suggestedName.Select(c => char.IsControl(c) || invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        if (name.Length == 0 || name is "." or "..") name = "export";
        var directory = Path.Combine(cacheDirectory, "exports", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        lock (_gate) _pending.Add(path, directory);
        return path;
    }

    public string PrepareShare(string path)
    {
        lock (_gate)
        {
            if (!_pending.TryGetValue(path, out var directory))
                throw new InvalidOperationException("This file is not a pending export.");
            if (!File.Exists(path)) throw new FileNotFoundException("The export file was not created.");
            if (Directory.EnumerateFileSystemEntries(directory).Count() == 1) return path;

            // Outside the source directory, so the archive never includes itself.
            var archive = directory + ".zip";
            File.Delete(archive);
            ZipFile.CreateFromDirectory(directory, archive, CompressionLevel.Fastest, includeBaseDirectory: false);
            // Keep a readable name in the share sheet; the archive's parent is
            // unique, and this separate folder cannot become an exported asset.
            var sharedDirectory = directory + "-shared";
            Directory.CreateDirectory(sharedDirectory);
            var shared = Path.Combine(sharedDirectory, Path.GetFileNameWithoutExtension(path) + ".zip");
            File.Move(archive, shared, overwrite: true);
            return shared;
        }
    }

    public void Release(string path)
    {
        lock (_gate)
        {
            if (!_pending.TryGetValue(path, out var directory)) return;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            var sharedDirectory = directory + "-shared";
            if (Directory.Exists(sharedDirectory)) Directory.Delete(sharedDirectory, recursive: true);
            File.Delete(directory + ".zip");
            _pending.Remove(path);
        }
    }
}
