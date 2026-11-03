using System.Text;
using Novalist.Core.Services;

namespace Novalist.Mobile.Services;

public sealed class DocumentTreeFileService : IFileService, IFileMetadataCache, IBatchTextReader, IFileReadCache
{
    private readonly DocumentTreeMounts mounts;
    private readonly IFileService local;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AsyncLocal<bool> _insideOperation = new();
    private readonly AsyncLocal<ReadScope?> _readScope = new();
    private readonly DocumentReadCache _readCache;

    public DocumentTreeFileService(DocumentTreeMounts mounts, IFileService local, TimeProvider? clock = null)
    {
        this.mounts = mounts;
        this.local = local;
        _readCache = new DocumentReadCache(clock ?? TimeProvider.System);
    }

    public IDisposable UseCachedReads() => SetReadMode(true);
    public IDisposable UseFreshReads() => SetReadMode(false);

    private IDisposable SetReadMode(bool cached)
    {
        var scope = new ReadScope(_readScope, _readScope.Value, cached, mounts);
        _readScope.Value = scope;
        return scope;
    }

    private sealed class ReadScope(AsyncLocal<ReadScope?> mode, ReadScope? previous, bool cached,
        DocumentTreeMounts mounts) : IDisposable
    {
        private bool _disposed;
        private bool _refreshed;
        private readonly object _gate = new();
        public bool Cached => cached;
        public void EnsureFreshMetadata()
        {
            if (cached) return;
            lock (_gate)
            {
                if (_refreshed) return;
                mounts.InvalidateAll();
                _refreshed = true;
            }
        }
        public void Dispose()
        {
            if (_disposed) return;
            mode.Value = previous;
            _disposed = true;
        }
    }

