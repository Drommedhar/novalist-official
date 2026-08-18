namespace Novalist.Core.Services;

/// <summary>
/// Abstraction over file system operations for testability.
/// </summary>
public interface IFileService
{
    Task<string> ReadTextAsync(string path);
    Task WriteTextAsync(string path, string content);

    /// <summary>Reads a file whose contents are not text. Audio is the first
    /// such thing Novalist writes into a project, and encoding it as base64 in a
    /// text file would double its size on disk and defeat Git's own handling of
    /// binaries.</summary>
    Task<byte[]> ReadBytesAsync(string path);

    /// <summary>Writes a file whose contents are not text.</summary>
    Task WriteBytesAsync(string path, byte[] bytes);
    Task<bool> ExistsAsync(string path);
    Task<bool> DirectoryExistsAsync(string path);
    Task CreateDirectoryAsync(string path);
    Task<IReadOnlyList<string>> GetFilesAsync(string directory, string pattern = "*", bool recursive = false);
    Task<IReadOnlyList<string>> GetDirectoriesAsync(string directory);
    Task DeleteFileAsync(string path);
    Task DeleteDirectoryAsync(string path, bool recursive = true);
    Task MoveFileAsync(string oldPath, string newPath);

    /// <summary>Size of the file in bytes. Used by the draft-index re-hash fast-path.</summary>
    Task<long> GetFileSizeAsync(string path);

    /// <summary>Last write time of the file in UTC. Used by the draft-index re-hash fast-path.</summary>
    Task<DateTime> GetLastWriteTimeUtcAsync(string path);

    string CombinePath(params string[] parts);
    string GetFileName(string path);
    string GetFileNameWithoutExtension(string path);
    string GetDirectoryName(string path);
}
