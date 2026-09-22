using System.Text;

namespace Novalist.Core.Services;

/// <summary>
/// File IO for hosts whose documents belong to a file provider. In particular,
/// a security-scoped iOS bookmark grants permission, but does not download a
/// cloud-only file or notify iCloud about a completed write. Coordination does.
/// </summary>
public sealed class CoordinatedFileService(IFileAccessCoordinator coordinator) : IFileService
{
    public Task<string> ReadTextAsync(string path) => coordinator.ReadAsync(path, File.ReadAllText);
    public Task<byte[]> ReadBytesAsync(string path) => coordinator.ReadAsync(path, File.ReadAllBytes);

    public Task WriteTextAsync(string path, string content) => WriteBytesAsync(path, Encoding.UTF8.GetBytes(content));

    public async Task WriteBytesAsync(string path, byte[] bytes)
    {
        await EnsureParentAsync(path).ConfigureAwait(false);
        await coordinator.WriteAsync(path, false, current =>
        {
            // Keep the previous document intact until the new one is complete.
            // The rename stays inside the coordinated write to the destination.
            var temporary = current + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, current, overwrite: true);
            }
            finally { File.Delete(temporary); }
            return true;
        }).ConfigureAwait(false);
    }

    public Task<bool> ExistsAsync(string path) => ExistsAsync(path, directory: false);
    public Task<bool> DirectoryExistsAsync(string path) => ExistsAsync(path, directory: true);

    private async Task<bool> ExistsAsync(string path, bool directory)
    {
        try
        {
            // Do not check File.Exists before coordination: a cloud-only file
            // may not have local contents yet. Do not turn access errors into
            // "missing" either, since a caller might then create an empty file.
            return await coordinator.ReadAsync(path,
                current => File.GetAttributes(current).HasFlag(FileAttributes.Directory) == directory)
                .ConfigureAwait(false);
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    public async Task CreateDirectoryAsync(string path)
    {
        if (await DirectoryExistsAsync(path).ConfigureAwait(false)) return;
        await EnsureParentAsync(path).ConfigureAwait(false);
        await coordinator.WriteAsync(path, false, current => Directory.CreateDirectory(current))
            .ConfigureAwait(false);
    }

    private Task EnsureParentAsync(string path)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(path));
        return parent == null ? Task.CompletedTask : CreateDirectoryAsync(parent);
    }

    public async Task<IReadOnlyList<string>> GetFilesAsync(string directory, string pattern = "*", bool recursive = false)
    {
        if (!await DirectoryExistsAsync(directory).ConfigureAwait(false)) return [];
        var files = (await coordinator.ReadAsync(directory,
            current => Directory.GetFiles(current, pattern)).ConfigureAwait(false)).ToList();
        if (recursive)
            foreach (var child in await GetDirectoriesAsync(directory).ConfigureAwait(false))
                files.AddRange(await GetFilesAsync(child, pattern, true).ConfigureAwait(false));
        return files;
    }

    public async Task<IReadOnlyList<string>> GetDirectoriesAsync(string directory)
    {
        if (!await DirectoryExistsAsync(directory).ConfigureAwait(false)) return [];
        return await coordinator.ReadAsync(directory, Directory.GetDirectories).ConfigureAwait(false);
    }

    public async Task DeleteFileAsync(string path)
    {
        if (!await ExistsAsync(path).ConfigureAwait(false)) return;
        await coordinator.WriteAsync(path, true, current => { File.Delete(current); return true; })
            .ConfigureAwait(false);
    }

    public async Task DeleteDirectoryAsync(string path, bool recursive = true)
    {
        if (!await DirectoryExistsAsync(path).ConfigureAwait(false)) return;
        await coordinator.WriteAsync(path, true, current => { Directory.Delete(current, recursive); return true; })
            .ConfigureAwait(false);
    }

    public async Task MoveFileAsync(string oldPath, string newPath)
    {
        await EnsureParentAsync(newPath).ConfigureAwait(false);
        await coordinator.MoveAsync(oldPath, newPath, (source, destination) =>
        {
            File.Move(source, destination);
            return true;
        }).ConfigureAwait(false);
    }

    public async Task MoveDirectoryAsync(string oldPath, string newPath)
    {
        await EnsureParentAsync(newPath).ConfigureAwait(false);
        await coordinator.MoveAsync(oldPath, newPath, (source, destination) =>
        {
            Directory.Move(source, destination);
            return true;
        }).ConfigureAwait(false);
    }

    public Task<long> GetFileSizeAsync(string path) => coordinator.ReadAsync(path, current => new FileInfo(current).Length);
    public Task<DateTime> GetLastWriteTimeUtcAsync(string path) => coordinator.ReadAsync(path, File.GetLastWriteTimeUtc);
    public string CombinePath(params string[] parts) => Path.Combine(parts);
    public string GetFileName(string path) => Path.GetFileName(path);
    public string GetFileNameWithoutExtension(string path) => Path.GetFileNameWithoutExtension(path);
    public string GetDirectoryName(string path) => Path.GetDirectoryName(path) ?? string.Empty;
}
