#if NOVALIST_COLLABORATION
using Novalist.Sync.Client;

namespace Novalist.Backend;

internal sealed partial class PeerProjectWorkspace
{
    private Operation BeginOperation(CancellationToken ct)
    {
        lock (operationGate)
        {
            if (pending is not null) throw new InvalidOperationException("A collaboration connection is already being prepared.");
            pending = CancellationTokenSource.CreateLinkedTokenSource(ct, stopping.Token);
            return new(this, pending);
        }
    }

    internal void Cancel()
    {
        lock (operationGate) pending?.Cancel();
        current?.Host?.CancelBootstrap();
    }

    private sealed class Operation(PeerProjectWorkspace owner, CancellationTokenSource source) : IDisposable
    {
        internal CancellationToken Token => source.Token;
        public void Dispose()
        {
            lock (owner.operationGate)
            {
                owner.pending = null;
                source.Dispose();
            }
        }
    }

    private async Task SynchronizeBootstrapAsync(Runtime runtime, Func<Task> transfer, CancellationToken ct)
    {
        async Task CaptureAsync()
        {
            if (current != runtime) throw new InvalidOperationException("The host ended this collaboration session.");
            await workspace.FlushSharedProjectFilesAsync();
            await runtime.Session.Updates.CaptureSnapshotAsync(async (sequence, token) =>
            {
                var directory = Path.Combine(storage, "sessions", runtime.Id.ToString("N"), "bootstrap");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".zip");
                runtime.BootstrapArchives.Add(path);
                await using var archive = new FileStream(path,
                    FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
                await workspace.CaptureActiveSharedProjectAsync(archive, token);
                await archive.FlushAsync(token);
                archive.Flush(true);
                runtime.RequireHost().SetBootstrap(archive.Name, sequence);
                return true;
            }, ct);
            await transfer();
        }
        if (workspace.Coordinator is { } coordinator) await coordinator.RunAsync("collaboration/hostSync", CaptureAsync);
        else if (workspace.DispatchSharedCapture is { } dispatch) await dispatch(CaptureAsync, ct);
        else await CaptureAsync();
    }
}
#endif
