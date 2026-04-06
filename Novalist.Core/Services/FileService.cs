namespace Novalist.Core.Services;

public class FileService : IFileService
{
    public async Task<string> ReadTextAsync(string path)
    {
        return await File.ReadAllTextAsync(path);
    }

    public async Task WriteTextAsync(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(path, content);
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

    public string CombinePath(params string[] parts) => Path.Combine(parts);
    public string GetFileName(string path) => Path.GetFileName(path);
    public string GetFileNameWithoutExtension(string path) => Path.GetFileNameWithoutExtension(path);
    public string GetDirectoryName(string path) => Path.GetDirectoryName(path) ?? string.Empty;
}
