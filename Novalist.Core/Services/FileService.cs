using System.Collections.Concurrent;

namespace Novalist.Core.Services;

public class FileService : IFileService
{
    /// <summary>
    /// One gate per file, so two operations on the same path queue instead of
    /// colliding.
    ///
    /// Novalist saves on a timer while the writer keeps working, so a settings
    /// or scene save can land while the same file is being read or written by
    /// another path through the app. On Windows the second one does not wait:
    /// it fails outright with "used by another process", and the write is lost.
    /// A project has a bounded number of files, so a gate each is cheap.
    /// </summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tries a few times before giving up. The gate settles anything Novalist
    /// itself is doing; a backup tool or a virus scanner holding the file for a
    /// moment is outside it, and retrying is the only answer to that.
    /// </summary>
    private const int Attempts = 5;
    private const int RetryDelayMs = 40;

    private static SemaphoreSlim GateFor(string path)
        => Gates.GetOrAdd(Path.GetFullPath(path), _ => new SemaphoreSlim(1, 1));

    private static async Task<T> WithFileAsync<T>(string path, Func<Task<T>> work)
    {
        var gate = GateFor(path);
        await gate.WaitAsync();
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await work();
                }
                catch (IOException) when (attempt < Attempts)
                {
                    await Task.Delay(RetryDelayMs * attempt);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<string> ReadTextAsync(string path)
        => WithFileAsync(path, () => File.ReadAllTextAsync(path));

    public Task WriteTextAsync(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        return WithFileAsync(path, async () =>
        {
            await File.WriteAllTextAsync(path, content);
            return true;
        });
    }

    public Task<bool> ExistsAsync(string path)
    {
        return Task.FromResult(File.Exists(path));
    }

    public Task<bool> DirectoryExistsAsync(string path)
    {
        return Task.FromResult(Directory.Exists(path));
    }

    public Task CreateDirectoryAsync(string path)
    {
        Directory.CreateDirectory(path);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetFilesAsync(string directory, string pattern = "*", bool recursive = false)
    {
        if (!Directory.Exists(directory))
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(directory, pattern, option);
        return Task.FromResult<IReadOnlyList<string>>(files);
    }

    public Task<IReadOnlyList<string>> GetDirectoriesAsync(string directory)
    {
        if (!Directory.Exists(directory))
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        var dirs = Directory.GetDirectories(directory);
        return Task.FromResult<IReadOnlyList<string>>(dirs);
    }

    public Task DeleteFileAsync(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public Task DeleteDirectoryAsync(string path, bool recursive = true)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive);
        return Task.CompletedTask;
    }

    public Task MoveFileAsync(string oldPath, string newPath)
    {
        var dir = Path.GetDirectoryName(newPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.Move(oldPath, newPath);
        return Task.CompletedTask;
    }

    public Task<long> GetFileSizeAsync(string path)
        => Task.FromResult(new FileInfo(path).Length);

    public Task<DateTime> GetLastWriteTimeUtcAsync(string path)
        => Task.FromResult(File.GetLastWriteTimeUtc(path));

    public string CombinePath(params string[] parts) => Path.Combine(parts);
    public string GetFileName(string path) => Path.GetFileName(path);
    public string GetFileNameWithoutExtension(string path) => Path.GetFileNameWithoutExtension(path);
    public string GetDirectoryName(string path) => Path.GetDirectoryName(path) ?? string.Empty;
}
