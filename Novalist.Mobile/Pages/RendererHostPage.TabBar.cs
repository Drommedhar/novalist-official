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
using UniformTypeIdentifiers;
using WebKit;
#endif

namespace Novalist.Mobile.Pages;

#if IOS
public sealed partial class RendererHostPage
{
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
        ("planning", "Plan", "square.stack.3d.up"),
        ("settings", "Settings", "gearshape"),
    };

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
        var centerX = PlanButtonCenterX(parent, _tabBar);
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
    private static double PlanButtonCenterX(UIView parent, UITabBar tabBar)
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
        Scan(tabBar);
        if (buttons.Count == Tabs.Length)
        {
            buttons.Sort((a, b) => a.Frame.X.CompareTo(b.Frame.X));
            return buttons[planIndex].ConvertRectToView(buttons[planIndex].Bounds, parent).GetMidX();
        }
        var bar = tabBar.ConvertRectToView(tabBar.Bounds, parent);
        return bar.X + bar.Width * ((planIndex + 0.5) / Tabs.Length);
    }
}
#endif
