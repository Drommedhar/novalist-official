using System.Diagnostics.CodeAnalysis;

namespace Novalist.Core.Services;

/// <summary>
/// Wraps a native <see cref="FileSystemWatcher"/> on the active draft folder and drives a
/// <see cref="DraftWatchCoordinator"/>: raw OS events in, debounced reconcile out. All the
/// decision logic (filtering, self-write suppression, coalescing, error-swallowing) lives in
/// the coordinator and is unit-tested; this class is only the un-testable interop glue —
/// constructing the watcher, a debounce <see cref="System.Threading.Timer"/>, and pausing
/// around the app's own writes. Excluded from coverage for that reason.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Native FileSystemWatcher + timer interop; logic is in DraftWatchCoordinator.")]
public sealed class DraftWatchService : IDisposable
{
    private readonly DraftWatchCoordinator _coordinator;
    private readonly System.Threading.Timer _timer;
    private readonly TimeSpan _debounce;
    private readonly FileSystemWatcher? _watcher;

    /// <param name="draftRoot">Folder to watch (recursively).</param>
    /// <param name="reconcile">Reconcile pass; the caller marshals it to the UI thread.</param>
    /// <param name="debounce">Quiet window to coalesce event bursts. Defaults to 500 ms.</param>
    public DraftWatchService(string draftRoot, Func<Task> reconcile, TimeSpan? debounce = null)
    {
        _debounce = debounce ?? TimeSpan.FromMilliseconds(500);
        _timer = new System.Threading.Timer(FlushPending);
        _coordinator = new DraftWatchCoordinator(reconcile, () => _timer.Change(_debounce, Timeout.InfiniteTimeSpan));

        try
        {
            _watcher = new FileSystemWatcher(draftRoot)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            };
            _watcher.Created += OnChanged;
            _watcher.Changed += OnChanged;
            _watcher.Deleted += OnChanged;
            _watcher.Renamed += OnRenamed;
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
            // Some network / cloud filesystems throw here. Fall back silently to load-time-only
            // reconcile — never crash the session over a watcher we couldn't start.
            _watcher = null;
        }
    }

    /// <summary>Registers a path the app is about to write so its event is ignored once.</summary>
    public void Suppress(string path) => _coordinator.Suppress(path);

    private void OnChanged(object sender, FileSystemEventArgs e) => _coordinator.NotifyChange(e.FullPath);

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        _coordinator.NotifyChange(e.OldFullPath);
        _coordinator.NotifyChange(e.FullPath);
    }

    private void FlushPending(object? state) => _ = _coordinator.FlushAsync();

    public void Dispose()
    {
        _watcher?.Dispose();
        _timer.Dispose();
    }
}
