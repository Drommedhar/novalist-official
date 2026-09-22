using CoreGraphics;
using Foundation;
using UIKit;

namespace Novalist.Mobile.Services;

/// <summary>Hands a completed file to iOS, including Save to Files and AirDrop.</summary>
public static class ExportSharing
{
    public static Task<bool> ShareAsync(string path)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            NSUrl? url = null;
            UIActivityViewController? sheet = null;
            try
            {
                var window = UIApplication.SharedApplication.ConnectedScenes
                    .OfType<UIWindowScene>()
                    .Where(scene => scene.ActivationState == UISceneActivationState.ForegroundActive)
                    .SelectMany(scene => scene.Windows)
                    .FirstOrDefault(window => window.IsKeyWindow);
                var presenter = window?.RootViewController;
                while (presenter?.PresentedViewController != null)
                    presenter = presenter.PresentedViewController;
                if (presenter?.View == null)
                    throw new InvalidOperationException("No window is available for sharing.");

                url = NSUrl.FromFilename(path);
                sheet = new UIActivityViewController([url], null);
                sheet.CompletionWithItemsHandler = (_, completed, _, error) =>
                {
                    if (error != null)
                        completion.TrySetException(new IOException("The export could not be shared."));
                    else
                        completion.TrySetResult(completed);
                    sheet.CompletionWithItemsHandler = null;
                    sheet.Dispose();
                    url.Dispose();
                };

                // A popover without an anchor crashes on iPad. The initiating
                // button is in the web view, so anchor within the native window.
                if (sheet.PopoverPresentationController is { } popover)
                {
                    popover.SourceView = presenter.View;
                    popover.SourceRect = new CGRect(presenter.View.Bounds.GetMidX(),
                        presenter.View.Bounds.GetMidY(), 1, 1);
                    popover.PermittedArrowDirections = 0;
                }
                presenter.PresentViewController(sheet, true, null);
            }
            catch (Exception error)
            {
                sheet?.Dispose();
                url?.Dispose();
                completion.TrySetException(error);
            }
        });
        return completion.Task;
    }
}
