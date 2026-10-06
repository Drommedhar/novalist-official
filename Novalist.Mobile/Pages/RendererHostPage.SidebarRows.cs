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
        foreach (var (row, item) in _sidebarRows.Zip(SidebarItems))
            row.Selected = item.Key == key;
    }

    private void ApplySidebarTitles()
    {
        if (_sidebarTitles == null) return;
        foreach (var (row, title) in _sidebarRows.Zip(_sidebarTitles))
            row.Title = title;
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

        public override bool Selected
        {
            get => base.Selected;
            set
            {
                base.Selected = value;
                BackgroundColor = value
                    ? UIColor.Label.ColorWithAlpha(0.16f)
                    : UIColor.Clear;
            }
        }
    }
}
#endif
