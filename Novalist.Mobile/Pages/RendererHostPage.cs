using System.Text;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using Nerdbank.Streams;
using Novalist.Backend;
using Novalist.Core.Services;
#if IOS
using UIKit;
using WebKit;
#endif

namespace Novalist.Mobile.Pages;

/// <summary>
/// Phase 1 + 2: hosts the real React renderer (built by `npm run build:mobile`
/// into Resources/Raw/app) in a HybridWebView, replacing Electron's main +
/// child-process + preload.
///
/// Two channels share the one HybridWebView raw-message pipe, told apart by the
/// first byte of each JS->native message:
///   - RPC transport: base64 of LSP-framed JSON-RPC bytes, piped to the in-process
///     <see cref="BackendHost"/> over a FullDuplexStream pair. rpc/client.ts sees a
///     normal MessagePort (mobile/shim.ts), so it is unchanged.
///   - Host bridge: JSON `{id,method,args}` (starts with '{') implementing the
///     native window.novalist surface with MAUI Essentials.
/// </summary>
public sealed class RendererHostPage : ContentPage, IDisposable
{
    private readonly HybridWebView _web;
    private readonly BackendHost _host;
    private readonly Stream _bridge;
    private readonly CancellationTokenSource _cts = new();

    public RendererHostPage()
    {
        var (backendEnd, nativeEnd) = FullDuplexStream.CreatePair();
        _bridge = nativeEnd;
        // UnavailableProcessRunner: the sandbox forbids launching `git`, so Git
        // degrades to "unavailable" rather than throwing (Phase 3).
        _host = new BackendHost(FileSystem.Current.AppDataDirectory, new UnavailableProcessRunner());
        _host.Attach(backendEnd, backendEnd);

        _web = new HybridWebView
        {
            HybridRoot = "app",
            DefaultFile = "index.mobile.html",
            VerticalOptions = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Fill,
        };
        _web.RawMessageReceived += OnRawMessageReceived;
        Content = _web;

#if IOS
        // Lock zoom once the WKWebView exists: prevents the iOS focus-zoom trap
        // (tapping a contenteditable auto-zooms the viewport with no way back).
        _web.HandlerChanged += (_, _) => LockWebViewZoom();
#endif

        _ = PumpBackendToWebAsync(_cts.Token);
    }

#if IOS
    private void LockWebViewZoom()
    {
        if (_web.Handler?.PlatformView is WKWebView wk)
        {
            wk.ScrollView.MinimumZoomScale = 1f;
            wk.ScrollView.MaximumZoomScale = 1f;
            wk.ScrollView.BouncesZoom = false;
            if (wk.ScrollView.PinchGestureRecognizer != null)
                wk.ScrollView.PinchGestureRecognizer.Enabled = false;
        }
    }
#endif

#if IOS
    // Native iOS 26 Liquid Glass bottom navigation. A plain UITabBar adopts the
    // system Liquid Glass material on iOS 26 automatically; it is overlaid on the
    // HybridWebView and drives the single-pane web layout via window.__novalistTab.
    // (The web content insets its bottom padding by --nl-mobile-tabbar-h.)
    private UITabBar? _tabBar;

    // Last localized titles pushed by the web (setTabTitles), in tab order. Held
    // so titles that arrive before the bar is built (or a rebuild) still apply.
    private string[]? _tabTitles;

