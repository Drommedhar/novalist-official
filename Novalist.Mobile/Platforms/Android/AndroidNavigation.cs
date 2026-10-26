using System.Globalization;
using System.Text.Json;

namespace Novalist.Mobile.Pages;

public sealed partial class RendererHostPage
{
    private const int AndroidSidebarRailWidth = 64;
    private const int AndroidSidebarWidth = 240;
    private const int AndroidTabBarHeight = 64;
    private static readonly string[] AndroidTabs = ["dashboard", "manuscript", "codex", "planning", "settings"];
    private static readonly string[] AndroidDestinations =
        ["dashboard", "write", "manuscript", "timeline", "plotGrid", "calendar", "relationships", "codex", "wiki", "maps", "research", "gallery", "export", "settings"];
    private static readonly string[] AndroidDestinationTitles =
        ["Dashboard", "Write", "Manuscript", "Timeline", "Plot Grid", "Calendar", "Relationships", "Codex", "Wiki", "Maps", "Research", "Gallery", "Export", "Settings"];
    private readonly List<AndroidNavigationButton> _androidTabs = [];
    private readonly List<AndroidNavigationButton> _androidDestinations = [];
    private readonly List<BoxView> _androidSeparators = [];
    private Grid? _androidChrome;
    private Grid? _androidBottomBar;
    private AndroidNavigationScrollView? _androidSidebar;
    private Grid? _androidSidebarScrim;
    private Color _androidSurface = Color.FromArgb("#0a1420");
    private Color _androidForeground = Colors.White;
    private bool _androidNavVisible;
    private bool _androidTablet;
    private bool _androidSidebarCollapsed;
    private double _androidSidebarDragStartWidth;

    private View CreateAndroidChrome()
    {
        SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.All;
        _androidChrome = new Grid { BackgroundColor = _androidSurface };
        _androidChrome.Add(_web);
        _androidSidebarScrim = CreateAndroidScrim(() => SetAndroidSidebarCollapsed(true));
        _androidChrome.Add(_androidSidebarScrim);
        AddAndroidSidebar(_androidChrome);
        AddAndroidTabs(_androidChrome);
        _androidChrome.SizeChanged += OnAndroidSizeChanged;
        SelectAndroidButton(_androidTabs, 0);
        SelectAndroidButton(_androidDestinations, 0);
        return _androidChrome;
    }

    private void AddAndroidSidebar(Grid chrome)
    {
        var stack = new VerticalStackLayout { Spacing = 2, Padding = 8 };
        for (var i = 0; i < AndroidDestinations.Length; i++)
        {
            if (i is 3 or 7 or 12 or 13)
            {
                var separator = new BoxView
                {
                    HeightRequest = 1, Margin = new Thickness(0, 8),
                    Color = _androidForeground.WithAlpha(0.18f)
                };
                _androidSeparators.Add(separator);
                stack.Add(separator);
            }
            var key = AndroidDestinations[i];
            var button = CreateAndroidNavigationButton(key, AndroidDestinationTitles[i], key, false);
            _androidDestinations.Add(button);
            stack.Add(button);
        }
        _androidSidebar = new AndroidNavigationScrollView
        {
            Content = stack, IsVisible = false, WidthRequest = AndroidSidebarWidth,
            HorizontalOptions = LayoutOptions.Start, BackgroundColor = _androidSurface,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never, ZIndex = 2
        };
        _androidSidebar.DragStarted = BeginAndroidSidebarDrag;
        _androidSidebar.DragChanged = UpdateAndroidSidebarDrag;
        _androidSidebar.DragEnded = EndAndroidSidebarDrag;
        chrome.Add(_androidSidebar);
    }

    private void AddAndroidTabs(Grid chrome)
    {
        _androidBottomBar = new Grid
        {
            IsVisible = false, VerticalOptions = LayoutOptions.End, HeightRequest = AndroidTabBarHeight,
            Padding = new Thickness(4), ColumnSpacing = 2, BackgroundColor = _androidSurface, ZIndex = 4
        };
        string[] titles = ["Dashboard", "Write", "Codex", "Plan", "Settings"];
        for (var i = 0; i < AndroidTabs.Length; i++)
        {
            _androidBottomBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var key = AndroidTabs[i];
            var button = CreateAndroidNavigationButton(key, titles[i], key == "manuscript" ? "write" : key, true);
            _androidTabs.Add(button);
            _androidBottomBar.Add(button, i, 0);
        }
        _androidBottomBar.SizeChanged += OnAndroidSizeChanged;
        chrome.Add(_androidBottomBar);
    }

    private AndroidNavigationButton CreateAndroidNavigationButton(string key, string title, string icon, bool tab)
    {
        var button = new AndroidNavigationButton(title, AndroidIconPath(icon), tab, "navigate-" + key);
        button.SetForeground(_androidForeground);
        button.Clicked += async (_, _) =>
        {
            if (!tab) SetAndroidSidebarCollapsed(true);
            if (key != "planning") HideAndroidPlanMenu();
            await EvalOnMainAsync($"window.__novalistTab?.({JsonSerializer.Serialize(key)})");
        };
        return button;
    }

