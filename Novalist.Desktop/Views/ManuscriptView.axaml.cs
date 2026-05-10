using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Platform;
using Novalist.Desktop.Utilities;
using Novalist.Desktop.ViewModels;

namespace Novalist.Desktop.Views;

public partial class ManuscriptView : UserControl
{
    private ManuscriptViewModel? _vm;
    private NativeWebView? _webView;
    private bool _webViewReady;
    private bool _pendingRefresh;
    private Image? _snapshotImage;

    public ManuscriptView()
    {
        InitializeComponent();
        TryCreateWebView();
    }

    internal void SetWebViewVisible(bool visible)
    {
        if (_webView == null) return;
        if (visible)
        {
            _webView.IsVisible = true;
            if (_snapshotImage != null) _snapshotImage.IsVisible = false;
        }
        else
        {
            if (_webView.IsVisible)
            {
                var capturedBounds = _webView.Bounds;
                var bmp = WebViewSnapshotter.Capture(_webView);
                if (bmp != null)
                {
                    EnsureSnapshotImage();
                    _snapshotImage!.Source = bmp;
                    _snapshotImage.Width = capturedBounds.Width;
                    _snapshotImage.Height = capturedBounds.Height;
                    _snapshotImage.IsVisible = true;
                }
            }
            _webView.IsVisible = false;
        }
    }

    private void EnsureSnapshotImage()
    {
        if (_snapshotImage != null || _webView == null) return;
        _snapshotImage = new Image
        {
            Stretch = Stretch.None,
            IsHitTestVisible = false,
            IsVisible = false,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        var idx = ManuscriptHost.Children.IndexOf(_webView);
        ManuscriptHost.Children.Insert(idx + 1, _snapshotImage);
    }

    private void TryCreateWebView()
    {
        try
        {
            _webView = new NativeWebView();
            ManuscriptHost.Children.Insert(0, _webView);

            _webView.EnvironmentRequested += (_, e) =>
            {
                if (e is WindowsWebView2EnvironmentRequestedEventArgs w)
                    w.UserDataFolder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Novalist", "WebView2", "default");
            };
            _webView.NavigationCompleted += OnNavigationCompleted;
            _webView.WebMessageReceived += OnWebMessageReceived;

            NavigateToManuscriptPage();
        }
        catch (Exception ex)
        {
            _webView = null;
            Console.Error.WriteLine($"[ManuscriptWebView] {ex}");
        }
    }

    private void NavigateToManuscriptPage()
    {
        if (_webView == null) return;

        var htmlPath = ResolveManuscriptHtmlPath();
        if (htmlPath != null)
        {
            if (OperatingSystem.IsMacOS())
            {
                _webView.NavigateToString(File.ReadAllText(htmlPath));
            }
            else
            {
                _webView.Source = new Uri(htmlPath);
            }
        }
    }

    private static string? ResolveManuscriptHtmlPath()
    {
        var basePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Editor", "manuscript-editor.html");
        if (File.Exists(basePath)) return basePath;

        var macBundlePath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "Resources", "Assets", "Editor", "manuscript-editor.html"));
        if (File.Exists(macBundlePath)) return macBundlePath;

