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
            // The web layout never scrolls sideways (the single-pane shell scrolls
            // vertically only), so refuse horizontal drags outright rather than let
            // the page be pulled left and right into empty space.
            wk.ScrollView.AlwaysBounceHorizontal = false;
            wk.ScrollView.ShowsHorizontalScrollIndicator = false;
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

    // iPad (regular horizontal size class) replaces the compact bottom tab bar
    // with a leading Liquid Glass sidebar. It carries the full desktop
    // destination set minus Git (no `git` binary in the iOS sandbox), grouped
    // exactly like the desktop activity bar (shellStore.activityGroups).
    //
    // Keys are the shellStore MainView values and go straight back to the web
    // through window.__novalistTab. English titles are only the pre-localization
    // fallback; the web pushes localized ones (in this order) via
    // setSidebarTitles. Group starts a new section above the row.
    private static readonly (string Key, string Title, string Symbol, bool Group)[] SidebarItems =
    {
        ("dashboard", "Dashboard", "square.grid.2x2", false),
        ("write", "Write", "square.and.pencil", false),
        ("manuscript", "Manuscript", "rectangle.split.1x2", false),
        ("timeline", "Timeline", "chart.bar.xaxis", true),
        ("plotGrid", "Plot Grid", "tablecells", false),
        ("calendar", "Calendar", "calendar", false),
        ("relationships", "Relationships", "point.3.connected.trianglepath.dotted", false),
        ("codex", "Codex", "person.2", true),
        ("wiki", "Wiki", "newspaper", false),
        ("maps", "Maps", "map", false),
        ("research", "Research", "doc.text", false),
        ("gallery", "Gallery", "photo.on.rectangle", false),
        ("export", "Export", "paperplane", true),
        ("settings", "Settings", "gearshape", true),
    };

    // Expanded shows icon + label; collapsed keeps the same Liquid Glass panel but
    // narrows it to an icon-only rail, so every destination stays one tap away
    // while the text column gets the difference back. Portrait on any iPad is the
    // case that needs it - sidebar + binder + editor do not all fit comfortably.
    private const int SidebarWidth = 240;
    private const int SidebarRailWidth = 64;

    private UIVisualEffectView? _sidebar;
    private NSLayoutConstraint? _sidebarWidth;
    private UIView? _sidebarScrim;
    private UIScrollView? _sidebarScroll;
    private bool _sidebarCollapsed;
    private readonly List<SidebarRow> _sidebarRows = new();
    private string[]? _sidebarTitles;
    private LayoutProbe? _probe;
    // Whether the last applied layout was the regular-width (iPad) one. Null until
    // the first size-class pass, so the first pass always pushes.
    private bool? _isRegularWidth;
    // Set false on the welcome screen (setNavVisible) - no chrome until a project
    // is open, in either size class.
    private bool _navVisible = true;

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler?.PlatformView is not UIView native) return;
        if (_tabBar == null) AddNativeTabBar(native);
        if (_sidebar == null) AddNativeSidebar(native);
        if (_probe == null) AddLayoutProbe(native);
    }

    // ---- Size-class adaptation (iPad sidebar <-> iPhone tab bar) -------------

    /// <summary>
    /// Invisible full-bleed view whose LayoutSubviews fires on every parent
    /// resize - rotation, Split View / Stage Manager drags, and the first
    /// appearance. Cheaper and less brittle than the deprecated
    /// TraitCollectionDidChange override, and it catches plain rotations (which
    /// keep the size class) as well as size-class flips.
    /// </summary>
    private sealed class LayoutProbe : UIView
    {
        public Action? LayoutChanged;

        public override void LayoutSubviews()
        {
            base.LayoutSubviews();
            LayoutChanged?.Invoke();
        }
    }

    private void AddLayoutProbe(UIView parent)
    {
        _probe = new LayoutProbe
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            BackgroundColor = UIColor.Clear,
            // Never swallow a touch meant for the web content underneath.
            UserInteractionEnabled = false
        };
        _probe.LayoutChanged = ApplySizeClass;
        parent.AddSubview(_probe);
        NSLayoutConstraint.ActivateConstraints(new[]
        {
            _probe.LeadingAnchor.ConstraintEqualTo(parent.LeadingAnchor),
            _probe.TrailingAnchor.ConstraintEqualTo(parent.TrailingAnchor),
            _probe.TopAnchor.ConstraintEqualTo(parent.TopAnchor),
            _probe.BottomAnchor.ConstraintEqualTo(parent.BottomAnchor),
        });
    }

    /// <summary>
    /// Picks the chrome for the current horizontal size class and tells the web
    /// which layout it is in. Regular (iPad full screen, and half-screen Split
    /// View on the large iPads) gets the leading sidebar; compact (iPhone, and a
    /// narrow Split View / Slide Over window) falls back to the bottom tab bar,
    /// so a resized iPad window collapses to the phone layout automatically.
    /// </summary>
    private void ApplySizeClass()
    {
        if (Handler?.PlatformView is not UIView parent) return;
        var regular = parent.TraitCollection.HorizontalSizeClass == UIUserInterfaceSizeClass.Regular;
        var changed = _isRegularWidth != regular;
        _isRegularWidth = regular;

        if (_sidebar != null) _sidebar.Hidden = !regular || !_navVisible;
        if (_tabBar != null) _tabBar.Hidden = regular || !_navVisible;
        // The Plan popover belongs to the compact tab bar; the sidebar lists the
        // planning modes directly, so it must not survive a rotation into regular.
        if (regular) HidePlanMenu();

        if (changed)
        {
            PushChromeMetrics();
            _ = EvalOnMainAsync(
                $"window.__novalistLayout && window.__novalistLayout('{(regular ? "tablet" : "phone")}')");
        }
        else
        {
            // Same size class, but the frame may still have moved (rotation).
            PushTabBarMetrics();
        }
    }

    // Push both chrome insets at once so the web never insets for chrome that is
    // not showing. The tab-bar height is re-measured by PushTabBarMetrics once
    // the bar has laid out.
    private void PushChromeMetrics()
    {
        var regular = _isRegularWidth == true;
        // The web only ever reserves the RAIL width. Expanding the sidebar slides
        // it OVER the content rather than reflowing the layout, so the glass
        // refracts the manuscript underneath instead of a flat background - which
        // is the whole reason to use the material. The rail stays reserved so no
        // content is permanently hidden behind it.
        var sidebar = regular && _navVisible ? SidebarRailWidth : 0;
        var w = sidebar.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _lastPushedTabH = -1;      // force the next tab-bar measurement through
        var js = $"document.documentElement.style.setProperty('--nl-mobile-sidebar-w','{w}px')";
        if (regular)
            js += ";document.documentElement.style.setProperty('--nl-mobile-tabbar-h','0px')";
        _ = EvalOnMainAsync(js);
        if (!regular) PushTabBarMetrics();
    }

    private void AddNativeSidebar(UIView parent)
    {
        // Same Liquid Glass material as the tab bar and the Plan popover (falls
        // back to a chrome blur on any pre-glass runtime).
        UIVisualEffect effect;
        try { effect = new UIGlassEffect(); }
        catch { effect = UIBlurEffect.FromStyle(UIBlurEffectStyle.SystemChromeMaterial); }
        _sidebar = new UIVisualEffectView(effect)
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            Hidden = true
        };
        // The app is dark-only; without this the glass renders in its light variant.
        _sidebar.OverrideUserInterfaceStyle = UIUserInterfaceStyle.Dark;

        var stack = new UIStackView
        {
            Axis = UILayoutConstraintAxis.Vertical,
            Spacing = 2,
            TranslatesAutoresizingMaskIntoConstraints = false
        };

        _sidebarRows.Clear();
        for (var i = 0; i < SidebarItems.Length; i++)
        {
            var item = SidebarItems[i];
            if (item.Group && i > 0)
            {
                var sep = new UIView { BackgroundColor = UIColor.Label.ColorWithAlpha(0.18f) };
                sep.HeightAnchor.ConstraintEqualTo(1).Active = true;
                var pad = new UIView();
                pad.HeightAnchor.ConstraintEqualTo(8).Active = true;
                stack.AddArrangedSubview(pad);
                stack.AddArrangedSubview(sep);
                var pad2 = new UIView();
                pad2.HeightAnchor.ConstraintEqualTo(8).Active = true;
                stack.AddArrangedSubview(pad2);
            }
            var key = item.Key;
            var row = new SidebarRow(item.Symbol, item.Title);
            row.TouchUpInside += (_, _) => OnSidebarSelected(key);
            _sidebarRows.Add(row);
            stack.AddArrangedSubview(row);
        }

        // Scrollable: 14 destinations plus separators exceed the short side of an
        // iPad mini in landscape.
        var scroll = new UIScrollView { TranslatesAutoresizingMaskIntoConstraints = false };
        _sidebarScroll = scroll;
        scroll.AddSubview(stack);
        _sidebar.ContentView.AddSubview(scroll);

        parent.AddSubview(_sidebar);
        _sidebarWidth = _sidebar.WidthAnchor.ConstraintEqualTo(CurrentSidebarWidth);
        _sidebarWidth.Active = true;
        NSLayoutConstraint.ActivateConstraints(new[]
        {
            _sidebar.LeadingAnchor.ConstraintEqualTo(parent.LeadingAnchor),
            _sidebar.TopAnchor.ConstraintEqualTo(parent.TopAnchor),
            _sidebar.BottomAnchor.ConstraintEqualTo(parent.BottomAnchor),

            // Inset the list by the safe area so it clears the status bar and the
            // home indicator; the glass itself still runs edge to edge.
            scroll.LeadingAnchor.ConstraintEqualTo(_sidebar.ContentView.LeadingAnchor),
            scroll.TrailingAnchor.ConstraintEqualTo(_sidebar.ContentView.TrailingAnchor),
            scroll.TopAnchor.ConstraintEqualTo(parent.SafeAreaLayoutGuide.TopAnchor, 8),
            scroll.BottomAnchor.ConstraintEqualTo(parent.SafeAreaLayoutGuide.BottomAnchor, -8),

            stack.LeadingAnchor.ConstraintEqualTo(scroll.ContentLayoutGuide.LeadingAnchor, 8),
            stack.TrailingAnchor.ConstraintEqualTo(scroll.ContentLayoutGuide.TrailingAnchor, -8),
            stack.TopAnchor.ConstraintEqualTo(scroll.ContentLayoutGuide.TopAnchor),
            stack.BottomAnchor.ConstraintEqualTo(scroll.ContentLayoutGuide.BottomAnchor),
            stack.WidthAnchor.ConstraintEqualTo(scroll.FrameLayoutGuide.WidthAnchor, 1, -16),
        });

        AddSidebarPan(parent);
        ApplySidebarTitles();
        // Set the initial presentation WITHOUT forcing a layout pass: this runs
        // inside OnHandlerChanged, and driving parent.LayoutIfNeeded() there
        // laid the (not yet sized) UITabBar out early and left its items
        // permanently compressed to "Da..." instead of "Dashboard".
        ApplySidebarCollapsed(animated: false);
        SelectSidebarKey("dashboard");
    }

    private int CurrentSidebarWidth => _sidebarCollapsed ? SidebarRailWidth : SidebarWidth;

    /// <summary>
    /// Animate between the labelled sidebar and the icon-only rail. The glass panel
    /// is the same view either way - only its width and the rows' labels change -
    /// so the material and the selection survive the transition.
    /// </summary>
    private void ApplySidebarCollapsed(bool animated = true)
    {
        foreach (var row in _sidebarRows) row.Compact = _sidebarCollapsed;
        if (_sidebarWidth != null) _sidebarWidth.Constant = CurrentSidebarWidth;
        UpdateSidebarScrim();
        // Only the user-driven toggle animates. At construction time the layout
        // must be left to UIKit's own first pass (see AddNativeSidebar).
        if (animated && Handler?.PlatformView is UIView parent)
            UIView.Animate(0.22, () => parent.LayoutIfNeeded());
        PushChromeMetrics();
    }

    /// <summary>
    /// While the sidebar is expanded it floats over the content, so a tap outside
    /// it must put it away - otherwise the expanded panel sits on top of the view
    /// with no obvious way back. Transparent: dimming the content would defeat the
    /// refraction the glass exists for.
    /// </summary>
    private void UpdateSidebarScrim()
    {
        var wanted = !_sidebarCollapsed && _navVisible && _isRegularWidth == true;
        if (!wanted)
        {
            _sidebarScrim?.RemoveFromSuperview();
            _sidebarScrim = null;
            return;
        }
        if (_sidebarScrim != null) return;
        if (Handler?.PlatformView is not UIView parent || _sidebar == null) return;

        var scrim = new UIView
        {
            BackgroundColor = UIColor.Clear,
            TranslatesAutoresizingMaskIntoConstraints = false
        };
        scrim.AddGestureRecognizer(new UITapGestureRecognizer(() => SetSidebarCollapsed(true)));
        // Below the sidebar so taps on the sidebar itself still reach its rows.
        parent.InsertSubviewBelow(scrim, _sidebar);
        NSLayoutConstraint.ActivateConstraints(new[]
        {
            scrim.LeadingAnchor.ConstraintEqualTo(parent.LeadingAnchor),
            scrim.TrailingAnchor.ConstraintEqualTo(parent.TrailingAnchor),
            scrim.TopAnchor.ConstraintEqualTo(parent.TopAnchor),
            scrim.BottomAnchor.ConstraintEqualTo(parent.BottomAnchor),
        });
        _sidebarScrim = scrim;
    }

    /// <summary>Collapse/expand from the native side, keeping the web's toggle in step.</summary>
    private void SetSidebarCollapsed(bool collapsed)
    {
        if (_sidebarCollapsed == collapsed) return;
        _sidebarCollapsed = collapsed;
        ApplySidebarCollapsed();
        NotifyWebSidebarCollapsed();
    }

    private void NotifyWebSidebarCollapsed() =>
        _ = EvalOnMainAsync(
            "window.__novalistSidebarCollapsed && window.__novalistSidebarCollapsed("
            + (_sidebarCollapsed ? "true" : "false") + ")");

    // ---- Interactive edge drag (rail <-> expanded) ---------------------------

    private double _panStartWidth;

    /// <summary>
    /// Drag the sidebar out from the leading edge, and back. The width is a single
    /// constraint, so the pan maps straight onto it and the panel tracks the
    /// finger instead of snapping.
    ///
    /// Deliberately a SCREEN EDGE recogniser: several views scroll horizontally
    /// (codex tabs, timeline toolbar) and the editor is contenteditable where a
    /// horizontal drag selects text - an edge gesture never sees any of those. The
    /// trailing edge is left alone because iPadOS uses it for Slide Over.
    /// </summary>
    private void AddSidebarPan(UIView parent)
    {
        // Attached to the SIDEBAR, not the screen edge. A screen-edge recogniser
        // on the parent never received the touch: the sidebar and the web view sit
        // above it and claimed the gesture first. Hanging the recogniser on the
        // panel itself means the drag starts on the view it moves, so nothing can
        // intercept it - and grabbing the rail is what people reach for anyway.
        if (_sidebar == null) return;
        UIPanGestureRecognizer pan = null!;
        pan = new UIPanGestureRecognizer(() => HandleSidebarPan(pan, parent));
        // Only claim horizontal drags, so a vertical swipe still scrolls the
        // destination list.
        pan.ShouldBegin = recognizer =>
        {
            if (recognizer is not UIPanGestureRecognizer p) return false;
            var v = p.VelocityInView(parent);
            return Math.Abs((double)v.X) > Math.Abs((double)v.Y);
        };
        _sidebar.AddGestureRecognizer(pan);
        // The list would otherwise swallow the drag before we see it.
        _sidebarScroll?.PanGestureRecognizer.RequireGestureRecognizerToFail(pan);
    }

    private void HandleSidebarPan(UIPanGestureRecognizer pan, UIView parent)
    {
        // Phone layout and the welcome screen have no sidebar to drag.
        if (_sidebarWidth == null || _isRegularWidth != true || !_navVisible) return;

        switch (pan.State)
        {
            case UIGestureRecognizerState.Began:
                _panStartWidth = (double)_sidebarWidth.Constant;
                break;

            case UIGestureRecognizerState.Changed:
            {
                var width = _panStartWidth + (double)pan.TranslationInView(parent).X;
                width = Math.Clamp(width, SidebarRailWidth, SidebarWidth);
                _sidebarWidth.Constant = (System.Runtime.InteropServices.NFloat)width;
                // Labels appear past the halfway point so the rail does not show
                // clipped text mid-drag.
                var showLabels = width > (SidebarRailWidth + SidebarWidth) / 2.0;
                foreach (var row in _sidebarRows) row.Compact = !showLabels;
                parent.LayoutIfNeeded();
                break;
            }

            case UIGestureRecognizerState.Ended:
            case UIGestureRecognizerState.Cancelled:
            {
                var width = (double)_sidebarWidth.Constant;
                var velocity = (double)pan.VelocityInView(parent).X;
                // A decisive flick wins over position, so a short fast drag still
                // completes; otherwise settle to whichever end is nearer.
                var expand = velocity > 250
                    || (velocity > -250 && width > (SidebarRailWidth + SidebarWidth) / 2.0);
                var changed = _sidebarCollapsed == expand;
                _sidebarCollapsed = !expand;
                ApplySidebarCollapsed();
                if (changed) NotifyWebSidebarCollapsed();
                break;
            }
        }
    }

    private void OnSidebarSelected(string key)
    {
        SelectSidebarKey(key);
        _ = EvalOnMainAsync($"window.__novalistTab && window.__novalistTab('{key}')");
        // Overlay semantics: picking a destination puts the sidebar away, or the
        // expanded panel and its tap-catcher would sit on top of the very view
        // that was just chosen.
        SetSidebarCollapsed(true);
    }

    // Highlight the row for a destination key (no-op for keys not in the list).
    private void SelectSidebarKey(string key)
    {
        for (var i = 0; i < _sidebarRows.Count && i < SidebarItems.Length; i++)
            _sidebarRows[i].Selected = SidebarItems[i].Key == key;
    }

    private void ApplySidebarTitles()
    {
        if (_sidebarTitles == null) return;
        var count = Math.Min(_sidebarRows.Count, _sidebarTitles.Length);
        for (var i = 0; i < count; i++)
            _sidebarRows[i].Title = _sidebarTitles[i];
    }

    /// <summary>
    /// One sidebar destination: an SF Symbol plus a label in a rounded, tappable
    /// row. A plain UIControl rather than a configured UIButton so the selected
    /// background and the 44pt touch target are explicit.
    /// </summary>
    private sealed class SidebarRow : UIControl
    {
        private readonly UILabel _label;
        // Swapped when collapsing: the icon moves from "leading, label beside it"
        // to "centred, no label". The label's own constraints are deactivated in
        // the rail - left active they would demand a negative width at 64pt.
        private readonly NSLayoutConstraint _iconLeading;
        private readonly NSLayoutConstraint _iconCentre;
        private readonly NSLayoutConstraint[] _labelConstraints;

        public SidebarRow(string symbol, string title)
        {
            TranslatesAutoresizingMaskIntoConstraints = false;
            Layer.CornerRadius = 10;

            var icon = new UIImageView(UIImage.GetSystemImage(symbol))
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
                ContentMode = UIViewContentMode.ScaleAspectFit,
                TintColor = UIColor.Label
            };
            _label = new UILabel
            {
                Text = title,
                TranslatesAutoresizingMaskIntoConstraints = false,
                Font = UIFont.SystemFontOfSize(16),
                TextColor = UIColor.Label,
                LineBreakMode = UILineBreakMode.TailTruncation
            };
            // The row handles the tap; the children must not intercept it.
            icon.UserInteractionEnabled = false;
            _label.UserInteractionEnabled = false;
            AddSubview(icon);
            AddSubview(_label);

            _iconLeading = icon.LeadingAnchor.ConstraintEqualTo(LeadingAnchor, 12);
            _iconCentre = icon.CenterXAnchor.ConstraintEqualTo(CenterXAnchor);
            _labelConstraints = new[]
            {
                _label.LeadingAnchor.ConstraintEqualTo(icon.TrailingAnchor, 12),
                _label.TrailingAnchor.ConstraintEqualTo(TrailingAnchor, -12),
            };

            NSLayoutConstraint.ActivateConstraints(new[]
            {
                HeightAnchor.ConstraintEqualTo(44),
                icon.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
                icon.WidthAnchor.ConstraintEqualTo(22),
                icon.HeightAnchor.ConstraintEqualTo(22),
                _label.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
            });
            _iconLeading.Active = true;
            NSLayoutConstraint.ActivateConstraints(_labelConstraints);
        }

        public string Title
        {
            set => _label.Text = value;
        }

        /// <summary>Icon-only rail presentation (no label, icon centred).</summary>
        public bool Compact
        {
            set
            {
                _label.Hidden = value;
                _iconLeading.Active = !value;
                NSLayoutConstraint.DeactivateConstraints(_labelConstraints);
                _iconCentre.Active = value;
                if (!value) NSLayoutConstraint.ActivateConstraints(_labelConstraints);
            }
        }

        public bool Selected
        {
            set => BackgroundColor = value
                ? UIColor.Label.ColorWithAlpha(0.16f)
                : UIColor.Clear;
        }
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
        _committedItem = items[0];
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

    // The tab item that maps to the view currently shown. Tapping Plan only opens
    // the menu (it does not switch the view), so on dismiss the highlight reverts
    // to this rather than sticking on Plan.
    private UITabBarItem? _committedItem;

    private void OnNativeTabSelected(int tag)
    {
        if (tag < 0 || tag >= Tabs.Length) return;
        var key = Tabs[tag].Key;
        if (key != "planning")
        {
            // A real view tab: it becomes the committed selection.
            _committedItem = _tabBar?.SelectedItem;
            HidePlanMenu();
        }
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
        // In regular width the bar is hidden and the sidebar owns the inset;
        // PushChromeMetrics already pinned the height to 0.
        if (_tabBar == null || _isRegularWidth == true) return;
        // A hidden bar keeps its frame, so measuring it on the welcome screen
        // reserved a strip of empty page for chrome that is not on screen. What
        // the web insets for is what it can see.
        var h = _navVisible ? Math.Round(_tabBar.Frame.Height) : 0;
        if (Math.Abs(h - _lastPushedTabH) < 0.5) return;             // unchanged layout
        if (h <= 0 && _navVisible) return;                           // not laid out yet
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

        // The tap-catcher covers only the area ABOVE the tab bar, so tapping another
        // tab still switches (and taps here dismiss the menu).
        var tabTop = _tabBar.Frame.Top;
        _planOverlay = new UIView
        {
            Frame = new CGRect(0, 0, parent.Bounds.Width, tabTop),
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
                // The last item (Find & Replace) is a dialog, not a view, so it
                // leaves the committed tab as-is; a planning mode commits to Plan.
                if (idx < labels.Length - 1 && _tabBar?.Items is { Length: > 3 } tabItems)
                    _committedItem = tabItems[3];
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
        var centerX = PlanButtonCenterX(parent);
        var x = Math.Max(8, Math.Min(centerX - width / 2, parent.Bounds.Width - width - 8));
        _planMenu.Frame = new CGRect(x, tabTop - height - 8, width, height);

        parent.AddSubview(_planOverlay);
        parent.AddSubview(_planMenu);
    }

    private void HidePlanMenu()
    {
        var wasOpen = _planMenu != null;
        _planMenu?.RemoveFromSuperview();
        _planMenu = null;
        _planOverlay?.RemoveFromSuperview();
        _planOverlay = null;
        // Revert the highlight to the view actually shown (Plan doesn't stick just
        // for opening the menu). Selecting a planning mode set _committedItem=Plan.
        if (wasOpen && _tabBar != null && _committedItem != null)
            _tabBar.SelectedItem = _committedItem;
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

    private static List<string> ArgStrings(JsonElement args, int index)
    {
        var list = new List<string>();
        if (args.ValueKind != JsonValueKind.Array || index >= args.GetArrayLength()) return list;
        var el = args[index];
        if (el.ValueKind != JsonValueKind.Array) return list;
        foreach (var item in el.EnumerateArray())
            list.Add(item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "");
        return list;
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
                // Images get the photo-library / Files choice (ImagePicking): the
                // document picker alone cannot see the camera roll. args[2] carries
                // the localized sheet labels (photos, files, cancel), in that order.
                if (ArgString(args, 1) == "images")
                {
                    var labels = ArgStrings(args, 2);
                    return await ImagePicking.PickImageAsync(
                        ArgString(args, 0),
                        labels.ElementAtOrDefault(0) ?? "Photo Library",
                        labels.ElementAtOrDefault(1) ?? "Browse Files",
                        labels.ElementAtOrDefault(2) ?? "Cancel").ConfigureAwait(false);
                }
                var options = new PickOptions { PickerTitle = ArgString(args, 0) };
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
                // Show the native Liquid Glass navigation only inside a project
                // (hidden on the welcome/start screen). Whichever chrome the
                // current size class uses - bottom tab bar or leading sidebar -
                // follows this flag, and the web's inset follows with it.
                var visible = args.ValueKind == JsonValueKind.Array
                    && args.GetArrayLength() > 0
                    && args[0].ValueKind == JsonValueKind.True;
#if IOS
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    _navVisible = visible;
                    var regular = _isRegularWidth == true;
                    if (_tabBar != null) _tabBar.Hidden = regular || !visible;
                    if (_sidebar != null) _sidebar.Hidden = !regular || !visible;
                    PushChromeMetrics();
                });
#endif
                return null;
            }
            case "setSidebarTitles":
            {
                // args[0] = localized titles in SidebarItems order. Same contract as
                // setTabTitles, for the iPad sidebar.
                var titles = new List<string>();
                if (args.ValueKind == JsonValueKind.Array && args.GetArrayLength() > 0
                    && args[0].ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in args[0].EnumerateArray())
                        titles.Add(el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "");
                }
