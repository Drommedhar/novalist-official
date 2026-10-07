using Microsoft.Maui.ApplicationModel;
#if IOS
using UIKit;
#endif

namespace Novalist.Mobile.Pages;

public sealed partial class RendererHostPage
{
    private readonly object _lifecycleGate = new();
    private Task? _backgroundSave;
    private TaskCompletionSource<bool>? _backgroundAcknowledgement;
    private int _backgroundRequestId;
    private bool _lifecycleDisposed;

    public void RequestBackgroundSave()
    {
        lock (_lifecycleGate)
        {
            if (_lifecycleDisposed || _backgroundSave is { IsCompleted: false }) return;
            var acknowledgement = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _backgroundAcknowledgement = acknowledgement;
            _backgroundSave = SaveBeforeSuspensionAsync(++_backgroundRequestId, acknowledgement.Task);
        }
    }

    private async Task SaveBeforeSuspensionAsync(int requestId, Task<bool> acknowledgement)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
#if IOS
        var application = UIApplication.SharedApplication;
        var backgroundTask = UIApplication.BackgroundTaskInvalid;
        void EndBackgroundTask()
        {
            var task = Interlocked.Exchange(ref backgroundTask, UIApplication.BackgroundTaskInvalid);
            if (task != UIApplication.BackgroundTaskInvalid) application.EndBackgroundTask(task);
        }
        backgroundTask = application.BeginBackgroundTask("Novalist save", () =>
        {
            deadline.Cancel();
            EndBackgroundTask();
        });
#endif
        try
        {
            var script = $"typeof window.__novalistLifecycleSave === 'function' ? "
                + $"(window.__novalistLifecycleSave({requestId}), 'requested') : 'unavailable'";
            var state = await MainThread.InvokeOnMainThreadAsync(() => EvaluateScriptAsync(script))
                .WaitAsync(deadline.Token).ConfigureAwait(false);
            if (state?.Trim('"') != "requested") return;
            if (!await acknowledgement.WaitAsync(deadline.Token).ConfigureAwait(false))
                // aislop-ignore-next-line ai-slop/csharp-console-leftover -- Reports save outcome without journal content, titles, or paths.
                System.Diagnostics.Debug.WriteLine("[MobileSave] Renderer retained unsaved recovery data.");
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            // aislop-ignore-next-line ai-slop/csharp-console-leftover -- Identifies a lifecycle deadline without exposing manuscript data.
            System.Diagnostics.Debug.WriteLine("[MobileSave] Save deadline expired or page closed.");
        }
        catch (Exception exception)
        {
            // aislop-ignore-next-line ai-slop/csharp-console-leftover -- Records only the exception type when the lifecycle bridge fails.
            System.Diagnostics.Debug.WriteLine($"[MobileSave] Save request failed: {exception.GetType().Name}");
        }
        finally
        {
            lock (_lifecycleGate)
            {
                if (_backgroundRequestId == requestId) _backgroundAcknowledgement = null;
            }
#if IOS
            await MainThread.InvokeOnMainThreadAsync(EndBackgroundTask).ConfigureAwait(false);
#endif
        }
    }

    private bool CompleteBackgroundSave(int requestId, bool success)
    {
        lock (_lifecycleGate)
            return requestId == _backgroundRequestId && _backgroundAcknowledgement?.TrySetResult(success) == true;
    }

    private void DisposeLifecycle()
    {
        lock (_lifecycleGate)
        {
            _lifecycleDisposed = true;
            _backgroundAcknowledgement?.TrySetCanceled();
            _backgroundAcknowledgement = null;
        }
    }
}