    private static readonly (string Key, string Title, string Symbol)[] Tabs =
    {
        ("dashboard", "Dashboard", "square.grid.2x2"),
        // key stays "manuscript" (internal); "Write" avoids clashing with the
        // desktop Manuscript (corkboard) view. English titles here are only the
        // pre-localization fallback; the web pushes localized ones via setTabTitles.
        ("manuscript", "Write", "square.and.pencil"),
        ("codex", "Codex", "person.2"),
        ("search", "Search", "magnifyingglass"),
        ("more", "More", "ellipsis"),
    };

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (_tabBar == null && Handler?.PlatformView is UIView native)
            AddNativeTabBar(native);
    }

    private void AddNativeTabBar(UIView parent)
    {
        var items = new UITabBarItem[Tabs.Length];
        for (var i = 0; i < Tabs.Length; i++)
            items[i] = new UITabBarItem(Tabs[i].Title, UIImage.GetSystemImage(Tabs[i].Symbol), i) { Tag = i };

        var bar = new MetricsTabBar { TranslatesAutoresizingMaskIntoConstraints = false, Hidden = true };
        // Re-measure whenever the bar lays out (first appearance, rotation): the
        // iOS 26 floating tab bar has a different height/position in landscape, so
        // the web's bottom inset must follow the real frame rather than a constant.
        bar.LayoutChanged = PushTabBarMetrics;
        _tabBar = bar;
        _tabBar.SetItems(items, animated: false);
        _tabBar.SelectedItem = items[0];
        ApplyTabTitles();   // adopt any titles the web pushed before the bar existed
        // "Search" and "More" that map to a dialog/sheet should not stick as the
        // selected tab; the web decides. We only forward the tap.
        _tabBar.ItemSelected += (_, e) => OnNativeTabSelected((int)e.Item.Tag);

        parent.AddSubview(_tabBar);
        NSLayoutConstraint.ActivateConstraints(new[]
        {
            _tabBar.LeadingAnchor.ConstraintEqualTo(parent.LeadingAnchor),
            _tabBar.TrailingAnchor.ConstraintEqualTo(parent.TrailingAnchor),
            _tabBar.BottomAnchor.ConstraintEqualTo(parent.BottomAnchor),
        });
    }

    private void OnNativeTabSelected(int tag)
    {
        if (tag < 0 || tag >= Tabs.Length) return;
        var key = Tabs[tag].Key;
        _ = EvalOnMainAsync($"window.__novalistTab && window.__novalistTab('{key}')");
    }

    // Push the web's localized titles onto the live UITabBarItems (main thread).
    private void ApplyTabTitles()
    {
        if (_tabBar?.Items is not { } items || _tabTitles == null) return;
        var count = Math.Min(items.Length, _tabTitles.Length);
        for (var i = 0; i < count; i++)
            items[i].Title = _tabTitles[i];
    }

    // Tell the web how much vertical space the (bottom-pinned) tab bar covers, so
    // .mobile-content can inset its bottom to clear it. The bar's bottom is the
    // screen bottom, so its frame height is the covered strip (already spanning the
    // home-indicator zone). Re-pushed on every layout so it tracks rotation.
    private double _lastPushedTabH = -1;

    private void PushTabBarMetrics()
    {
        if (_tabBar == null) return;
        var h = Math.Round(_tabBar.Frame.Height);
        if (h <= 0 || Math.Abs(h - _lastPushedTabH) < 0.5) return;   // unchanged layout
        _lastPushedTabH = h;
        var px = h.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _ = EvalOnMainAsync(
            $"document.documentElement.style.setProperty('--nl-mobile-tabbar-h','{px}px')");
    }

    // UITabBar that reports its layout so the web inset can track the real frame.
    private sealed class MetricsTabBar : UITabBar
    {
        public Action? LayoutChanged;

        public override void LayoutSubviews()
        {
            base.LayoutSubviews();
            LayoutChanged?.Invoke();
        }
    }
