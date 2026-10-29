namespace Novalist.Mobile.Services;

internal sealed class CachedDocumentTree(IDocumentTree source, TimeProvider clock) : IDocumentTree
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);
    private const int MaximumFolders = 256;
    private readonly Dictionary<string, FolderListing> _folders = new();
    private readonly object _gate = new();
    public string RootId { get; } = source.RootId;

    public void EnsureAccess() => source.EnsureAccess();

    public IReadOnlyList<DocumentTreeEntry> GetChildren(string parentId)
    {
        FolderListing listing;
        lock (_gate)
        {
            if (!_folders.TryGetValue(parentId, out var cached) ||
                (cached.Entries.IsValueCreated && clock.GetUtcNow() - cached.ReadAt >= Lifetime))
            {
                if (_folders.Count >= MaximumFolders) _folders.Remove(_folders.MinBy(pair => pair.Value.ReadAt).Key);
                _folders[parentId] = cached = new FolderListing(() => source.GetChildren(parentId), clock);
            }
            listing = cached;
        }
        try { return listing.Entries.Value; }
        catch
        {
            lock (_gate)
                if (_folders.GetValueOrDefault(parentId) == listing) _folders.Remove(parentId);
            throw;
        }
    }

    private sealed class FolderListing
    {
        public DateTimeOffset ReadAt { get; private set; }
        public Lazy<IReadOnlyList<DocumentTreeEntry>> Entries { get; }
        public FolderListing(Func<IReadOnlyList<DocumentTreeEntry>> read, TimeProvider clock)
        {
            ReadAt = clock.GetUtcNow();
            Entries = new Lazy<IReadOnlyList<DocumentTreeEntry>>(() =>
            {
                var entries = read();
                ReadAt = clock.GetUtcNow();
                return entries;
            });
        }
    }

    public void Invalidate()
    {
        lock (_gate) _folders.Clear();
    }

    public byte[] Read(string id) => source.Read(id);

    public DocumentTreeEntry Create(string parentId, string name, bool directory)
    {
        try { return source.Create(parentId, name, directory); }
        finally { Invalidate(); }
    }

    public void Write(string id, byte[] bytes)
    {
        try { source.Write(id, bytes); }
        finally { Invalidate(); }
    }

    public DocumentTreeEntry Rename(string id, string name)
    {
        try { return source.Rename(id, name); }
        finally { Invalidate(); }
    }

    public void Delete(string id)
    {
        try { source.Delete(id); }
        finally { Invalidate(); }
    }
}