        return null;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        DataContextChanged += OnDataContextChanged;
        if (DataContext is ManuscriptViewModel vm)
            AttachToViewModel(vm);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DataContextChanged -= OnDataContextChanged;
        DetachFromViewModel();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        DetachFromViewModel();
        if (DataContext is ManuscriptViewModel vm)
            AttachToViewModel(vm);
    }

    private void AttachToViewModel(ManuscriptViewModel vm)
    {
        if (ReferenceEquals(_vm, vm)) return;
        _vm = vm;
        _vm.PropertyChanged += OnVmPropertyChanged;
        _vm.ContentRefreshRequested += OnContentRefreshRequested;
        if (_webViewReady && _vm.HasContent)
            PushManuscriptToWebView();
    }

    private void DetachFromViewModel()
    {
        if (_vm != null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.ContentRefreshRequested -= OnContentRefreshRequested;
            _vm = null;
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ManuscriptViewModel.HasContent))
        {
            if (_vm?.HasContent == true && _webViewReady)
                PushManuscriptToWebView();
        }
    }

    private void OnContentRefreshRequested()
    {
        if (_webViewReady)
            PushManuscriptToWebView();
        else
            _pendingRefresh = true;
    }

    // ── Navigation Events ───────────────────────────────────────────

    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        _webViewReady = true;
        ApplyTheme();
        ApplyFont();

        if (_pendingRefresh || _vm?.HasContent == true)
        {
            _pendingRefresh = false;
            PushManuscriptToWebView();
        }
    }

    // ── Push Content to WebView ─────────────────────────────────────

    private void PushManuscriptToWebView()
    {
        if (_vm == null || _webView == null || !_webViewReady) return;

        var json = _vm.GetManuscriptJson();
        var escaped = JsonEncodedText.Encode(json).ToString();
        ExecuteScript($"setManuscript(\"{escaped}\")");
        UpdateStatsText();
    }

    private void UpdateStatsText()
    {
        if (_vm == null) return;
        StatsText.Text = $"{_vm.TotalWordsDisplay} · {_vm.TotalScenes} scenes · {_vm.ReadingTimeDisplay}";
    }

    // ── WebView Message Handling ────────────────────────────────────

    private void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Body) || _vm == null) return;

        try
        {
            using var doc = JsonDocument.Parse(e.Body);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            switch (type)
            {
                case "ready":
                    _webViewReady = true;
                    ApplyTheme();
                    ApplyFont();
                    if (_pendingRefresh || _vm.HasContent)
                    {
                        _pendingRefresh = false;
                        PushManuscriptToWebView();
                    }
                    break;

                case "sceneContentChanged":
                    OnSceneContentChanged(root);
                    break;

                case "cycleStatus":
                    OnCycleStatus(root);
                    break;

                case "openScene":
                    OnOpenScene(root);
                    break;

                case "save":
                    _ = _vm.SaveAllDirtyAsync();
                    break;

                case "sceneFocused":
                    OnSceneFocused(root);
                    break;

                case "hotkey":
                    OnHotkeyFromWebView(root);
                    break;
            }
        }
        catch { /* ignore malformed messages */ }
    }

    private void OnSceneContentChanged(JsonElement root)
    {
        if (_vm == null) return;
        var sceneId = root.GetProperty("sceneId").GetString() ?? string.Empty;
        var chapterGuid = root.GetProperty("chapterGuid").GetString() ?? string.Empty;
        var html = root.GetProperty("html").GetString() ?? string.Empty;
        var wordCount = root.GetProperty("wordCount").GetInt32();

        _vm.OnWebViewSceneChanged(sceneId, chapterGuid, html, wordCount);
        UpdateStatsText();
    }

    private void OnCycleStatus(JsonElement root)
    {
        if (_vm == null) return;
        var chapterGuid = root.GetProperty("chapterGuid").GetString() ?? string.Empty;
        _ = _vm.CycleStatusByGuidAsync(chapterGuid);
    }

    private void OnOpenScene(JsonElement root)
    {
        if (_vm == null) return;
        var chapterGuid = root.GetProperty("chapterGuid").GetString() ?? string.Empty;
        var sceneId = root.GetProperty("sceneId").GetString() ?? string.Empty;
        _vm.RequestOpenScene(chapterGuid, sceneId);
    }

    private void OnSceneFocused(JsonElement root)
    {
        if (_vm == null) return;
        var chapterGuid = root.GetProperty("chapterGuid").GetString() ?? string.Empty;
        var sceneId = root.GetProperty("sceneId").GetString() ?? string.Empty;
        _vm.OnSceneFocused(chapterGuid, sceneId);
    }

    private void OnHotkeyFromWebView(JsonElement root)
    {
        var code = root.GetProperty("code").GetString() ?? string.Empty;
        var key = root.GetProperty("key").GetString() ?? string.Empty;
        var ctrl = root.GetProperty("ctrlKey").GetBoolean();
        var shift = root.GetProperty("shiftKey").GetBoolean();
        var alt = root.GetProperty("altKey").GetBoolean();

        var avKey = WebViewKeyMapper.MapToAvaloniaKey(code, key);
        if (avKey == Key.None) return;

        var modifiers = KeyModifiers.None;
        if (ctrl) modifiers |= KeyModifiers.Control;
        if (shift) modifiers |= KeyModifiers.Shift;
        if (alt) modifiers |= KeyModifiers.Alt;

        App.HotkeyManager.TryExecute(avKey, modifiers);
    }

    // ── Theme & Font ────────────────────────────────────────────────

    private void ApplyTheme()
    {
        if (!_webViewReady || _webView == null) return;

        string bg = "#1e1e2e", fg = "#cdd6f4", selBg = "#45475a", accent = "#89b4fa", subtle = "#6c7086", divider = "#313244";

        if (App.Current?.TryGetResource("EditorBackground", App.Current.ActualThemeVariant, out var bgRes) == true
            && bgRes is ISolidColorBrush bgBrush)
            bg = FormatColor(bgBrush.Color);

        if (App.Current?.TryGetResource("NormalText", App.Current.ActualThemeVariant, out var fgRes) == true
            && fgRes is ISolidColorBrush fgBrush)
            fg = FormatColor(fgBrush.Color);

        if (App.Current?.TryGetResource("AccentBrush", App.Current.ActualThemeVariant, out var accentRes) == true
            && accentRes is ISolidColorBrush accentBrush)
            accent = FormatColor(accentBrush.Color);

        if (App.Current?.TryGetResource("SubtleText", App.Current.ActualThemeVariant, out var subtleRes) == true
            && subtleRes is ISolidColorBrush subtleBrush)
            subtle = FormatColor(subtleBrush.Color);

        if (App.Current?.TryGetResource("CardBorder", App.Current.ActualThemeVariant, out var divRes) == true
            && divRes is ISolidColorBrush divBrush)
            divider = FormatColor(divBrush.Color);

        ExecuteScript($"setTheme('{bg}','{fg}','{fg}','{selBg}','{accent}','{subtle}','{divider}')");
    }

    private void ApplyFont()
    {
        if (!_webViewReady || _webView == null) return;
        // Use default editor font settings if available
        var family = "Segoe UI";
        var size = 16;

        if (App.Current?.TryGetResource("EditorFontFamily", App.Current.ActualThemeVariant, out var fontRes) == true
            && fontRes is FontFamily ff)
            family = ff.Name;

        ExecuteScript($"setFont('{family.Replace("'", "\\'")}',{size})");
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private void ExecuteScript(string script)
    {
        if (_webViewReady && _webView != null)
            _ = _webView.InvokeScript(script);
    }

    private static string FormatColor(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
