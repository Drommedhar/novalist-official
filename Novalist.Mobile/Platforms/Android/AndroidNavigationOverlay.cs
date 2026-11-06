using Microsoft.Maui.Controls.Shapes;

namespace Novalist.Mobile.Pages;

public sealed partial class RendererHostPage
{
    private Grid? _androidPlanOverlay;
    private Border? _androidPlanMenu;
    private readonly List<Button> _androidPlanButtons = [];

    private void ShowAndroidPlanMenu(IReadOnlyList<string> labels)
    {
        HideAndroidPlanMenu();
        if (_androidChrome == null || _androidTablet || !_androidNavVisible || labels.Count == 0) return;
        var items = new VerticalStackLayout { Padding = new Thickness(0, 6) };
        for (var i = 0; i < labels.Count; i++)
        {
            var index = i;
            var button = new Button
            {
                Text = labels[i], AutomationId = $"planning-{index}", FontSize = 17,
                MinimumHeightRequest = 48, Padding = new Thickness(16, 0), CornerRadius = 0,
                BackgroundColor = Colors.Transparent, TextColor = _androidForeground
            };
            button.HandlerChanged += (_, _) =>
            {
                if (button.Handler?.PlatformView is Android.Widget.Button native)
                    native.Gravity = Android.Views.GravityFlags.CenterVertical | Android.Views.GravityFlags.Start;
            };
            button.Clicked += async (_, _) =>
            {
                HideAndroidPlanMenu();
                await EvalOnMainAsync($"window.__novalistPlanSelect?.({index})");
            };
            _androidPlanButtons.Add(button);
            items.Add(button);
        }
        _androidPlanMenu = new Border
        {
            Content = new ScrollView { Content = items }, BackgroundColor = _androidSurface,
            Stroke = _androidForeground.WithAlpha(0.18f), StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            VerticalOptions = LayoutOptions.End, HorizontalOptions = LayoutOptions.Start,
            Shadow = new Shadow { Brush = Colors.Black, Opacity = 0.3f, Radius = 12, Offset = new Point(0, 4) }
        };
        _androidPlanOverlay = new Grid { ZIndex = 3 };
        var scrim = CreateAndroidScrim(DismissAndroidPlanMenu);
        scrim.IsVisible = true;
        scrim.ZIndex = 0;
        _androidPlanOverlay.Add(scrim);
        _androidPlanOverlay.Add(_androidPlanMenu);
        PositionAndroidPlanMenu();
        _androidChrome.Add(_androidPlanOverlay);
    }

    private void PositionAndroidPlanMenu()
    {
        if (_androidChrome == null || _androidPlanOverlay == null || _androidPlanMenu == null) return;
        var tabHeight = Math.Max(AndroidTabBarHeight, _androidBottomBar?.Height ?? 0);
        var width = Math.Min(AndroidSidebarWidth, Math.Max(0, _androidChrome.Width - 16));
        var x = Math.Clamp(_androidChrome.Width * 0.7 - width / 2, 8, Math.Max(8, _androidChrome.Width - width - 8));
        _androidPlanOverlay.Margin = new Thickness(0, 0, 0, tabHeight);
        _androidPlanMenu.WidthRequest = width;
        _androidPlanMenu.HeightRequest = Math.Min(_androidPlanButtons.Count * 48 + 12, Math.Max(0, _androidChrome.Height - tabHeight - 16));
        _androidPlanMenu.Margin = new Thickness(x, 8, 8, 8);
    }

    private void HideAndroidPlanMenu()
    {
        if (_androidPlanOverlay != null) _androidChrome?.Remove(_androidPlanOverlay);
        _androidPlanOverlay = null;
        _androidPlanMenu = null;
        _androidPlanButtons.Clear();
    }

    private void DismissAndroidPlanMenu()
    {
        if (_androidPlanOverlay == null) return;
        HideAndroidPlanMenu();
        _ = EvalOnMainAsync("window.__novalistPlanDismiss?.()");
    }

    protected override bool OnBackButtonPressed()
    {
        if (_androidPlanOverlay != null) { DismissAndroidPlanMenu(); return true; }
        if (_androidTablet && _androidNavVisible && !_androidSidebarCollapsed)
        {
            SetAndroidSidebarCollapsed(true);
            return true;
        }
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

    private void SetAndroidColors(string background, string foreground)
    {
        if (!Color.TryParse(background, out var surface) || !Color.TryParse(foreground, out var text)) return;
        _androidSurface = surface;
        _androidForeground = text;
        BackgroundColor = surface;
        if (_androidChrome != null) _androidChrome.BackgroundColor = surface;
        if (_androidSidebar != null) _androidSidebar.BackgroundColor = surface;
        if (_androidBottomBar != null) _androidBottomBar.BackgroundColor = surface;
        if (_androidPlanMenu != null)
        {
            _androidPlanMenu.BackgroundColor = surface;
            _androidPlanMenu.Stroke = text.WithAlpha(0.18f);
        }
        foreach (var button in _androidTabs.Concat(_androidDestinations)) button.SetForeground(text);
        foreach (var button in _androidPlanButtons) button.TextColor = text;
        foreach (var separator in _androidSeparators) separator.Color = text.WithAlpha(0.18f);
        if (Platform.CurrentActivity?.Window is not { } window) return;
        var insets = AndroidX.Core.View.WindowCompat.GetInsetsController(window, window.DecorView);
        if (insets == null) return;
        var light = surface.Red * 0.2126 + surface.Green * 0.7152 + surface.Blue * 0.0722 > 0.5;
        insets.AppearanceLightStatusBars = light;
        insets.AppearanceLightNavigationBars = light;
    }
}
