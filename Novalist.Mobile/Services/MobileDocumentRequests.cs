using System.Collections.Concurrent;
using System.Text.Json;

namespace Novalist.Mobile.Services;

public sealed class MobileDocumentRequests(Func<int, object, CancellationToken, Task> send) : IDisposable
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> pending = new();
    private readonly CancellationTokenSource lifetime = new();
    private int nextId;

    public async Task<JsonElement> RequestAsync(object parameters, CancellationToken ct)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        operation.CancelAfter(TimeSpan.FromMinutes(2));
        var id = Interlocked.Increment(ref nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        try
        {
            await send(id, parameters, operation.Token);
            return await completion.Task.WaitAsync(operation.Token);
        }
        finally { pending.TryRemove(id, out _); }
    }

    public void Complete(int id, bool success, JsonElement result)
    {
        if (!pending.TryRemove(id, out var completion)) return;
        if (success) completion.TrySetResult(result.Clone());
        else completion.TrySetException(new InvalidOperationException("The mobile shared document operation failed."));
    }

    public void Dispose()
    {
        lifetime.Cancel();
        lifetime.Dispose();
    }
}
