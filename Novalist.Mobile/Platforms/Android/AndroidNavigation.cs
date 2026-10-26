using System.Text.Json;

namespace Novalist.Mobile.Pages;

public sealed partial class RendererHostPage
{
    private static readonly string[] AndroidTabs = ["dashboard", "manuscript", "codex", "planning", "settings"];
    private static readonly string[] AndroidDestinations =
        ["dashboard", "write", "manuscript", "timeline", "plotGrid", "calendar", "relationships", "codex", "wiki", "maps", "research", "gallery", "export", "settings"];
    private readonly List<Button> _androidTabs = [];
    private readonly List<Button> _androidDestinations = [];
    private Grid? _androidChrome;
    private Grid? _androidBottomBar;
    private ScrollView? _androidSidebar;
    private bool _androidNavVisible;
    private bool _androidTablet;
    private bool _androidSidebarCollapsed;
    private bool _androidMenuOpen;

    private void SetAndroidColors(string background, string foreground)
    {
        if (!Color.TryParse(background, out var surface) || !Color.TryParse(foreground, out var text)) return;
        BackgroundColor = surface;
        if (_androidChrome != null) _androidChrome.BackgroundColor = surface;
        if (_androidSidebar != null) _androidSidebar.BackgroundColor = surface;
        foreach (var button in _androidTabs.Concat(_androidDestinations))
        {
            button.BackgroundColor = surface;
            button.TextColor = text;
        }
        if (Platform.CurrentActivity?.Window is { } window)
        {
            var insets = AndroidX.Core.View.WindowCompat.GetInsetsController(window, window.DecorView);
            if (insets == null) return;
            var light = surface.Red * 0.2126 + surface.Green * 0.7152 + surface.Blue * 0.0722 > 0.5;
            insets.AppearanceLightStatusBars = light;
            insets.AppearanceLightNavigationBars = light;
        }
    }

    private View CreateAndroidChrome()
    {
        _androidChrome = new Grid
        {
            RowDefinitions = [new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto)],
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)]
        };
        _androidBottomBar = new Grid { IsVisible = false };
        for (var i = 0; i < AndroidTabs.Length; i++)
        {
            _androidBottomBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var button = NavigationButton(AndroidTabs[i]);
            button.FontSize = 11;
            _androidTabs.Add(button);
            _androidBottomBar.Add(button, i, 0);
        }
        var sidebar = new VerticalStackLayout();
        foreach (var key in AndroidDestinations)
        {
            var button = NavigationButton(key);
            _androidDestinations.Add(button);
            sidebar.Add(button);
        }
        _androidSidebar = new ScrollView { Content = sidebar, IsVisible = false, WidthRequest = 160 };
        _androidChrome.Add(_androidSidebar, 0, 0);
        Grid.SetRowSpan(_androidSidebar, 2);
        _androidChrome.Add(_web, 1, 0);
        _androidChrome.Add(_androidBottomBar, 1, 1);
        _androidChrome.SizeChanged += OnAndroidSizeChanged;
        return _androidChrome;
    }

    private Button NavigationButton(string key)
    {
        var button = new Button
        {
            Text = key, AutomationId = "navigate-" + key, MinimumWidthRequest = 0,
            MinimumHeightRequest = 48, CornerRadius = 0, Padding = new Thickness(2, 12)
        };
        button.Clicked += async (_, _) => await EvalOnMainAsync($"window.__novalistTab?.({JsonSerializer.Serialize(key)})");
        return button;
    }

    private void OnAndroidSizeChanged(object? sender, EventArgs args) => PushAndroidLayout();

    protected override bool OnBackButtonPressed()
    {
        if (!_rendererReady.Task.IsCompletedSuccessfully) return base.OnBackButtonPressed();
        _ = HandleAndroidBackAsync();
        return true;
    }

    private async Task HandleAndroidBackAsync()
    {
        try
        {
            var result = await EvaluateScriptAsync("window.__novalistBack?.() ?? false");
            if (result?.Trim('"') == "false") Platform.CurrentActivity?.MoveTaskToBack(true);
        }
        catch (Exception error) { await FailBridgeAsync(error); }
    }

    private void PushAndroidLayout()
    {
        if (_androidChrome == null || _androidChrome.Width <= 0) return;
        _androidTablet = _androidChrome.Width > 700;
        if (_androidBottomBar != null) _androidBottomBar.IsVisible = _androidNavVisible && !_androidTablet;
        if (_androidSidebar != null)
        {
            _androidSidebar.IsVisible = _androidNavVisible && _androidTablet;
            _androidSidebar.WidthRequest = _androidSidebarCollapsed ? 110 : 160;
        }
        _ = EvalOnMainAsync($"window.__novalistLayout?.('{(_androidTablet ? "tablet" : "phone")}');"
            + "document.documentElement.style.setProperty('--nl-mobile-sidebar-w','0px');"
            + "document.documentElement.style.setProperty('--nl-mobile-tabbar-h','0px')");
    }

    private static void SetAndroidTitles(IReadOnlyList<Button> buttons, IReadOnlyList<string> titles)
    {
        for (var i = 0; i < Math.Min(buttons.Count, titles.Count); i++) buttons[i].Text = titles[i];
    }

    private static void SelectAndroidButton(IReadOnlyList<Button> buttons, int index)
    {
        for (var i = 0; i < buttons.Count; i++)
            buttons[i].FontAttributes = i == index ? FontAttributes.Bold : FontAttributes.None;
    }

    private async Task ShowAndroidPlanMenuAsync(IReadOnlyList<string> labels, string cancelLabel)
    {
        if (_androidMenuOpen || labels.Count == 0) return;
        _androidMenuOpen = true;
        try
        {
            var selected = await DisplayActionSheetAsync(_androidTabs[3].Text, cancelLabel, null, labels.ToArray());
            var index = Array.IndexOf(labels.ToArray(), selected);
            if (index >= 0) await EvalOnMainAsync($"window.__novalistPlanSelect?.({index})");
            else await EvalOnMainAsync("window.__novalistPlanDismiss?.()");
        }
        finally { _androidMenuOpen = false; }
    }

    private void DisposeAndroidChrome()
    {
        if (_androidChrome != null) _androidChrome.SizeChanged -= OnAndroidSizeChanged;
    }
}
