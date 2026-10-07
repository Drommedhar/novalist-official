namespace Novalist.Mobile.Services;

/// <summary>Owns the temporary grants of one renderer page, including late picker replies.</summary>
public sealed class TemporaryAccessRegistry(Action<Exception> releaseFailed) : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Action> _releases = new(StringComparer.Ordinal);
    private bool _disposed;

    public bool IsDisposed { get { lock (_gate) return _disposed; } }

    public bool TryRetain(string path, Action release)
    {
        Action? previous = null;
        bool retained;
        lock (_gate)
        {
            retained = !_disposed;
            if (retained)
            {
                _releases.Remove(path, out previous);
                _releases[path] = release;
            }
        }
        if (previous != null) ReleaseGrant(previous);
        if (!retained) ReleaseGrant(release);
        return retained;
    }

    public void Release(string path)
    {
        Action? release;
        lock (_gate)
        {
            if (!_releases.Remove(path, out release)) return;
        }
        ReleaseGrant(release);
    }

    public void Dispose()
    {
        Action[] releases;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            releases = _releases.Values.ToArray();
            _releases.Clear();
        }
        foreach (var release in releases) ReleaseGrant(release);
    }

    private void ReleaseGrant(Action release)
    {
        try { release(); }
        catch (Exception error) { releaseFailed(error); }
    }
}
