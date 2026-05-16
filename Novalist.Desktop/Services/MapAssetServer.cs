using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Novalist.Desktop.Services;

/// <summary>
/// Loopback HTTP server used by MapView on macOS. WKWebView (via
/// Avalonia.Controls.WebView's NavigateToString path) parks inline HTML at
/// http://localhost:<port>/ with no base URL, so map.html's
/// &lt;script type="module" src="map3d.js"&gt;, the importmap, and any
/// file:// image referenced by setImageBaseUrl all fail (relative scripts
/// 404, cross-origin file:// loads are blocked).
///
/// This server serves two roots from a loopback origin so the map page
/// loads exactly like it does on Windows:
///   GET /...            -> Assets/Map/ (map.html, map3d.js, three.*, vegetation/, draco/, basis/, libs/)
///   GET /book/...       -> active book root (project images)
/// </summary>
internal static class MapAssetServer
{
    private static HttpListener? _listener;
    private static int _port;
    private static string? _assetsRoot;
    private static Func<string?>? _bookRootProvider;
    private static readonly object _lock = new();

    public static string BaseUrl => $"http://127.0.0.1:{_port}/";
    public static string BookBaseUrl => $"http://127.0.0.1:{_port}/book/";

    public static void EnsureStarted(string assetsRoot, Func<string?> bookRootProvider)
    {
        lock (_lock)
        {
            if (_listener != null) return;
            _assetsRoot = Path.GetFullPath(assetsRoot);
            _bookRootProvider = bookRootProvider;
            _port = PickFreePort();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            listener.Start();
            _listener = listener;
            _ = Task.Run(AcceptLoop);
            Console.Error.WriteLine($"[MapAssetServer] listening on {BaseUrl} (assets={_assetsRoot})");
        }
    }

    private static int PickFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try { return ((IPEndPoint)probe.LocalEndpoint).Port; }
        finally { probe.Stop(); }
    }

    private static async Task AcceptLoop()
    {
        var listener = _listener;
        if (listener == null) return;
        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch { return; }
            _ = Task.Run(() => HandleRequest(ctx));
        }
    }

    private static void HandleRequest(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            string? filePath = ResolveRequestPath(path);
            if (filePath == null || !File.Exists(filePath))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }

            ctx.Response.ContentType = ContentTypeFor(Path.GetExtension(filePath));
            ctx.Response.Headers["Cache-Control"] = "no-store";
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                ctx.Response.ContentLength64 = fs.Length;
                fs.CopyTo(ctx.Response.OutputStream);
            }
            ctx.Response.OutputStream.Close();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MapAssetServer] request failed: {ex.Message}");
            try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
        }
    }

    private static string? ResolveRequestPath(string urlPath)
    {
        // %20 etc. — decode before touching the filesystem.
        var decoded = Uri.UnescapeDataString(urlPath);
        if (decoded.StartsWith("/book/", StringComparison.Ordinal))
        {
            var bookRoot = _bookRootProvider?.Invoke();
            if (string.IsNullOrEmpty(bookRoot)) return null;
            return SafeJoin(bookRoot, decoded.Substring("/book/".Length));
        }

        var rel = decoded.TrimStart('/');
        if (rel.Length == 0) rel = "map.html";
        return SafeJoin(_assetsRoot!, rel);
    }

    private static string? SafeJoin(string root, string rel)
    {
        var rootFull = Path.GetFullPath(root + Path.DirectorySeparatorChar);
        var combined = Path.GetFullPath(Path.Combine(rootFull, rel));
        // Reject path traversal — resolved path must stay under root.
        if (!combined.StartsWith(rootFull, StringComparison.Ordinal)) return null;
        return combined;
    }

    private static readonly Dictionary<string, string> _types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".htm"]  = "text/html; charset=utf-8",
        [".js"]   = "application/javascript; charset=utf-8",
        [".mjs"]  = "application/javascript; charset=utf-8",
        [".css"]  = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".png"]  = "image/png",
        [".jpg"]  = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".gif"]  = "image/gif",
        [".svg"]  = "image/svg+xml",
        [".bmp"]  = "image/bmp",
        [".ico"]  = "image/x-icon",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".ttf"]  = "font/ttf",
        [".otf"]  = "font/otf",
        [".glb"]  = "model/gltf-binary",
        [".gltf"] = "model/gltf+json",
        [".bin"]  = "application/octet-stream",
        [".ktx2"] = "image/ktx2",
        [".wasm"] = "application/wasm",
        [".hdr"]  = "application/octet-stream",
        [".exr"]  = "application/octet-stream",
    };

    private static string ContentTypeFor(string ext) =>
        _types.TryGetValue(ext, out var t) ? t : "application/octet-stream";
}
