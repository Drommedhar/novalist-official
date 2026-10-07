#if IOS
using WebKit;
#endif

namespace Novalist.Mobile.Pages;

public sealed partial class RendererHostPage
{
    private async Task<string?> EvaluateScriptAsync(string script)
    {
#if IOS
        if (_web.Handler?.PlatformView is not WKWebView webView)
            throw new InvalidOperationException("The editor is not ready.");

        // MAUI's async-void evaluation handler can throw outside the caller's
        // task. Await WebKit directly so process failures reach our recovery path.
        using var result = await webView.EvaluateJavaScriptAsync(script);
        return result?.ToString();
#else
        return await _web.EvaluateJavaScriptAsync(script);
#endif
    }
}
