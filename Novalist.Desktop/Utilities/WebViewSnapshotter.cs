using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;

namespace Novalist.Desktop.Utilities;

/// <summary>
/// Captures the current visual contents of a native WebView control into an
/// Avalonia <see cref="Bitmap"/>. Used to swap an image-overlay in place of
/// the native HWND/NSView while overlays/dialogs are open, avoiding the
/// jarring blank flash caused by simply toggling IsVisible.
/// </summary>
internal static class WebViewSnapshotter
{
    public static Bitmap? Capture(Control webView)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return CaptureWindows(webView);
            if (OperatingSystem.IsMacOS())
                return CaptureMacOS(webView);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WebViewSnapshotter] {ex}");
        }
        return null;
    }

    // ── Windows (PrintWindow against the top-level HWND) ─────────────

    private const uint PW_CLIENTONLY = 0x00000001;
    private const uint PW_RENDERFULLCONTENT = 0x00000002;
    private const uint SRCCOPY = 0x00CC0020;
    private const int BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage,
        out IntPtr ppvBits, IntPtr hSection, uint offset);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public uint[] bmiColors;
    }

    private static Bitmap? CaptureWindows(Control webView)
    {
        var topLevel = TopLevel.GetTopLevel(webView);
        if (topLevel?.TryGetPlatformHandle() is not { } handle) return null;
        var hwnd = handle.Handle;
        if (hwnd == IntPtr.Zero) return null;

        var bounds = webView.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;

        // Translate webview-local origin to top-level (window) coordinates.
        var origin = webView.TranslatePoint(new Point(0, 0), topLevel) ?? new Point(0, 0);

        var scaling = topLevel.RenderScaling;
        int srcX = (int)Math.Round(origin.X * scaling);
        int srcY = (int)Math.Round(origin.Y * scaling);
        int srcW = (int)Math.Round(bounds.Width * scaling);
        int srcH = (int)Math.Round(bounds.Height * scaling);
        if (srcW <= 0 || srcH <= 0) return null;

        // Get the full client area pixel size of the window.
        var clientSize = topLevel.ClientSize;
        int winW = (int)Math.Round(clientSize.Width * scaling);
        int winH = (int)Math.Round(clientSize.Height * scaling);
        if (winW <= 0 || winH <= 0) return null;

        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return null;
        IntPtr memDc = IntPtr.Zero;
        IntPtr dib = IntPtr.Zero;
        IntPtr oldBmp = IntPtr.Zero;
        try
        {
            memDc = CreateCompatibleDC(screenDc);
            if (memDc == IntPtr.Zero) return null;

            var bmi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = winW,
                    biHeight = -winH, // top-down
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = BI_RGB,
                },
                bmiColors = new uint[4],
            };

            dib = CreateDIBSection(memDc, ref bmi, DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero) return null;

            oldBmp = SelectObject(memDc, dib);

            if (!PrintWindow(hwnd, memDc, PW_CLIENTONLY | PW_RENDERFULLCONTENT))
                return null;

            // Crop the WebView region out of the full window bitmap.
            int strideSrc = winW * 4;
            int strideDst = srcW * 4;

            // Clamp source rect to the bitmap.
            if (srcX < 0) { srcW += srcX; srcX = 0; }
            if (srcY < 0) { srcH += srcY; srcY = 0; }
            if (srcX + srcW > winW) srcW = winW - srcX;
            if (srcY + srcH > winH) srcH = winH - srcY;
            if (srcW <= 0 || srcH <= 0) return null;

            var pixelSize = new PixelSize(srcW, srcH);
            var dpi = new Vector(96 * scaling, 96 * scaling);
            var writeable = new WriteableBitmap(pixelSize, dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
            var rowBuf = new byte[strideDst];
            using (var fb = writeable.Lock())
            {
                for (int y = 0; y < srcH; y++)
                {
                    var srcRowPtr = IntPtr.Add(bits, (srcY + y) * strideSrc + srcX * 4);
                    Marshal.Copy(srcRowPtr, rowBuf, 0, strideDst);
                    // PrintWindow content is opaque; force alpha = 0xFF for safety.
                    for (int x = 3; x < strideDst; x += 4) rowBuf[x] = 0xFF;
                    var dstRowPtr = IntPtr.Add(fb.Address, y * fb.RowBytes);
                    Marshal.Copy(rowBuf, 0, dstRowPtr, strideDst);
                }
            }
            return writeable;
        }
        finally
        {
            if (memDc != IntPtr.Zero && oldBmp != IntPtr.Zero) SelectObject(memDc, oldBmp);
            if (dib != IntPtr.Zero) DeleteObject(dib);
            if (memDc != IntPtr.Zero) DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    // ── macOS (NSView cacheDisplayInRect on window contentView) ──────
    //
    // TODO(macos-snapshot): Image still renders ~1.5–2× too big when overlay
    // opens, even though capture pixels are correct.
    //
    // Verified diagnostic log:
    //   bounds=784x613.5 dip, contentH=900, rect=(330,212.5,784,613.5),
    //   px=1568x1227, bytesPerRow=6272, bpp=32, spp=4
    // → backing-scale 2× capture is correct. Bitmap pixel dims and rect match
    //   the webView's DIP bounds. Issue is on the DISPLAY side, not capture.
    //
    // Tried so far (none fixed):
    //   • Stretch=Fill + explicit Image.Width/Height pin + HAlign.Left/VAlign.Top
    //   • Stretch=None relying on bitmap DPI (192) for intrinsic DIP size
    //
    // Likely culprits to investigate on real macOS device:
    //   1. Avalonia macOS Image renderer may ignore WriteableBitmap DPI and
    //      treat pxW as DIPs → intrinsic = 1568 DIP instead of 784.
    //      Fix: override intrinsic via wrapping Border with Width/Height +
    //      Image inside with Stretch=Uniform.
    //   2. Width pin not honored when Image is direct child of Grid with
    //      `*` row/column. Try wrapping in a Canvas or Border.
    //   3. PixelFormat/AlphaFormat mismatch causing Skia to upscale.
    //   4. WriteableBitmap DPI=192 may be interpreted as "display at 2× device
    //      pixels" instead of "logical=intrinsic/2" on Avalonia's macOS backend.
    //      Test: hardcode dpi = (96, 96) and pin Image.Width=bounds.W.

    private const string LibObjc = "/usr/lib/libobjc.dylib";
    private const string LibAppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint { public double X; public double Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGSize { public double Width; public double Height; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect { public CGPoint Origin; public CGSize Size; }

    [DllImport(LibObjc, EntryPoint = "objc_getClass")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(LibObjc, EntryPoint = "sel_registerName")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr msgSend_IntPtr(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr msgSend_IntPtr_Rect(IntPtr receiver, IntPtr selector, CGRect rect);

    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr msgSend_IntPtr_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    private static extern void msgSend_Void_Rect_IntPtr(IntPtr receiver, IntPtr selector, CGRect rect, IntPtr arg);

    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    private static extern long msgSend_Long(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjc, EntryPoint = "objc_msgSend_stret")]
    private static extern void msgSend_stret_Rect(out CGRect outRect, IntPtr receiver, IntPtr selector);

    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    private static extern CGRect msgSend_Rect(IntPtr receiver, IntPtr selector);

    private static CGRect GetRect(IntPtr obj, IntPtr sel)
    {
        // arm64: structs <= 16 bytes returned in regs; CGRect (32) returned via x8 implicit pointer.
        // Standard P/Invoke marshalling handles both ARM64 and x86_64 when struct return.
        // Use stret on x86_64 only.
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            msgSend_stret_Rect(out var r, obj, sel);
            return r;
        }
        return msgSend_Rect(obj, sel);
    }

    private static Bitmap? CaptureMacOS(Control webView)
    {
        var topLevel = TopLevel.GetTopLevel(webView);
        if (topLevel?.TryGetPlatformHandle() is not { } handle) return null;
        var nsHandle = handle.Handle;
        if (nsHandle == IntPtr.Zero) return null;

        // Handle may be NSWindow or NSView depending on Avalonia internals.
        // Resolve to NSWindow if it's a view (NSView responds to "window").
        var nsWindow = nsHandle;
        var selRespondsTo = sel_registerName("respondsToSelector:");
        var selContentViewProbe = sel_registerName("contentView");
        if (msgSend_IntPtr_IntPtr(nsHandle, selRespondsTo, selContentViewProbe) == IntPtr.Zero)
        {
            var selWindow = sel_registerName("window");
            var w = msgSend_IntPtr(nsHandle, selWindow);
            if (w != IntPtr.Zero) nsWindow = w;
        }

        var bounds = webView.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;

        var origin = webView.TranslatePoint(new Point(0, 0), topLevel) ?? new Point(0, 0);

        // contentView is the NSView root of the window's client area.
        var selContentView = sel_registerName("contentView");
        var contentView = msgSend_IntPtr(nsWindow, selContentView);
        if (contentView == IntPtr.Zero) return null;

        // Get contentView frame for height (used for Y flip).
        var selBounds = sel_registerName("bounds");
        var contentBounds = GetRect(contentView, selBounds);
        var contentH = contentBounds.Size.Height;

        // NSView coords: origin bottom-left. Flip from top-left DIPs.
        var rect = new CGRect
        {
            Origin = new CGPoint
            {
                X = origin.X,
                Y = contentH - origin.Y - bounds.Height,
            },
            Size = new CGSize
            {
                Width = bounds.Width,
                Height = bounds.Height,
            },
        };
        if (rect.Size.Width <= 0 || rect.Size.Height <= 0) return null;

        // [contentView bitmapImageRepForCachingDisplayInRect:rect]
        var selBitmapRep = sel_registerName("bitmapImageRepForCachingDisplayInRect:");
        var rep = msgSend_IntPtr_Rect(contentView, selBitmapRep, rect);
        if (rep == IntPtr.Zero) return null;

        // [contentView cacheDisplayInRect:rect toBitmapImageRep:rep]
        var selCacheDisplay = sel_registerName("cacheDisplayInRect:toBitmapImageRep:");
        msgSend_Void_Rect_IntPtr(contentView, selCacheDisplay, rect, rep);

        var pxW = (int)msgSend_Long(rep, sel_registerName("pixelsWide"));
        var pxH = (int)msgSend_Long(rep, sel_registerName("pixelsHigh"));
        var bytesPerRow = (int)msgSend_Long(rep, sel_registerName("bytesPerRow"));
        var bitsPerPixel = (int)msgSend_Long(rep, sel_registerName("bitsPerPixel"));
        var samplesPerPixel = (int)msgSend_Long(rep, sel_registerName("samplesPerPixel"));
        var bitmapData = msgSend_IntPtr(rep, sel_registerName("bitmapData"));
        Console.WriteLine($"[WebViewSnapshotter.macOS] bounds={bounds.Width}x{bounds.Height} dip, " +
                          $"contentH={contentH}, rect=({rect.Origin.X},{rect.Origin.Y},{rect.Size.Width},{rect.Size.Height}), " +
                          $"px={pxW}x{pxH}, bytesPerRow={bytesPerRow}, bpp={bitsPerPixel}, spp={samplesPerPixel}");
        if (pxW <= 0 || pxH <= 0 || bitmapData == IntPtr.Zero) return null;
        if (bitsPerPixel != 32) return null; // expect RGBA8

        // Use a high DPI so bitmap logical size == webView DIP size; Image.Width
        // also pins it. Combination prevents 2x zoom on Retina.
        var scaleX = pxW / Math.Max(1.0, bounds.Width);
        var scaleY = pxH / Math.Max(1.0, bounds.Height);
        var dpi = new Vector(96 * scaleX, 96 * scaleY);
        var pixelSize = new PixelSize(pxW, pxH);
        var writeable = new WriteableBitmap(pixelSize, dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);

        var rowBuf = new byte[Math.Min(bytesPerRow, pxW * 4)];
        using (var fb = writeable.Lock())
        {
            for (int y = 0; y < pxH; y++)
            {
                var srcRowPtr = IntPtr.Add(bitmapData, y * bytesPerRow);
                Marshal.Copy(srcRowPtr, rowBuf, 0, rowBuf.Length);
                // NSBitmapImageRep default: RGBA8 (or RGB if samplesPerPixel==3).
                // Convert to BGRA8888 in-place.
                if (samplesPerPixel >= 3)
                {
                    for (int x = 0; x + 3 < rowBuf.Length; x += 4)
                    {
                        byte r = rowBuf[x];
                        byte b = rowBuf[x + 2];
                        rowBuf[x] = b;
                        rowBuf[x + 2] = r;
                        if (samplesPerPixel == 3) rowBuf[x + 3] = 0xFF;
                    }
                }
                var dstRowPtr = IntPtr.Add(fb.Address, y * fb.RowBytes);
                Marshal.Copy(rowBuf, 0, dstRowPtr, rowBuf.Length);
            }
        }
        return writeable;
    }
}
