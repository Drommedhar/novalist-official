using System.Text;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using Nerdbank.Streams;
using Novalist.Backend;
using Novalist.Core.Services;
using Novalist.Mobile.Services;
#if IOS
using CoreGraphics;
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
        // Planning opens a drawer of planning modes (Timeline / Plot Grid /
        // Calendar / Find & Replace); Settings replaces the old More tab.
        ("planning", "Plan", "square.stack.3d.up"),
        ("settings", "Settings", "gearshape"),
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

    // ---- Plan menu: a native Liquid Glass popover over the tab bar ------------

    private UIView? _planOverlay;      // transparent full-screen tap-catcher
    private UIVisualEffectView? _planMenu;

    private void ShowPlanMenu(string[] labels)
    {
        HidePlanMenu();
        if (Handler?.PlatformView is not UIView parent || _tabBar == null || labels.Length == 0)
            return;

        _planOverlay = new UIView
        {
            Frame = parent.Bounds,
            AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight,
            BackgroundColor = UIColor.Clear
        };
        _planOverlay.AddGestureRecognizer(new UITapGestureRecognizer(() =>
        {
            _ = EvalOnMainAsync("window.__novalistPlanDismiss && window.__novalistPlanDismiss()");
            HidePlanMenu();
        }));

        // The same Liquid Glass material the tab bar uses (falls back to a chrome
        // blur on any pre-glass runtime).
        UIVisualEffect effect;
        try { effect = new UIGlassEffect(); }
        catch { effect = UIBlurEffect.FromStyle(UIBlurEffectStyle.SystemChromeMaterial); }
        _planMenu = new UIVisualEffectView(effect) { ClipsToBounds = true };
        _planMenu.Layer.CornerRadius = 18;
        // Match the tab bar's dark Liquid Glass (the app is dark-only); otherwise
        // the glass renders in the light variant.
        _planMenu.OverrideUserInterfaceStyle = UIUserInterfaceStyle.Dark;

        var stack = new UIStackView
        {
            Axis = UILayoutConstraintAxis.Vertical,
            TranslatesAutoresizingMaskIntoConstraints = false
        };
        for (var i = 0; i < labels.Length; i++)
        {
            var idx = i;
            var btn = UIButton.FromType(UIButtonType.System);
            btn.SetTitle(labels[i], UIControlState.Normal);
            btn.SetTitleColor(UIColor.Label, UIControlState.Normal);
            btn.HorizontalAlignment = UIControlContentHorizontalAlignment.Left;
            btn.ContentEdgeInsets = new UIEdgeInsets(0, 16, 0, 16);
            if (btn.TitleLabel != null) btn.TitleLabel.Font = UIFont.SystemFontOfSize(17);
            btn.TouchUpInside += (_, _) =>
            {
                _ = EvalOnMainAsync($"window.__novalistPlanSelect && window.__novalistPlanSelect({idx})");
                HidePlanMenu();
            };
            btn.HeightAnchor.ConstraintEqualTo(48).Active = true;
            stack.AddArrangedSubview(btn);
        }
        _planMenu.ContentView.AddSubview(stack);
        NSLayoutConstraint.ActivateConstraints(new[]
        {
            stack.LeadingAnchor.ConstraintEqualTo(_planMenu.ContentView.LeadingAnchor),
            stack.TrailingAnchor.ConstraintEqualTo(_planMenu.ContentView.TrailingAnchor),
            stack.TopAnchor.ConstraintEqualTo(_planMenu.ContentView.TopAnchor, 6),
            stack.BottomAnchor.ConstraintEqualTo(_planMenu.ContentView.BottomAnchor, -6),
        });

        const double width = 240;
        var height = labels.Length * 48 + 12;
        var tabTop = _tabBar.Frame.Top;
        var centerX = PlanButtonCenterX(parent);
        var x = Math.Max(8, Math.Min(centerX - width / 2, parent.Bounds.Width - width - 8));
        _planMenu.Frame = new CGRect(x, tabTop - height - 8, width, height);

        parent.AddSubview(_planOverlay);
        parent.AddSubview(_planMenu);
    }

    private void HidePlanMenu()
    {
        _planMenu?.RemoveFromSuperview();
        _planMenu = null;
        _planOverlay?.RemoveFromSuperview();
        _planOverlay = null;
    }

    // Center-x (in parent coords) of the Plan tab item (index 3 of the 5 tabs),
    // so the menu anchors to it. Falls back to an even-spacing estimate.
    private double PlanButtonCenterX(UIView parent)
    {
        const int planIndex = 3;
        var buttons = new List<UIView>();
        void Scan(UIView v)
        {
            foreach (var s in v.Subviews)
            {
                if (s.GetType().Name.Contains("TabBarButton")) buttons.Add(s);
                Scan(s);
            }
        }
        Scan(_tabBar!);
        if (buttons.Count == Tabs.Length)
        {
            buttons.Sort((a, b) => a.Frame.X.CompareTo(b.Frame.X));
            return buttons[planIndex].ConvertRectToView(buttons[planIndex].Bounds, parent).GetMidX();
        }
        var bar = _tabBar!.ConvertRectToView(_tabBar.Bounds, parent);
        return bar.X + bar.Width * ((planIndex + 0.5) / Tabs.Length);
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

    // Absolute folder of the open project; set via setProjectRoot on project open.
    private string? _projectRoot;

    private string? ReadProjectImage(string relative)
    {
        if (string.IsNullOrEmpty(_projectRoot) || string.IsNullOrEmpty(relative)) return null;
        try
        {
            var rootFull = Path.GetFullPath(_projectRoot);
            var full = Path.GetFullPath(Path.Combine(rootFull, relative));
            // Never serve outside the project folder.
            if (!full.StartsWith(rootFull, StringComparison.Ordinal)) return null;
            if (!File.Exists(full)) return null;
            var bytes = File.ReadAllBytes(full);
            return $"data:{MimeForExtension(full)};base64,{Convert.ToBase64String(bytes)}";
        }
        catch
        {
            return null;
        }
    }

    private static string MimeForExtension(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };

    private async Task<object?> InvokeHostAsync(string method, JsonElement args)
    {
        switch (method)
        {
            case "pickFolder":
            {
                // Real external-folder picker: the iOS document picker returns a
                // security-scoped folder URL (e.g. an iCloud/Files/Working-Copy repo
                // folder). SecurityScopedFolders persists a bookmark and keeps the
                // scope open so the backend can read/write it. Null on cancel.
                return await SecurityScopedFolders.PickFolderAsync().ConfigureAwait(false);
            }
            case "beginProjectAccess":
                // Mirror the MAS contract: resolve the stored bookmark and start
                // access; false lets the renderer re-prompt for the folder.
                return SecurityScopedFolders.BeginAccess(ArgString(args, 0));
            case "endProjectAccess":
                SecurityScopedFolders.EndAccess(ArgString(args, 0));
                return null;
            case "setProjectRoot":
                // Track the open project's folder so project images (served on
                // desktop via the novalist-project:// scheme) can be read below.
                _projectRoot = ArgString(args, 0);
                return null;
            case "readProjectImage":
                // Read a project-relative image and return it as a data: URI. The
                // mobile build has no custom-scheme handler, so novalist-project://
                // <img> srcs are rewritten to call this (see mobile/projectImages).
                return ReadProjectImage(ArgString(args, 0));
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
            case "setPlanningMenuOpen":
            {
                // args[0]=open (bool), args[1]=localized labels (in Plan-menu order).
                // Rendered natively so it uses the same Liquid Glass as the tab bar
                // and can anchor to the Plan tab item. Selection/dismissal come back
                // via window.__novalistPlanSelect / __novalistPlanDismiss.
                var open = args.ValueKind == JsonValueKind.Array
                    && args.GetArrayLength() > 0
                    && args[0].ValueKind == JsonValueKind.True;
                var labels = new List<string>();
                if (open && args.GetArrayLength() > 1 && args[1].ValueKind == JsonValueKind.Array)
                    foreach (var el in args[1].EnumerateArray())
                        labels.Add(el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "");
#if IOS
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (open) ShowPlanMenu(labels.ToArray());
                    else HidePlanMenu();
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
