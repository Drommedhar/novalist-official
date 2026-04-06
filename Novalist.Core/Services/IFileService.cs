namespace Novalist.Core.Services;

/// <summary>
/// Abstraction over file system operations for testability.
/// </summary>
public interface IFileService
{
    Task<string> ReadTextAsync(string path);
    Task WriteTextAsync(string path, string content);
    Task<bool> ExistsAsync(string path);
    Task<bool> DirectoryExistsAsync(string path);
    Task CreateDirectoryAsync(string path);
    Task<IReadOnlyList<string>> GetFilesAsync(string directory, string pattern = "*", bool recursive = false);
    Task<IReadOnlyList<string>> GetDirectoriesAsync(string directory);
    Task DeleteFileAsync(string path);
    Task DeleteDirectoryAsync(string path, bool recursive = true);
    Task MoveFileAsync(string oldPath, string newPath);
    string CombinePath(params string[] parts);
    string GetFileName(string path);
    string GetFileNameWithoutExtension(string path);
    string GetDirectoryName(string path);
}
