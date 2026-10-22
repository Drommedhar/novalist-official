#if NOVALIST_COLLABORATION
using System.Text;
using Novalist.Core.Services;

namespace Novalist.Backend;

internal sealed class CollaborationFileService : IFileService
{
    private readonly IFileService inner;
    private readonly Workspace workspace;

    internal CollaborationFileService(IFileService inner, Workspace workspace)
    {
        this.inner = inner;
        this.workspace = workspace;
    }

    public Task<string> ReadTextAsync(string path) => inner.ReadTextAsync(path);
    public Task<byte[]> ReadBytesAsync(string path) => inner.ReadBytesAsync(path);
    public Task<bool> ExistsAsync(string path) => inner.ExistsAsync(path);
    public Task<bool> DirectoryExistsAsync(string path) => inner.DirectoryExistsAsync(path);
    public Task<IReadOnlyList<string>> GetFilesAsync(string directory, string pattern = "*", bool recursive = false) => inner.GetFilesAsync(directory, pattern, recursive);
    public Task<IReadOnlyList<string>> GetDirectoriesAsync(string directory) => inner.GetDirectoriesAsync(directory);
    public Task<long> GetFileSizeAsync(string path) => inner.GetFileSizeAsync(path);
    public Task<DateTime> GetLastWriteTimeUtcAsync(string path) => inner.GetLastWriteTimeUtcAsync(path);
    public string CombinePath(params string[] parts) => inner.CombinePath(parts);
    public string GetFileName(string path) => inner.GetFileName(path);
    public string GetFileNameWithoutExtension(string path) => inner.GetFileNameWithoutExtension(path);
    public string GetDirectoryName(string path) => inner.GetDirectoryName(path);

    public Task WriteTextAsync(string path, string content) => WriteBytesAsync(path, new UTF8Encoding(false, true).GetBytes(content));

    public async Task WriteBytesAsync(string path, byte[] bytes)
    {
        await workspace.ValidateSharedWriteAsync(path, bytes, structural: false);
        await inner.WriteBytesAsync(path, bytes);
        await workspace.RecordSharedWriteAsync(path, bytes);
    }

    public async Task CreateDirectoryAsync(string path)
    {
        if (await inner.DirectoryExistsAsync(path)) return;
        await workspace.ValidateSharedWriteAsync(path, null, structural: true);
        await inner.CreateDirectoryAsync(path);
    }

    public async Task DeleteFileAsync(string path)
    {
        await workspace.ValidateSharedWriteAsync(path, null, structural: true);
        await inner.DeleteFileAsync(path);
        await workspace.RecordSharedWriteAsync(path, null);
    }

    public async Task DeleteDirectoryAsync(string path, bool recursive = true)
    {
        await workspace.ValidateSharedWriteAsync(path, null, structural: true);
        var files = await inner.GetFilesAsync(path, recursive: true);
        foreach (var file in files) await workspace.ValidateSharedWriteAsync(file, null, structural: true);
        await inner.DeleteDirectoryAsync(path, recursive);
        foreach (var file in files) await workspace.RecordSharedWriteAsync(file, null);
    }

    public async Task MoveFileAsync(string oldPath, string newPath)
    {
        var bytes = await inner.ReadBytesAsync(oldPath);
        await workspace.ValidateSharedWriteAsync(oldPath, null, structural: true);
        await workspace.ValidateSharedWriteAsync(newPath, bytes, structural: true);
        await inner.MoveFileAsync(oldPath, newPath);
        await workspace.RecordSharedWriteAsync(oldPath, null);
        await workspace.RecordSharedWriteAsync(newPath, bytes);
    }

    public async Task MoveDirectoryAsync(string oldPath, string newPath)
    {
        await workspace.ValidateSharedWriteAsync(oldPath, null, structural: true);
        await workspace.ValidateSharedWriteAsync(newPath, null, structural: true);
        var moves = new List<(string Before, string After, byte[] Content)>();
        foreach (var file in await inner.GetFilesAsync(oldPath, recursive: true))
        {
            var destination = Path.Combine(newPath, Path.GetRelativePath(oldPath, file));
            var bytes = await inner.ReadBytesAsync(file);
            await workspace.ValidateSharedWriteAsync(file, null, structural: true);
            await workspace.ValidateSharedWriteAsync(destination, bytes, structural: true);
            moves.Add((file, destination, bytes));
        }
        await inner.MoveDirectoryAsync(oldPath, newPath);
        foreach (var move in moves)
        {
            await workspace.RecordSharedWriteAsync(move.Before, null);
            await workspace.RecordSharedWriteAsync(move.After, move.Content);
        }
    }
}
#endif
