namespace Novalist.Sdk.Hooks;

/// <summary>
/// SDK v2: lets an extension attach message-handling logic to the webview
/// surfaces it declared in the manifest's <c>contributes.views</c> block.
/// Web hosts route frame messages to the matching controller and relay
/// controller posts back into the frame.
/// </summary>
public interface IWebViewContributor
{
    /// <summary>Returns a controller for the given view key, or null when the
    /// view is purely static HTML with no extension-side logic.</summary>
    IWebViewController? CreateController(string viewKey);
}

/// <summary>Message channel between one webview surface and the extension.</summary>
public interface IWebViewController
{
    /// <summary>Handles a JSON message posted by the webview. The returned
    /// string, when non-null, is delivered back to the frame as a reply.</summary>
    Task<string?> OnMessageAsync(string json);

    /// <summary>Raised when the extension wants to push a JSON message into
    /// the webview outside a request/reply exchange.</summary>
    event Action<string>? MessagePosted;
}