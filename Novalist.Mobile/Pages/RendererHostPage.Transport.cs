using Microsoft.Maui.ApplicationModel;
#if IOS
using WebKit;
#endif

namespace Novalist.Mobile.Pages;

public sealed partial class RendererHostPage
{
    private readonly TaskCompletionSource _rendererReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _bridgeFailed;
    private int _disposed;

    private async Task DeliverCallbackAsync(string script)
    {
        var result = await MainThread.InvokeOnMainThreadAsync(() => EvaluateScriptAsync(script))
            .WaitAsync(TimeSpan.FromSeconds(15), _cts.Token).ConfigureAwait(false);
        if (result?.Trim('"') != "accepted") throw new IOException("The editor did not accept its response.");
    }

    private async Task FailBridgeAsync(Exception exception)
    {
        if (_disposed != 0 || Interlocked.Exchange(ref _bridgeFailed, 1) != 0) return;
        // aislop-ignore-next-line ai-slop/csharp-console-leftover -- Content-free transport failure diagnostics; no script, manuscript or path is logged.
        System.Diagnostics.Debug.WriteLine($"[MobileBridge] Connection failed: {exception.GetType().Name}");
        _cts.Cancel();
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                await EvaluateScriptAsync("window.__novalistBridgeFailed?.()")
                    .WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception) { /* A terminated WebKit process cannot receive the failure notification. */ }
            if (_disposed != 0) return;
            var reload = await DisplayAlertAsync("Editing paused",
                "The editor stopped responding. Reload Novalist to reopen your saved work and recover retained edits.",
                "Reload", "Later");
            if (!reload || Window is not { } window) return;
            Dispose();
            window.Page = new RendererHostPage();
        }).ConfigureAwait(false);
    }

#if IOS
    private WebProcessObserver? _webProcessObserver;

    private void DisposeWebProcessObserver()
    {
        if (_web.Handler?.PlatformView is WKWebView webView
            && webView.NavigationDelegate == _webProcessObserver)
            webView.NavigationDelegate = null;
        _webProcessObserver?.Dispose();
        _webProcessObserver = null;
    }

    private void ObserveWebProcess()
    {
        if (_web.Handler?.PlatformView is not WKWebView webView) return;
        _webProcessObserver ??= new WebProcessObserver(this);
        webView.NavigationDelegate = _webProcessObserver;
    }

    private sealed class WebProcessObserver(RendererHostPage owner) : WKNavigationDelegate
    {
        public override void ContentProcessDidTerminate(WKWebView webView)
            => _ = owner.FailBridgeAsync(new IOException("WebKit content process terminated."));
    }
#endif
}
