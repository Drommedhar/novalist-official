using System.Runtime.ExceptionServices;

namespace Novalist.Sync.Client;

internal sealed class PeerBootstrapSession : IAsyncDisposable
{
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task running = Task.CompletedTask;
    private Exception? failure;

    internal static async Task<PeerBootstrapSession> StartAsync(Func<Func<Task>, Task> synchronize, CancellationToken ct)
    {
        var session = new PeerBootstrapSession();
        session.running = session.RunAsync(synchronize, ct);
        try { await session.ready.Task; }
        catch { await session.running; throw; }
        return session;
    }

    private async Task RunAsync(Func<Func<Task>, Task> synchronize, CancellationToken ct)
    {
        try
        {
            await synchronize(async () =>
            {
                using var cancellation = ct.Register(() => released.TrySetResult());
                ct.ThrowIfCancellationRequested();
                ready.TrySetResult();
                await released.Task;
            });
        }
        catch (Exception error)
        {
            failure = error;
            ready.TrySetException(error);
        }
    }

    public async ValueTask DisposeAsync()
    {
        released.TrySetResult();
        await running;
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
