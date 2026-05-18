using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Novalist.Desktop.Utilities;

/// <summary>
/// Workaround for an Avalonia.Controls.WebView 12.0.1 + GtkX11 + XWayland
/// rendering bug: the native WebKitGTK widget stays at its ~200×200 initial
/// allocation because layout/size changes don't propagate through the
/// XEmbed bridge. Setting Avalonia-side Width/Height updates the wrapper's
/// bounds and Avalonia's layout reports the correct size, but pixels stay
/// clipped because the X11 child window backing the embed never resizes.
///
/// The reliable combination is:
///   1. <c>gtk_widget_set_size_request</c> + <c>gtk_widget_size_allocate</c>
///      walked up every ancestor of the WebKitWebView.
///   2. A hide→show toggle on the WebView after AdapterCreated, which
///      forces GTK to tear down and re-establish the XEmbed surface from
///      scratch — that's what finally makes WebKit allocate a render
///      buffer of the right size.
///   3. Re-apply on every host SizeChanged so window resizes also work.
///
/// Filed upstream: see docs/avalonia-webview-bug-report/.
/// </summary>
internal static class NativeWebViewSizeFix
{
    public static void Attach(NativeWebView webView, Control host)
    {
        if (!OperatingSystem.IsLinux()) return;

        webView.AdapterCreated += (_, _) =>
        {
            Force(webView, host.Bounds.Width, host.Bounds.Height);
            // Hide+show is what finally kicks WebKit to allocate the right
            // surface; the size_allocate calls alone update GTK layout but
            // not the underlying X11 child window.
            Dispatcher.UIThread.Post(() =>
            {
                if (webView == null) return;
                webView.IsVisible = false;
                Dispatcher.UIThread.Post(() =>
                {
                    webView.IsVisible = true;
                    Force(webView, host.Bounds.Width, host.Bounds.Height);
                }, DispatcherPriority.Background);
            }, DispatcherPriority.Background);
        };

        host.SizeChanged += (_, ev) =>
        {
            webView.Width = ev.NewSize.Width;
            webView.Height = ev.NewSize.Height;
            Force(webView, ev.NewSize.Width, ev.NewSize.Height);
        };
    }

    private static void Force(NativeWebView webView, double width, double height)
    {
        if (!OperatingSystem.IsLinux()) return;
        if (width <= 0 || height <= 0) return;
        try
        {
            var handle = webView.TryGetPlatformHandle();
            if (handle is not IGtkWebViewPlatformHandle gtk || gtk.WebKitWebView == IntPtr.Zero)
                return;

            var iw = (int)width;
            var ih = (int)height;
            var cur = gtk.WebKitWebView;
            while (cur != IntPtr.Zero)
            {
                gtk_widget_set_size_request(cur, iw, ih);
                var alloc = new GtkAllocation { X = 0, Y = 0, Width = iw, Height = ih };
                gtk_widget_size_allocate(cur, ref alloc);
                cur = gtk_widget_get_parent(cur);
            }
            gtk_widget_queue_resize(gtk.WebKitWebView);
            gtk_widget_queue_draw(gtk.WebKitWebView);
            _ = webView.InvokeScript("window.dispatchEvent(new Event('resize'))");
        }
        catch { /* best effort — handle may not yet be available */ }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GtkAllocation
    {
        public int X, Y, Width, Height;
    }

    [DllImport("libgtk-3.so.0")]
    private static extern void gtk_widget_set_size_request(IntPtr widget, int width, int height);

    [DllImport("libgtk-3.so.0")]
    private static extern void gtk_widget_size_allocate(IntPtr widget, ref GtkAllocation allocation);

    [DllImport("libgtk-3.so.0")]
    private static extern void gtk_widget_queue_resize(IntPtr widget);

    [DllImport("libgtk-3.so.0")]
    private static extern void gtk_widget_queue_draw(IntPtr widget);

    [DllImport("libgtk-3.so.0")]
    private static extern IntPtr gtk_widget_get_parent(IntPtr widget);
}
