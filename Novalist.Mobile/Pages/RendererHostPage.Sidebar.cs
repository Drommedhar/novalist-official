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
    /// The recogniser belongs to the sidebar so horizontal gestures in the
    /// editor and planning views remain available to their own controls. The
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
        var pan = new UIPanGestureRecognizer(recognizer => HandleSidebarPan(recognizer, parent));
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
}
#endif