    private async Task<T> Run<T>(Func<Task<T>> action)
    {
        var cancellationToken = DocumentTreeReadCancellation.Current;
        cancellationToken.ThrowIfCancellationRequested();
        if (_insideOperation.Value) return await action().ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                _insideOperation.Value = true;
                try { return await action().ConfigureAwait(false); }
                finally { _insideOperation.Value = false; }
            }).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private Task<T> Route<T>(string path, Func<Task<T>> localAction, Func<DocumentTreePath, T> externalAction, bool mutation = false)
        => Run(() => ResolveForOperation(path, mutation) is { } external ? Task.FromResult(externalAction(external)) : localAction());

    private DocumentTreePath? ResolveForOperation(string path, bool mutation = false)
    {
        var external = mounts.Resolve(path);
        if (external != null) _readScope.Value?.EnsureFreshMetadata();
        if (mutation)
        {
            _readCache.Invalidate(path);
            if (external?.Tree is CachedDocumentTree cached) cached.Invalidate();
        }
        using (DocumentTreeReadCancellation.Enter(CancellationToken.None))
            external?.Transactions.Recover(external.Tree);
        DocumentTreeReadCancellation.Current.ThrowIfCancellationRequested();
        return external;
    }

    private Task Route(string path, Func<Task> localAction, Action<DocumentTreePath> externalAction)
        => Route(path, async () => { await localAction().ConfigureAwait(false); return true; }, external => { externalAction(external); return true; }, mutation: true);

    public async Task<string> ReadTextAsync(string path)
    {
        using var stream = new MemoryStream(await ReadBytesAsync(path).ConfigureAwait(false));
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }
    public Task WriteTextAsync(string path, string content) => WriteBytesAsync(path, new UTF8Encoding(false, true).GetBytes(content));
    public void InvalidateReadCache(string path)
    {
        mounts.Invalidate(path);
        _readCache.Invalidate(path);
    }
    public Task RefreshMetadataAsync(string path) => Run(() =>
    {
        InvalidateReadCache(path);
        return Task.FromResult(true);
    });
    public Task<IReadOnlyDictionary<string, string>> ReadExistingTextsAsync(IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default) => ReadBatchAsync(paths, existingOnly: true, cancellationToken);
    public Task<IReadOnlyDictionary<string, string>> ReadTextsAsync(IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default) => ReadBatchAsync(paths, existingOnly: false, cancellationToken);
    private async Task<IReadOnlyDictionary<string, string>> ReadBatchAsync(IReadOnlyList<string> paths,
        bool existingOnly, CancellationToken cancellationToken)
    {
        using var reads = DocumentTreeReadCancellation.Enter(cancellationToken);
        return await Run<IReadOnlyDictionary<string, string>>(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var path in paths) ResolveForOperation(path);
            var result = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.Ordinal);
            await Parallel.ForEachAsync(paths, new ParallelOptions
            {
                MaxDegreeOfParallelism = 4,
                CancellationToken = cancellationToken
            }, async (path, token) =>
            {
                using var batchRead = DocumentTreeReadCancellation.Enter(token);
                token.ThrowIfCancellationRequested();
                if (!existingOnly || await ExistsAsync(path).ConfigureAwait(false))
                    result[path] = await ReadTextAsync(path).ConfigureAwait(false);
            }).ConfigureAwait(false);
            return result;
        }).ConfigureAwait(false);
    }
    public Task<byte[]> ReadBytesAsync(string path) => Route(path, () => local.ReadBytesAsync(path), external =>
    {
        var entry = RequireFile(external);
        DocumentTreeReadCancellation.Current.ThrowIfCancellationRequested();
        if (_readScope.Value?.Cached == true && _readCache.TryRead(path, entry, out var cached)) return cached;
        var bytes = external.Tree.Read(entry.Id);
        DocumentTreeReadCancellation.Current.ThrowIfCancellationRequested();
        _readCache.Store(path, entry, bytes);
        return bytes;
    });
    public Task WriteBytesAsync(string path, byte[] bytes) => Route(path, () => local.WriteBytesAsync(path, bytes), external => DocumentTreeOperations.Write(external, bytes));
    public Task<bool> ExistsAsync(string path) => Route(path, () => local.ExistsAsync(path), external => DocumentTreeOperations.Find(external) is { IsDirectory: false });
    public Task<bool> DirectoryExistsAsync(string path) => Route(path, () => local.DirectoryExistsAsync(path), external => DocumentTreeOperations.Find(external) is { IsDirectory: true });
    public Task CreateDirectoryAsync(string path) => Route(path, () => local.CreateDirectoryAsync(path), external =>
    {
        if (DocumentTreeOperations.Find(external, true)?.IsDirectory != true) throw new IOException("A file already uses this folder name.");
    });
    public Task<long> GetFileSizeAsync(string path) => Route(path, () => local.GetFileSizeAsync(path), external => RequireFile(external).Size);
    public Task<DateTime> GetLastWriteTimeUtcAsync(string path) => Route(path, () => local.GetLastWriteTimeUtcAsync(path), external => RequireFile(external).LastWriteTimeUtc);

    public Task<IReadOnlyList<string>> GetFilesAsync(string directory, string pattern = "*", bool recursive = false)
        => Route<IReadOnlyList<string>>(directory, () => local.GetFilesAsync(directory, pattern, recursive), external =>
        {
            var result = new List<string>();
            if (DocumentTreeOperations.Find(external) is { IsDirectory: true } parent)
                DocumentTreeOperations.ListFiles(external.Tree, parent.Id, directory, pattern, recursive, result);
            return result;
        });

    public Task<IReadOnlyList<string>> GetDirectoriesAsync(string directory)
        => Route<IReadOnlyList<string>>(directory, () => local.GetDirectoriesAsync(directory), external =>
            DocumentTreeOperations.Find(external) is { IsDirectory: true } parent
                ? DocumentTreeOperations.Children(external.Tree, parent.Id).Where(entry => entry.IsDirectory).Select(entry => Path.Combine(directory, entry.Name)).ToArray()
                : []);

    public Task DeleteFileAsync(string path) => Route(path, () => local.DeleteFileAsync(path), external =>
    {
        if (DocumentTreeOperations.Find(external) is not { } entry) return;
        if (entry.IsDirectory) throw new IOException("The document is a folder.");
        external.Tree.Delete(entry.Id);
    });

    public Task DeleteDirectoryAsync(string path, bool recursive = true) => Route(path, () => local.DeleteDirectoryAsync(path, recursive), external =>
    {
        if (external.Segments.Length == 0) throw new IOException("The selected root folder cannot be deleted.");
        if (DocumentTreeOperations.Find(external) is not { } entry) return;
        if (!entry.IsDirectory || (!recursive && DocumentTreeOperations.Children(external.Tree, entry.Id).Count != 0))
            throw new IOException("The document is not an empty folder.");
        external.Tree.Delete(entry.Id);
    });

    public Task MoveFileAsync(string oldPath, string newPath) => Run(async () => { await MoveAsync(oldPath, newPath, false).ConfigureAwait(false); return true; });
    public Task MoveDirectoryAsync(string oldPath, string newPath) => Run(async () => { await MoveAsync(oldPath, newPath, true).ConfigureAwait(false); return true; });

    private async Task MoveAsync(string oldPath, string newPath, bool directory)
    {
        if (!mounts.IsExternalPath(oldPath) && !mounts.IsExternalPath(newPath))
        {
            if (directory) await local.MoveDirectoryAsync(oldPath, newPath).ConfigureAwait(false);
            else await local.MoveFileAsync(oldPath, newPath).ConfigureAwait(false);
            return;
        }
        mounts.Invalidate(oldPath);
        mounts.Invalidate(newPath);
        _readCache.Invalidate(oldPath);
        _readCache.Invalidate(newPath);
        if (directory && ResolveForOperation(oldPath) is { Segments.Length: 0 })
            throw new IOException("The selected root folder cannot be moved.");
        if (await ExistsAsync(newPath).ConfigureAwait(false) || await DirectoryExistsAsync(newPath).ConfigureAwait(false))
            throw new IOException("The move destination already exists.");
        if (TryRenameWithinParent(oldPath, newPath, directory)) return;
        if (directory)
        {
            var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(oldPath));
            if (Path.GetFullPath(newPath).StartsWith(source + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new IOException("A folder cannot be moved inside itself.");
            if (!await DirectoryExistsAsync(oldPath).ConfigureAwait(false)) throw new DirectoryNotFoundException();
            try { await CopyDirectoryAsync(oldPath, newPath).ConfigureAwait(false); }
            catch (Exception failure)
            {
                try { await DeleteDirectoryAsync(newPath).ConfigureAwait(false); }
                catch (Exception cleanup) { throw new AggregateException("Moving failed and the incomplete destination could not be removed.", failure, cleanup); }
                throw;
            }
            await DeleteDirectoryAsync(oldPath).ConfigureAwait(false);
        }
        else
        {
            await WriteBytesAsync(newPath, await ReadBytesAsync(oldPath).ConfigureAwait(false)).ConfigureAwait(false);
            await DeleteFileAsync(oldPath).ConfigureAwait(false);
        }
    }

    private async Task CopyDirectoryAsync(string source, string target)
    {
        await CreateDirectoryAsync(target).ConfigureAwait(false);
        foreach (var file in await GetFilesAsync(source).ConfigureAwait(false))
            await WriteBytesAsync(Path.Combine(target, Path.GetFileName(file)), await ReadBytesAsync(file).ConfigureAwait(false)).ConfigureAwait(false);
        foreach (var folder in await GetDirectoriesAsync(source).ConfigureAwait(false))
            await CopyDirectoryAsync(folder, Path.Combine(target, Path.GetFileName(folder))).ConfigureAwait(false);
    }

    private bool TryRenameWithinParent(string oldPath, string newPath, bool directory)
    {
        var source = ResolveForOperation(oldPath);
        var target = ResolveForOperation(newPath);
        if (source == null || target == null || source.Segments.Length == 0 || target.Segments.Length == 0
            || source.Tree.RootId != target.Tree.RootId
            || !source.Segments[..^1].SequenceEqual(target.Segments[..^1], StringComparer.Ordinal)) return false;
        var entry = DocumentTreeOperations.Find(source) ?? throw new FileNotFoundException("The move source is unavailable.");
        if (entry.IsDirectory != directory) throw new IOException("The move source has the wrong document type.");
        source.Tree.Rename(entry.Id, target.Segments[^1]).RequireName(target.Segments[^1]);
        return true;
    }

    private static DocumentTreeEntry RequireFile(DocumentTreePath path)
        => DocumentTreeOperations.Find(path) is { IsDirectory: false } file ? file : throw new FileNotFoundException("The selected folder no longer contains this file.");

    public string CombinePath(params string[] parts) => Path.Combine(parts);
    public string GetFileName(string path) => Path.GetFileName(path);
    public string GetFileNameWithoutExtension(string path) => Path.GetFileNameWithoutExtension(path);
    public string GetDirectoryName(string path) => Path.GetDirectoryName(path) ?? "";
}
