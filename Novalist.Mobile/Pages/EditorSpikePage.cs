namespace Novalist.Mobile.Pages;

/// <summary>
/// Phase 0, checkpoint 2 (the single highest risk in the port): load the real
/// contenteditable editor (app/src/renderer/public/editor/editor.html, copied
/// into Resources/Raw/editor at build) inside a HybridWebView and validate typing,
/// caret, and selection on a real device. No RPC bridge here - that is Phase 1;
/// this checkpoint answers only "does the editor behave on WKWebView?".
/// </summary>
public sealed class EditorSpikePage : ContentPage
{
    public EditorSpikePage()
    {
        Title = "Editor spike";

        var web = new HybridWebView
        {
            HybridRoot = "editor",
            DefaultFile = "editor.html",
            VerticalOptions = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Fill,
        };

        Content = web;
    }
}
