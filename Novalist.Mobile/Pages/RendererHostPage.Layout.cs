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
        UpdateSidebarScrim();

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
}
#endif
