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

    public Task<string> ReadTextAsync(string path)
        => Files.TryGetValue(path, out var v) ? Task.FromResult(v) : throw new FileNotFoundException(path);

    public Task WriteTextAsync(string path, string content)
    {
        Files[path] = content;
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string path) => Task.FromResult(Files.ContainsKey(path));
    public Task<bool> DirectoryExistsAsync(string path) => Task.FromResult(Dirs.Contains(path));

    public Task CreateDirectoryAsync(string path)
    {
        Dirs.Add(path);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetFilesAsync(string directory, string pattern = "*", bool recursive = false)
        => Task.FromResult<IReadOnlyList<string>>(Files.Keys.Where(k => k.StartsWith(directory, StringComparison.OrdinalIgnoreCase)).ToList());

    public Task<IReadOnlyList<string>> GetDirectoriesAsync(string directory)
        => Task.FromResult<IReadOnlyList<string>>(Dirs.Where(d => d.StartsWith(directory, StringComparison.OrdinalIgnoreCase)).ToList());

    public Task DeleteFileAsync(string path)
    {
        Files.Remove(path);
        return Task.CompletedTask;
    }

    public Task DeleteDirectoryAsync(string path, bool recursive = true)
    {
        Dirs.Remove(path);
        return Task.CompletedTask;
    }

    public Task MoveFileAsync(string oldPath, string newPath)
    {
        if (Files.Remove(oldPath, out var v)) Files[newPath] = v;
        return Task.CompletedTask;
    }

    public string CombinePath(params string[] parts) => Path.Combine(parts);
    public string GetFileName(string path) => Path.GetFileName(path);
    public string GetFileNameWithoutExtension(string path) => Path.GetFileNameWithoutExtension(path);
    public string GetDirectoryName(string path) => Path.GetDirectoryName(path) ?? string.Empty;
}
