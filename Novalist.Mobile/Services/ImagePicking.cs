using CoreGraphics;
using Foundation;
using Microsoft.Maui.Storage;
using PhotosUI;
using UIKit;
using UniformTypeIdentifiers;

namespace Novalist.Mobile.Services;

/// <summary>
/// Image picking for iOS. The document picker (FilePicker) can only reach files
/// in Files/iCloud Drive - it never shows the photo library, so "add a banner /
/// cover / gallery image" used to be unable to see the camera roll at all.
///
/// An image request therefore opens a two-way action sheet: the photo library
/// (PHPickerViewController - out-of-process, so it needs no photo-library
/// permission and no usage description) or the document picker, for images that
/// live in Files but not in Photos. Both branches end at a real filesystem path
/// in the cache directory that the backend imports from, exactly like desktop.
///
/// Photos taken on an iPhone are usually HEIC, which the project's image
/// handling (and the desktop renderer) does not support, so anything outside
/// the portable set is transcoded to JPEG before the path is handed back.
/// </summary>
public static class ImagePicking
{
    // Extensions the app itself treats as images (Novalist.Core EntityService);
    // anything else has to be transcoded or it silently vanishes from galleries.
    private static readonly string[] PortableExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".svg", ".webp"];

    // PHPickerViewController.Delegate is a weak reference; without a strong one
    // here the delegate can be collected before the user finishes picking.
    private static readonly List<NSObject> Retained = new();

    /// <summary>
    /// Ask where the image should come from, then present that picker. Labels are
    /// passed in already localized by the renderer. Returns a readable path, or
    /// null if the user cancelled at either step.
    /// </summary>
    public static Task<string?> PickImageAsync(
        string title, string photosLabel, string filesLabel, string cancelLabel)
    {
        var tcs = new TaskCompletionSource<string?>();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var top = TopViewController();
                if (top?.View == null) { tcs.TrySetResult(null); return; }

                var sheet = UIAlertController.Create(
                    string.IsNullOrEmpty(title) ? null : title, null, UIAlertControllerStyle.ActionSheet);
                // Presenting is deferred by one run-loop turn: UIKit is still tearing
                // the sheet down when the handler runs, and presenting into a
                // dismissing controller is how a picker ends up never appearing.
                sheet.AddAction(UIAlertAction.Create(photosLabel, UIAlertActionStyle.Default,
                    _ => MainThread.BeginInvokeOnMainThread(() => PresentPhotoLibrary(tcs))));
                sheet.AddAction(UIAlertAction.Create(filesLabel, UIAlertActionStyle.Default,
                    _ => MainThread.BeginInvokeOnMainThread(() => PresentDocumentPicker(title, tcs))));
                sheet.AddAction(UIAlertAction.Create(
                    cancelLabel, UIAlertActionStyle.Cancel, _ => tcs.TrySetResult(null)));

                // iPad renders an action sheet as a popover and throws unless it is
                // anchored. There is no button view to point at (the tap came from
                // the web view), so anchor to the bottom centre with no arrow.
                if (sheet.PopoverPresentationController is { } popover)
                {
                    popover.SourceView = top.View;
                    popover.SourceRect = new CGRect(top.View.Bounds.GetMidX(), top.View.Bounds.GetMaxY(), 0, 0);
                    popover.PermittedArrowDirections = 0;
                }
                top.PresentViewController(sheet, true, null);
            }
            catch
            {
                tcs.TrySetResult(null);
            }
        });
        return tcs.Task;
    }

    private static void PresentPhotoLibrary(TaskCompletionSource<string?> tcs)
    {
        try
        {
            var top = TopViewController();
            if (top == null) { tcs.TrySetResult(null); return; }

            var config = new PHPickerConfiguration { SelectionLimit = 1, Filter = PHPickerFilter.ImagesFilter };
            var handler = new PhotoPickerDelegate(tcs);
            Retained.Add(handler);
            var picker = new PHPickerViewController(config) { Delegate = handler };
            top.PresentViewController(picker, true, null);
        }
        catch
        {
            tcs.TrySetResult(null);
        }
    }

    private static async void PresentDocumentPicker(string title, TaskCompletionSource<string?> tcs)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = title,
                FileTypes = FilePickerFileType.Images,
            }).ConfigureAwait(false);
            tcs.TrySetResult(result?.FullPath);
        }
        catch
        {
            tcs.TrySetResult(null);
        }
    }

    private sealed class PhotoPickerDelegate : PHPickerViewControllerDelegate
    {
        private readonly TaskCompletionSource<string?> _tcs;

        public PhotoPickerDelegate(TaskCompletionSource<string?> tcs) => _tcs = tcs;

        public override void DidFinishPicking(PHPickerViewController picker, PHPickerResult[] results)
        {
            picker.DismissViewController(true, null);
            Retained.Remove(this);

            if (results.Length == 0) { _tcs.TrySetResult(null); return; }

            // The URL handed to the callback is a short-lived temp file the system
            // deletes as soon as the callback returns, so it is copied out here.
            results[0].ItemProvider.LoadFileRepresentation(UTTypes.Image.Identifier, (url, _) =>
            {
                string? staged = null;
                try { if (url?.Path is { Length: > 0 } path) staged = Stage(path); }
                catch { staged = null; }
                _tcs.TrySetResult(staged);
            });
        }
    }

    // Copy the picked file into the cache directory (the backend imports from a
    // path) and make sure the result is in a format the app can actually display.
    private static string? Stage(string sourcePath)
    {
        var folder = Path.Combine(FileSystem.Current.CacheDirectory, "picked-images");
        Directory.CreateDirectory(folder);
        var name = Path.GetFileName(sourcePath);
        if (string.IsNullOrEmpty(name)) name = "image";
        var staged = Path.Combine(folder, name);
        File.Copy(sourcePath, staged, overwrite: true);

        var extension = Path.GetExtension(staged).ToLowerInvariant();
        if (PortableExtensions.Contains(extension)) return staged;
        return TranscodeToJpeg(staged) ?? staged;
    }

    // HEIC/HEIF (and anything else exotic) -> JPEG, so the image survives a sync
    // to the desktop app, where the renderer cannot decode Apple's formats.
    private static string? TranscodeToJpeg(string path)
    {
        using var image = UIImage.FromFile(path);
        using var jpeg = image?.AsJPEG(0.92f);
        if (jpeg == null) return null;

        var target = Path.ChangeExtension(path, ".jpg");
        if (!jpeg.Save(target, auxiliaryFile: true, out _)) return null;
        if (!string.Equals(target, path, StringComparison.Ordinal))
        {
            try { File.Delete(path); }
            catch { /* the stale original is only cache; leaving it is harmless */ }
        }
        return target;
    }

    private static UIViewController? TopViewController()
    {
        UIWindow? window = null;
        foreach (var w in UIApplication.SharedApplication.Windows)
        {
            if (w.IsKeyWindow) { window = w; break; }
            window ??= w;
        }
        var vc = window?.RootViewController;
        while (vc?.PresentedViewController != null) vc = vc.PresentedViewController;
        return vc;
    }
}