#if IOS
                _sidebarTitles = titles.ToArray();
                await MainThread.InvokeOnMainThreadAsync(ApplySidebarTitles);
#endif
                return null;
            }
            case "setSidebarCollapsed":
            {
                // Collapse the iPad sidebar to an icon-only rail (or expand it).
                // Driven from the web so the toggle lives with the other pane
                // controls in the tablet top bar.
                var collapsed = args.ValueKind == JsonValueKind.Array
                    && args.GetArrayLength() > 0
                    && args[0].ValueKind == JsonValueKind.True;
#if IOS
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (_sidebarCollapsed == collapsed) return;
                    _sidebarCollapsed = collapsed;
                    ApplySidebarCollapsed();
                });
#endif
                return null;
            }
            case "setSidebarSelection":
            {
                // Keep the sidebar highlight on the destination the web actually
                // shows (e.g. opening a scene from the binder switches to Write).
                var key = ArgString(args, 0);
#if IOS
                await MainThread.InvokeOnMainThreadAsync(() => SelectSidebarKey(key));
#endif
                return null;
            }
            case "requestLayout":
            {
                // The web asks which layout it is in on mount. The size-class pass
                // may have run before the bundle finished loading, so re-push it
                // unconditionally rather than only on change.
#if IOS
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    _isRegularWidth = null;      // force ApplySizeClass to re-push
                    ApplySizeClass();
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
            case "setSelectedTab":
            {
                // The bar highlights whatever was tapped. A tab the web switched
                // to on its own (the first-run tour walks them) never was, so it
                // has to be told, or the highlight names one tab while the screen
                // shows another.
                var index = args.ValueKind == JsonValueKind.Array
                    && args.GetArrayLength() > 0
                    && args[0].ValueKind == JsonValueKind.Number
                    ? args[0].GetInt32()
                    : -1;
#if IOS
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (_tabBar?.Items is not { } items) return;
                    if (index < 0 || index >= items.Length) return;
                    _tabBar.SelectedItem = items[index];
                    _committedItem = items[index];
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
#if IOS
        // The probe outlives the page otherwise and would keep calling back.
        if (_probe != null) _probe.LayoutChanged = null;
#endif
        _web.RawMessageReceived -= OnRawMessageReceived;
        _host.Dispose();
        _bridge.Dispose();
        _cts.Dispose();
    }
}