#endif

    private void OnRawMessageReceived(object? sender, HybridWebViewRawMessageReceivedEventArgs e)
    {
        var message = e.Message;
        if (string.IsNullOrEmpty(message)) return;

        // '{' => host-bridge JSON; otherwise base64 RPC frame bytes.
        if (message[0] == '{')
        {
            _ = HandleHostCallAsync(message);
            return;
        }

        try
        {
            var bytes = Convert.FromBase64String(message);
            _bridge.Write(bytes, 0, bytes.Length);
            _bridge.Flush();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RendererHostPage] inbound bridge write failed: {ex.GetType().Name}");
        }
    }

    // native -> JS: backend response/notification bytes -> base64 -> the shim's
    // RPC receiver. EvaluateJavaScript must run on the UI thread.
    private async Task PumpBackendToWebAsync(CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        while (!ct.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await _bridge.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
            }
            catch (Exception)
            {
                break;
            }
            if (read <= 0) break;

            var payload = Convert.ToBase64String(buffer, 0, read);
            await EvalOnMainAsync($"window.__novalistRecv('{payload}')").ConfigureAwait(false);
        }
    }

    // ---- Host bridge (window.novalist) --------------------------------------

    private async Task HandleHostCallAsync(string json)
    {
        var id = 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            id = root.GetProperty("id").GetInt32();
            var method = root.GetProperty("method").GetString() ?? "";
            var args = root.TryGetProperty("args", out var a) ? a : default;
            var result = await InvokeHostAsync(method, args).ConfigureAwait(false);
            await SendHostResultAsync(id, ok: true, result, error: null).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await SendHostResultAsync(id, ok: false, result: null, error: ex.Message).ConfigureAwait(false);
        }
    }

    private static string ArgString(JsonElement args, int index)
    {
        if (args.ValueKind != JsonValueKind.Array || index >= args.GetArrayLength()) return "";
        var el = args[index];
        return el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "";
    }

    private async Task<object?> InvokeHostAsync(string method, JsonElement args)
    {
        switch (method)
        {
            case "pickFolder":
            {
                // App-container storage: projects live under a writable sandbox dir.
                // No external folder picker yet (security-scoped URLs come later).
                var dir = Path.Combine(FileSystem.Current.AppDataDirectory, "Projects");
                Directory.CreateDirectory(dir);
                return dir;
            }
            case "pickFile":
            {
                var options = new PickOptions { PickerTitle = ArgString(args, 0) };
                if (ArgString(args, 1) == "images") options.FileTypes = FilePickerFileType.Images;
                var result = await MainThread.InvokeOnMainThreadAsync(() => FilePicker.Default.PickAsync(options))
                    .ConfigureAwait(false);
                return result?.FullPath;
            }
            case "saveFile":
            {
                // App-container: hand back a cache path the backend can write to.
                var name = ArgString(args, 0);
                return Path.Combine(FileSystem.Current.CacheDirectory, string.IsNullOrEmpty(name) ? "export" : name);
            }
            case "openExternal":
            {
                var target = ArgString(args, 0);
                try
                {
                    await MainThread.InvokeOnMainThreadAsync(() => Launcher.Default.OpenAsync(target)).ConfigureAwait(false);
                    return true;
                }
                catch { return false; }
            }
            case "copyText":
                await MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.SetTextAsync(ArgString(args, 0)))
                    .ConfigureAwait(false);
                return null;
            case "revealPath":
                return false;          // no file-manager reveal on iOS
            case "readClipboardImage":
                return null;           // MAUI Clipboard is text-only
            case "setNavVisible":
            {
                // Show the native Liquid Glass tab bar only inside a project
                // (hidden on the welcome/start screen).
                var visible = args.ValueKind == JsonValueKind.Array
                    && args.GetArrayLength() > 0
                    && args[0].ValueKind == JsonValueKind.True;
#if IOS
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (_tabBar != null) _tabBar.Hidden = !visible;
                });
#endif
                return null;
            }
            case "setTabTitles":
            {
                // args[0] = localized titles in tab order (dashboard, manuscript,
                // codex, search, more). Pushed by the web on mount + language change.
                var titles = new List<string>();
                if (args.ValueKind == JsonValueKind.Array && args.GetArrayLength() > 0
                    && args[0].ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in args[0].EnumerateArray())
                        titles.Add(el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "");
                }
#if IOS
                _tabTitles = titles.ToArray();
                await MainThread.InvokeOnMainThreadAsync(ApplyTabTitles);
#endif
                return null;
            }
            default:
                return null;           // unknown / JS-side no-ops
        }
    }

    private static readonly JsonSerializerOptions CamelCase =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private Task SendHostResultAsync(int id, bool ok, object? result, string? error)
    {
        // camelCase so the shim reads {id, ok, result, error}.
        var payload = JsonSerializer.Serialize(new HostResult(id, ok, result, error), CamelCase);
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        return EvalOnMainAsync($"window.__novalistHostResult('{b64}')");
    }

    private Task EvalOnMainAsync(string js) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try { await _web.EvaluateJavaScriptAsync(js); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RendererHostPage] eval failed: {ex.GetType().Name}");
            }
        });

    private sealed record HostResult(int Id, bool Ok, object? Result, string? Error);

    public void Dispose()
    {
        _cts.Cancel();
        _web.RawMessageReceived -= OnRawMessageReceived;
        _host.Dispose();
        _bridge.Dispose();
        _cts.Dispose();
    }
}