    private static Grid CreateAndroidScrim(Action dismiss)
    {
        var scrim = new Grid { BackgroundColor = Colors.Transparent, IsVisible = false, ZIndex = 1 };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => dismiss();
        scrim.GestureRecognizers.Add(tap);
        return scrim;
    }

    private void OnAndroidSizeChanged(object? sender, EventArgs args) => PushAndroidLayout();

    private void PushAndroidLayout()
    {
        if (_androidChrome == null || _androidChrome.Width <= 0) return;
        _androidTablet = _androidChrome.Width > 700;
        if (_androidTablet || !_androidNavVisible) DismissAndroidPlanMenu();
        if (_androidBottomBar != null) _androidBottomBar.IsVisible = _androidNavVisible && !_androidTablet;
        if (_androidSidebar != null)
        {
            _androidSidebar.IsVisible = _androidNavVisible && _androidTablet;
            _androidSidebar.WidthRequest = _androidSidebarCollapsed ? AndroidSidebarRailWidth : AndroidSidebarWidth;
        }
        foreach (var button in _androidDestinations) button.Compact = _androidSidebarCollapsed;
        if (_androidSidebarScrim != null)
            _androidSidebarScrim.IsVisible = _androidNavVisible && _androidTablet && !_androidSidebarCollapsed;
        PositionAndroidPlanMenu();
        var sidebarWidth = _androidNavVisible && _androidTablet ? AndroidSidebarRailWidth : 0;
        var tabHeight = _androidNavVisible && !_androidTablet
            ? Math.Max(AndroidTabBarHeight, _androidBottomBar?.Height ?? 0) : 0;
        // The web viewport stays full size; CSS reserves only the rail and the visible tab bar.
        _ = EvalOnMainAsync($"window.__novalistLayout?.('{(_androidTablet ? "tablet" : "phone")}');"
            + $"document.documentElement.style.setProperty('--nl-mobile-sidebar-w','{sidebarWidth}px');"
            + $"document.documentElement.style.setProperty('--nl-mobile-tabbar-h','{tabHeight.ToString(CultureInfo.InvariantCulture)}px')");
    }

    private void SetAndroidSidebarCollapsed(bool collapsed)
    {
        if (_androidSidebarCollapsed == collapsed) return;
        _androidSidebarCollapsed = collapsed;
        PushAndroidLayout();
        _ = EvalOnMainAsync($"window.__novalistSidebarCollapsed?.({(collapsed ? "true" : "false")})");
    }

    private void BeginAndroidSidebarDrag()
    {
        if (_androidSidebar != null) _androidSidebarDragStartWidth = _androidSidebar.WidthRequest;
    }

    private void UpdateAndroidSidebarDrag(double translation)
    {
        if (_androidSidebar == null || !_androidTablet || !_androidNavVisible) return;
        var width = Math.Clamp(_androidSidebarDragStartWidth + translation, AndroidSidebarRailWidth, AndroidSidebarWidth);
        _androidSidebar.WidthRequest = width;
        foreach (var button in _androidDestinations)
            button.Compact = width <= (AndroidSidebarRailWidth + AndroidSidebarWidth) / 2.0;
    }

    private void EndAndroidSidebarDrag(double velocity, bool cancelled)
    {
        if (_androidSidebar == null) return;
        var expanded = velocity > 250 || (velocity > -250
            && _androidSidebar.WidthRequest > (AndroidSidebarRailWidth + AndroidSidebarWidth) / 2.0);
        if (!cancelled && _androidSidebarCollapsed == expanded) SetAndroidSidebarCollapsed(!expanded);
        else PushAndroidLayout();
    }

    private static void SetAndroidTitles(IReadOnlyList<AndroidNavigationButton> buttons, IReadOnlyList<string> titles)
    {
        for (var i = 0; i < Math.Min(buttons.Count, titles.Count); i++) buttons[i].Title = titles[i];
    }

    private static void SelectAndroidButton(IReadOnlyList<AndroidNavigationButton> buttons, int index)
    {
        for (var i = 0; i < buttons.Count; i++) buttons[i].Selected = i == index;
    }

    private void DisposeAndroidChrome()
    {
        if (_androidChrome != null) _androidChrome.SizeChanged -= OnAndroidSizeChanged;
        if (_androidBottomBar != null) _androidBottomBar.SizeChanged -= OnAndroidSizeChanged;
        if (_androidSidebar != null)
        {
            _androidSidebar.DragStarted = null;
            _androidSidebar.DragChanged = null;
            _androidSidebar.DragEnded = null;
        }
        HideAndroidPlanMenu();
    }
}
