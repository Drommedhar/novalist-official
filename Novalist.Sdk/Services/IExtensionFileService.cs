using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;

namespace Novalist.Sdk.Services;

/// <summary>
/// Read-only file system operations exposed to extensions.
/// </summary>
public interface IExtensionFileService
{
    Task<string> ReadTextAsync(string path);
    Task WriteTextAsync(string path, string content);
    Task<bool> ExistsAsync(string path);
    Task<bool> DirectoryExistsAsync(string path);
    Task CreateDirectoryAsync(string path);
    Task<IReadOnlyList<string>> GetFilesAsync(string directory, string pattern = "*", bool recursive = false);
    Task<IReadOnlyList<string>> GetDirectoriesAsync(string directory);
    string CombinePath(params string[] parts);
    string GetFileName(string path);
    string GetFileNameWithoutExtension(string path);
    string GetDirectoryName(string path);
}
