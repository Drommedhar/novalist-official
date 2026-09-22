using System.Runtime.ExceptionServices;
using Foundation;
using Novalist.Core.Services;
using UIKit;

namespace Novalist.Mobile.Services;

/// <summary>
/// Security scope grants permission; NSFileCoordinator downloads provider data
/// before reading and tells iCloud when a write has finished. Never await inside
/// a native accessor: returning from it releases the provider's coordination.
/// </summary>
public sealed class IosFileAccessCoordinator : IFileAccessCoordinator
{
    public Task<T> ReadAsync<T>(string path, Func<string, T> access) => Run(coordinator =>
    {
        using var url = NSUrl.FromFilename(path);
        var result = new AccessResult<T>();
        coordinator.CoordinateRead(url, 0, out var error,
            current => result.Invoke(() => access(CurrentPath(current))));
        return result.Complete(error);
    });

    public Task<T> WriteAsync<T>(string path, bool deleting, Func<string, T> access) => Run(coordinator =>
    {
        using var url = NSUrl.FromFilename(path);
        var result = new AccessResult<T>();
        coordinator.CoordinateWrite(url, deleting ? NSFileCoordinatorWritingOptions.ForDeleting : 0,
            out var error, current => result.Invoke(() => access(CurrentPath(current))));
        return result.Complete(error);
    });

    public Task<T> MoveAsync<T>(string source, string destination, Func<string, string, T> access) => Run(coordinator =>
    {
        using var from = NSUrl.FromFilename(source);
        using var to = NSUrl.FromFilename(destination);
        var result = new AccessResult<T>();
        coordinator.CoordinateWriteWrite(from, NSFileCoordinatorWritingOptions.ForMoving, to, 0,
            out var error, (currentFrom, currentTo) => result.Invoke(() =>
            {
                coordinator.WillMove(currentFrom, currentTo);
                var value = access(CurrentPath(currentFrom), CurrentPath(currentTo));
                coordinator.ItemMoved(currentFrom, currentTo);
                return value;
            }));
        return result.Complete(error);
    });

    private static string CurrentPath(NSUrl url) => url.Path
        ?? throw new IOException("The file provider returned no local path.");

    private static Task<T> Run<T>(Func<NSFileCoordinator, T> action) => Task.Run(() =>
    {
        // Coordination can wait for a download or another process. Keep it off
        // the UI thread and prevent suspension while holding a provider lock.
        // UIKit permits Begin/EndBackgroundTask from a non-main thread.
        using var coordinator = new NSFileCoordinator();
        var app = UIApplication.SharedApplication;
        var task = app.BeginBackgroundTask("Novalist file access", coordinator.Cancel);
        try { return action(coordinator); }
        finally
        {
            if (task != UIApplication.BackgroundTaskInvalid) app.EndBackgroundTask(task);
        }
    });

    private sealed class AccessResult<T>
    {
        private T _value = default!;
        private bool _called;
        private ExceptionDispatchInfo? _failure;

        public void Invoke(Func<T> action)
        {
            _called = true;
            // A managed exception must not escape an Objective-C callback.
            try { _value = action(); }
            catch (Exception ex) { _failure = ExceptionDispatchInfo.Capture(ex); }
        }

        public T Complete(NSError? error)
        {
            _failure?.Throw();
            if (error != null)
            {
                // Only genuine absence is "not found". Offline, permission and
                // cancelled coordination errors must not look like empty data.
                if (error.Domain == "NSCocoaErrorDomain" &&
                    (error.Code == (long)NSCocoaError.FileNoSuchFile ||
                     error.Code == (long)NSCocoaError.FileReadNoSuchFile))
                    throw new FileNotFoundException("The cloud file was not found.");
                throw new IOException($"File provider access failed ({error.Domain}, {error.Code}).");
            }
            if (!_called) throw new IOException("The file provider did not grant access.");
            return _value;
        }
    }
}
