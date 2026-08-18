using Novalist.Core.Services;

namespace Novalist.Core.Tests.TestHelpers;

/// <summary>
/// In-memory <see cref="IFileService"/> for services that do read-modify-write
/// round trips. Avoids real disk while preserving the abstraction's semantics.
/// </summary>
public sealed class InMemoryFileService : IFileService
{
    public readonly Dictionary<string, string> Files = new(StringComparer.OrdinalIgnoreCase);
    public readonly HashSet<string> Dirs = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, DateTime> Mtimes = new(StringComparer.OrdinalIgnoreCase);

    public Task<string> ReadTextAsync(string path)
        => Files.TryGetValue(path, out var v) ? Task.FromResult(v) : throw new FileNotFoundException(path);

    public Task WriteTextAsync(string path, string content)
    {
        Files[path] = content;
        Mtimes[path] = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    /// <summary>Binary files live in their own map, so a test that writes audio
    /// and reads it back gets the bytes rather than a lossy round trip through a
    /// string.</summary>
    public Dictionary<string, byte[]> Binaries { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<byte[]> ReadBytesAsync(string path)
        => Binaries.TryGetValue(path, out var v)
            ? Task.FromResult(v)
            : throw new FileNotFoundException(path);

    public Task WriteBytesAsync(string path, byte[] bytes)
    {
        Binaries[path] = bytes;
        Files[path] = string.Empty;
        Mtimes[path] = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string path) => Task.FromResult(Files.ContainsKey(path));
    public Task<bool> DirectoryExistsAsync(string path)
        => Task.FromResult(Dirs.Any(d => Same(d, path)));

    /// <summary>
    /// Adds the directory and every ancestor, because Directory.CreateDirectory
    /// does - and a service that asks whether the parent exists would otherwise
    /// be told no on a tree it just created.
    ///
    /// Ancestors are derived with GetDirectoryName, which normalises separators;
    /// the path as given is added too, so a caller that built it with a forward
    /// slash still finds it.
    /// </summary>
    public Task CreateDirectoryAsync(string path)
    {
        Dirs.Add(path);
        for (var dir = GetDirectoryName(path); !string.IsNullOrEmpty(dir); dir = GetDirectoryName(dir))
        {
            if (!Dirs.Add(dir)) break;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetFilesAsync(string directory, string pattern = "*", bool recursive = false)
        => Task.FromResult<IReadOnlyList<string>>(Files.Keys.Where(k => k.StartsWith(directory, StringComparison.OrdinalIgnoreCase)).ToList());

    /// <summary>
    /// Immediate children only, and never the directory itself - which is what
    /// Directory.GetDirectories returns, and what a caller walking a folder of
    /// per-scene subfolders depends on.
    /// </summary>
    public Task<IReadOnlyList<string>> GetDirectoriesAsync(string directory)
        => Task.FromResult<IReadOnlyList<string>>(
            Dirs.Where(d => Same(GetDirectoryName(d), directory)).ToList());

    /// <summary>
    /// Two paths naming the same place. Windows accepts either separator, and a
    /// test that writes "/draft" while the code under test derives "\draft"
    /// through GetDirectoryName is describing one directory, not two.
    /// </summary>
    private static bool Same(string a, string b)
        => Canonical(a).Equals(Canonical(b), StringComparison.OrdinalIgnoreCase);

    private static string Canonical(string path)
        => path.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);

    public Task DeleteFileAsync(string path)
    {
        Files.Remove(path);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes the directory, and recursively everything under it - because
    /// Directory.Delete(recursive: true) does, and a caller that deletes a
    /// folder expects its files to be gone rather than merely unreachable.
    /// </summary>
    public Task DeleteDirectoryAsync(string path, bool recursive = true)
    {
        Dirs.Remove(path);
        if (!recursive) return Task.CompletedTask;

        var prefix = Canonical(path) + Path.DirectorySeparatorChar;
        foreach (var key in Files.Keys.Where(k => Canonical(k).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
            Files.Remove(key);
        foreach (var dir in Dirs.Where(d => Canonical(d).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
            Dirs.Remove(dir);
        return Task.CompletedTask;
    }

    public Task MoveFileAsync(string oldPath, string newPath)
    {
        if (Files.Remove(oldPath, out var v))
        {
            Files[newPath] = v;
            Mtimes.Remove(oldPath, out var t);
            Mtimes[newPath] = t == default ? DateTime.UtcNow : t;
        }
        return Task.CompletedTask;
    }

    public Task<long> GetFileSizeAsync(string path)
        => Files.TryGetValue(path, out var v)
            ? Task.FromResult((long)System.Text.Encoding.UTF8.GetByteCount(v))
            : throw new FileNotFoundException(path);

    public Task<DateTime> GetLastWriteTimeUtcAsync(string path)
        => Mtimes.TryGetValue(path, out var t) ? Task.FromResult(t) : throw new FileNotFoundException(path);

    public string CombinePath(params string[] parts) => Path.Combine(parts);
    public string GetFileName(string path) => Path.GetFileName(path);
    public string GetFileNameWithoutExtension(string path) => Path.GetFileNameWithoutExtension(path);
    public string GetDirectoryName(string path) => Path.GetDirectoryName(path) ?? string.Empty;
}
