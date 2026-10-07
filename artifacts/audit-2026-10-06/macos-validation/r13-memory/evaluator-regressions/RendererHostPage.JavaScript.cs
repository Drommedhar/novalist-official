#if IOS
using Foundation;
using ObjCRuntime;
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
        // Transfer the created string reference to the wrapper. The string
        // constructor retains an extra native reference in the current binding.
        using var source = Runtime.GetNSObject<NSString>(NSString.CreateNative(script), owns: true)
            ?? throw new InvalidOperationException("The editor response could not be prepared.");
        using var result = await webView.EvaluateJavaScriptAsync(source);
        return result?.ToString();
#else
        return await _web.EvaluateJavaScriptAsync(script);
#endif
    }
}
