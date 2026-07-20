using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Novalist.Backend.Extensions;

/// <summary>
/// Backend-owned single-threaded UI pump. The headless backend has no Avalonia
/// dispatcher (Program.Main only runs the JSON-RPC stdio loop), yet extension
/// ViewModels derive from <c>ObservableObject</c> and expect their mutations to
/// happen on one consistent "UI" thread. This pump provides exactly that: a
/// dedicated long-lived thread that drains a queue of actions in order.
///
/// <see cref="HostServices.PostToUI"/> marshals onto this thread, so AI
/// stream-chunk mutations (<c>AiChatViewModel</c>/<c>StoryAnalysisViewModel</c>)
/// run serially and are then relayed to the renderer by the webview controllers.
/// </summary>
public sealed class UiPump : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private volatile bool _disposed;

    public UiPump()
    {
        _thread = new Thread(RunLoop)
        {
            IsBackground = true,
            Name = "NovalistUiPump",
        };
        _thread.Start();
    }

    // The blocking drain loop. Instrumentation cannot reliably attribute the
    // background-thread blocking wait, so the loop shell is excluded; the unit
    // of work (RunOne) is fully covered via the public Post/Invoke API.
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private void RunLoop()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
            RunOne(action);
    }

    /// <summary>Runs one queued action, swallowing (but logging the shape of) faults
    /// so one bad extension callback cannot tear down the pump thread.</summary>
    internal void RunOne(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            // Log only the exception type — never the message, which could echo story content.
            Log.Error($"[UiPump] queued action threw {ex.GetType().Name}");
        }
    }

    /// <summary>True when the calling thread is the pump thread.</summary>
    public bool CheckAccess() => Thread.CurrentThread == _thread;

    /// <summary>Queues an action to run on the pump thread. Dropped after disposal.</summary>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            _queue.Add(action);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // The queue was completed / disposed (pump is shutting down); drop it.
        }
    }

    /// <summary>Runs the action on the pump thread and completes when it has run.
    /// Runs inline when already on the pump thread.</summary>
    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try { action(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    /// <summary>Runs the function on the pump thread and blocks until it returns.
    /// Runs inline when already on the pump thread. Never call from the pump
    /// thread with work that itself waits on the pump.</summary>
    public T Invoke<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        if (CheckAccess())
            return func();

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _queue.CompleteAdding();
        if (!CheckAccess())
            _thread.Join();
        _queue.Dispose();
    }
}
