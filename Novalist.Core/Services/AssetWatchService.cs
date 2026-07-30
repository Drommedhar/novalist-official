using System.Diagnostics.CodeAnalysis;

namespace Novalist.Core.Services;

/// <summary>
/// Native watchers on the user's Themes, Locales and Analysis folders, driving
/// an <see cref="AssetWatchCoordinator"/>: raw OS events in, debounced reload
/// out.
///
/// All the decisions - which files matter, which folder they belong to,
/// coalescing a burst, swallowing a failed reload - live in the coordinator and
/// are unit-tested. This class is only the untestable interop: constructing the
/// watchers and a debounce timer. Excluded from coverage for that reason,
/// exactly as <see cref="DraftWatchService"/> is.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Native FileSystemWatcher + timer interop; logic is in AssetWatchCoordinator.")]
public sealed class AssetWatchService : IDisposable
{
    private readonly AssetWatchCoordinator _coordinator;
    private readonly System.Threading.Timer _timer;
    private readonly TimeSpan _debounce;
    private readonly List<FileSystemWatcher> _watchers = [];

    public AssetWatchService(
        IReadOnlyDictionary<UserAssetKind, string> folders,
        Func<IReadOnlyCollection<UserAssetKind>, Task> reload,
        TimeSpan? debounce = null)
    {
        _debounce = debounce ?? TimeSpan.FromMilliseconds(400);
        _timer = new System.Threading.Timer(_ => _ = _coordinator!.FlushAsync());
        _coordinator = new AssetWatchCoordinator(
            reload, () => _timer.Change(_debounce, Timeout.InfiniteTimeSpan));

        foreach (var (kind, folder) in folders)
        {
            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
                };
                watcher.Created += (_, e) => _coordinator.NotifyChange(kind, e.FullPath);
                watcher.Changed += (_, e) => _coordinator.NotifyChange(kind, e.FullPath);
                watcher.Deleted += (_, e) => _coordinator.NotifyChange(kind, e.FullPath);
                watcher.Renamed += (_, e) =>
                {
                    _coordinator.NotifyChange(kind, e.OldFullPath);
                    _coordinator.NotifyChange(kind, e.FullPath);
                };
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch
            {
                // A folder that does not exist yet, or a filesystem that cannot
                // be watched. Falling back to load-time-only is the documented
                // behaviour; it is not worth failing a launch over.
            }
        }
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();
        _timer.Dispose();
    }
}
