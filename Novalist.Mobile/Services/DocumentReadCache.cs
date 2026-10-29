namespace Novalist.Mobile.Services;

internal sealed class DocumentReadCache(TimeProvider clock)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private const long MaximumBytes = 32 * 1024 * 1024;
    private const long MaximumFileBytes = 1024 * 1024;
    private const int MaximumFiles = 2048;
    private sealed record CachedFile(DocumentTreeEntry Metadata, byte[] Bytes, LinkedListNode<string> Position, DateTimeOffset ReadAt);
    private readonly Dictionary<string, CachedFile> _files = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _recent = new();
    private readonly object _gate = new();
    private long _bytes;

    public bool TryRead(string path, DocumentTreeEntry metadata, out byte[] bytes)
    {
        lock (_gate)
        {
            if (_files.TryGetValue(path, out var cached) && cached.Metadata == metadata
                && clock.GetUtcNow() - cached.ReadAt < Lifetime)
            {
                _recent.Remove(cached.Position);
                _recent.AddLast(cached.Position);
                bytes = (byte[])cached.Bytes.Clone();
                return true;
            }
            bytes = [];
            return false;
        }
    }

    public void Store(string path, DocumentTreeEntry metadata, byte[] bytes)
    {
        lock (_gate)
        {
            Remove(path);
            if (metadata.LastWriteTimeUtc <= DateTime.UnixEpoch || bytes.LongLength != metadata.Size
                || bytes.LongLength > MaximumFileBytes) return;
            while ((_bytes + bytes.LongLength > MaximumBytes || _files.Count >= MaximumFiles)
                && _recent.First is { } oldest) Remove(oldest.Value);
            _files.Add(path, new CachedFile(metadata, (byte[])bytes.Clone(), _recent.AddLast(path), clock.GetUtcNow()));
            _bytes += bytes.LongLength;
        }
    }

    public void Invalidate(string path)
    {
        lock (_gate)
        {
            var prefix = Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar;
            foreach (var key in _files.Keys.Where(key => key == path || key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
                Remove(key);
        }
    }

    private void Remove(string path)
    {
        if (!_files.Remove(path, out var cached)) return;
        _bytes -= cached.Bytes.LongLength;
        _recent.Remove(cached.Position);
    }
}
