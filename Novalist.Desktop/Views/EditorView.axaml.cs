using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Novalist.Desktop.Utilities;
using Novalist.Desktop.ViewModels;

namespace Novalist.Desktop.Views;

public partial class EditorView : UserControl
{
    private EditorViewModel? _vm;
    private bool _webViewReady;
    private string? _pendingContent;
    private bool _loadingContentFromViewModel;
    private NativeWebView? _webView;
    private string? _reinitLanguage;
    private Image? _snapshotImage;

    /// <summary>
    /// Hides or shows the native WebView control to work around the airspace
    /// problem where the WebView2 HWND renders on top of all Avalonia overlays.
    /// On hide, captures a bitmap of the WebView and shows it in place so the
    /// transition is visually seamless.
    /// </summary>
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
            Stretch = Avalonia.Media.Stretch.Uniform,
            IsHitTestVisible = false,
            IsVisible = false,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        var idx = EditorHost.Children.IndexOf(_webView);
        EditorHost.Children.Insert(idx + 1, _snapshotImage);
    }

    public EditorView()
    {
        InitializeComponent();
        Focusable = true;

        TryCreateWebView();
    }

    private void TryCreateWebView()
    {
        try
        {
            _webView = new NativeWebView();
            _webView[!NativeWebView.IsHitTestVisibleProperty] =
                new Avalonia.Data.ReflectionBinding(nameof(EditorViewModel.IsDocumentOpen));

            EditorHost.Children.Insert(0, _webView);
            FocusPeekPopup.PlacementTarget = _webView;

            _webView.EnvironmentRequested += OnEnvironmentRequested;
            _webView.AdapterCreated += OnAdapterCreated;
            _webView.NavigationStarted += OnNavigationStarted;
            _webView.NavigationCompleted += OnNavigationCompleted;
            _webView.WebMessageReceived += OnWebMessageReceived;

            NavigateToEditorPage();
        }
        catch (Exception ex)
        {
            _webView = null;
            Console.Error.WriteLine($"[WebViewCreate] {ex}");
            ShowFallbackMessage();
        }
    }

    private void ShowFallbackMessage()
    {
        var message = new TextBlock
        {
            Text = "The rich-text editor is not available on this platform.\n" +
                   "Project management features (characters, locations, plot board, etc.) still work.",
            FontSize = 14,
            Foreground = Avalonia.Media.Brushes.Gray,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            TextAlignment = Avalonia.Media.TextAlignment.Center,
            Margin = new Thickness(40)
        };
        EditorHost.Children.Insert(0, message);
        FocusPeekPopup.PlacementTarget = EditorHost;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        DataContextChanged += OnDataContextChanged;
        if (_webView != null)
            _webView.SizeChanged += OnEditorSizeChanged;

        if (App.ExtensionManager?.Host is { } host)
            host.InlineActionContributorsChanged += OnInlineActionContributorsChanged;

        if (DataContext is EditorViewModel vm)
            AttachToViewModel(vm);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DataContextChanged -= OnDataContextChanged;
        if (_webView != null)
            _webView.SizeChanged -= OnEditorSizeChanged;
        if (App.ExtensionManager?.Host is { } host)
            host.InlineActionContributorsChanged -= OnInlineActionContributorsChanged;
        DetachFromViewModel();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnInlineActionContributorsChanged()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(PushInlineActions);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        DetachFromViewModel();
        if (DataContext is EditorViewModel vm)
            AttachToViewModel(vm);
    }

    private void AttachToViewModel(EditorViewModel vm)
    {
        if (ReferenceEquals(_vm, vm)) return;

        _vm = vm;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        _vm.GrammarCheck.SetScriptExecutor(ExecuteScript);
        WireFormattingActions(vm);
        ApplyEditorSettings();
    }

    private void DetachFromViewModel()
    {
        if (_vm != null)
        {
            ClearFormattingActions(_vm);
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm = null;
        }
    }

    private void WireFormattingActions(EditorViewModel vm)
    {
        vm.AddCommentAction = id => ExecuteScript($"addCommentToSelection('{EscapeForSingleQuoteJs(id)}')");
        vm.RemoveCommentAction = id => ExecuteScript($"removeCommentById('{EscapeForSingleQuoteJs(id)}')");
        vm.ScrollToCommentAction = id => ExecuteScript($"scrollToCommentById('{EscapeForSingleQuoteJs(id)}')");
        vm.SyncCommentsAction = () => SyncCommentsToWebView();
        vm.ToggleBoldAction = () => ExecuteScript("toggleBold()");
        vm.ToggleItalicAction = () => ExecuteScript("toggleItalic()");
        vm.ToggleUnderlineAction = () => ExecuteScript("toggleUnderline()");
        vm.AlignLeftAction = () => ExecuteScript("alignLeft()");
        vm.AlignCenterAction = () => ExecuteScript("alignCenter()");
        vm.AlignRightAction = () => ExecuteScript("alignRight()");
        vm.AlignJustifyAction = () => ExecuteScript("alignJustify()");
    }

    private static void ClearFormattingActions(EditorViewModel vm)
    {
        vm.ToggleBoldAction = null;
        vm.ToggleItalicAction = null;
        vm.ToggleUnderlineAction = null;
        vm.AlignLeftAction = null;
        vm.AlignCenterAction = null;
        vm.AlignRightAction = null;
        vm.AlignJustifyAction = null;
    }

    // ── Navigation & Content ────────────────────────────────────────

    private void OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs e)
    {
        if (e is WindowsWebView2EnvironmentRequestedEventArgs webView2)
        {
            var lang = _reinitLanguage ?? "default";
            webView2.UserDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Novalist", "WebView2", lang);
            if (_reinitLanguage != null)
                webView2.Language = _reinitLanguage;
        }
    }

    private void OnAdapterCreated(object? sender, WebViewAdapterEventArgs e)
    {
    }

    private void NavigateToEditorPage()
    {
        if (_webView == null) return;

        var editorPath = ResolveEditorHtmlPath();
        if (editorPath != null)
        {
            Console.WriteLine($"[Editor] Resolved editor.html at: {editorPath}");
            if (OperatingSystem.IsMacOS())
            {
                // WKWebView blocks file:// URL navigation; load as HTML string instead
                var html = File.ReadAllText(editorPath);
                Console.WriteLine($"[Editor] Loaded HTML via HtmlContent ({html.Length} chars)");
                _webView.NavigateToString(html);
            }
            else
            {
                _webView.Source = new Uri(editorPath);
            }
        }
        else
        {
            Console.WriteLine("[Editor] Using bare fallback HTML (no editor.html found)");
            _webView.NavigateToString("<html><body><div contenteditable='true' id='editor'></div></body></html>");
        }
    }

    private static string? ResolveEditorHtmlPath()
    {
        Console.WriteLine($"[Editor] AppContext.BaseDirectory = {AppContext.BaseDirectory}");

        var basePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Editor", "editor.html");
        Console.WriteLine($"[Editor] Checking: {basePath} -> {File.Exists(basePath)}");
        if (File.Exists(basePath))
            return basePath;

        // macOS .app bundle: directories land in Contents/Resources/ instead of Contents/MacOS/
        var macBundlePath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "Resources", "Assets", "Editor", "editor.html"));
        Console.WriteLine($"[Editor] Checking macOS bundle: {macBundlePath} -> {File.Exists(macBundlePath)}");
        if (File.Exists(macBundlePath))
            return macBundlePath;

        Console.WriteLine("[Editor] WARNING: editor.html not found at any known location!");
        return null;
    }

    private void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
    }

    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        _webViewReady = true;
        ApplyEditorSettings();
        PushContextMenuLabels();
        PushInlineActions();
        PushGrammarCheckState();

        if (_pendingContent != null)
        {
            var content = _pendingContent;
            _pendingContent = null;
            SetContentInWebView(content);
        }

        PushAutoReplacements();
        PushDialogueCorrection();
        PushEntityNames();
    }

    internal void SetContent(string content)
    {
        if (!_webViewReady)
        {
            _pendingContent = content;
            return;
        }
        SetContentInWebView(content);
    }

    private async void SetContentInWebView(string content)
    {
        _loadingContentFromViewModel = true;
        // JSON-escape large HTML off UI thread
        var escaped = await Task.Run(() => JsonEncodedText.Encode(content).ToString()).ConfigureAwait(true);
        ExecuteScript($"setContent(\"{escaped}\")");
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _loadingContentFromViewModel = false;
            SyncCommentsToWebView();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    internal void SyncCommentsToWebView()
    {
        if (!_webViewReady) return;
        var comments = _vm?.CurrentScene?.Comments;
        var payload = comments == null
            ? "[]"
            : JsonSerializer.Serialize(comments.Select(c => new
            {
                id = c.Id,
                anchorText = c.AnchorText ?? string.Empty,
                text = c.Text ?? string.Empty
            }));
        ExecuteScript($"setCommentsData({payload})");
    }

    internal string GetPlainText()
    {
        // Plain text is tracked via JS messages — return cached value from ViewModel
        return _vm?.PlainTextContent ?? string.Empty;
    }

    internal string GetHtmlContent()
    {
        return _vm?.Content ?? string.Empty;
    }

    // ── WebView Message Handling ────────────────────────────────────

    private void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Body)) return;

        try
        {
            using var doc = JsonDocument.Parse(e.Body);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            switch (type)
            {
                case "ready":
                    OnEditorReady();
                    break;
                case "contentChanged":
                    OnContentChanged(root);
                    break;
                case "formattingChanged":
                    OnFormattingChanged(root);
                    break;
                case "caretPosition":
                    OnCaretPositionChanged(root);
                    break;
                case "entityHover":
                    OnEntityHover(root);
                    break;
                case "entityExit":
                    OnEntityExit();
                    break;
                case "pointerPressed":
                    OnPointerPressedInEditor();
                    break;
                case "save":
                    OnSaveRequested();
                    break;
                case "zoom":
                    OnZoom(root);
                    break;
                case "hotkey":
                    OnHotkeyFromWebView(root);
                    break;
                case "grammarCheckRequest":
                    OnGrammarCheckRequest(root);
                    break;
                case "commentAdded":
                {
                    var commentId = root.GetProperty("commentId").GetString() ?? string.Empty;
                    var anchorText = root.TryGetProperty("anchorText", out var a) ? a.GetString() ?? string.Empty : string.Empty;
                    if (!string.IsNullOrEmpty(commentId) && _vm != null)
                    {
                        _vm.RaiseCommentAnchored(commentId, anchorText);
                        // Push updated comment list so the gutter card appears.
                        SyncCommentsToWebView();
                    }
                    break;
                }
                case "commentTextChanged":
                {
                    var commentId = root.GetProperty("commentId").GetString() ?? string.Empty;
                    var text = root.TryGetProperty("text", out var tv) ? tv.GetString() ?? string.Empty : string.Empty;
                    if (!string.IsNullOrEmpty(commentId) && _vm != null)
                        _vm.RaiseCommentTextEdited(commentId, text);
                    break;
                }
                case "commentDeleted":
                {
                    var commentId = root.GetProperty("commentId").GetString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(commentId) && _vm != null)
                        _vm.RaiseCommentDeleteRequested(commentId);
                    break;
                }
                case "commentClicked":
                {
                    var commentId = root.GetProperty("commentId").GetString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(commentId))
                        _vm?.RaiseCommentClicked(commentId);
                    break;
                }
                case "inlineActionRequested":
                {
                    var actionId = root.GetProperty("actionId").GetString() ?? string.Empty;
                    var selected = root.GetProperty("selectedText").GetString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(actionId) && !string.IsNullOrEmpty(selected) && _vm != null)
                        _ = ExecuteInlineActionAsync(actionId, selected);
                    break;
                }
            }
        }
        catch
        {
            // Ignore malformed messages
        }
    }

    private void OnEditorReady()
    {
        ApplyEditorSettings();
        PushContextMenuLabels();
        PushInlineActions();
        PushGrammarCheckState();
        if (_vm?.IsDocumentOpen == true)
        {
            SetContentInWebView(_vm.Content);
            PushAutoReplacements();
            PushDialogueCorrection();
            PushEntityNames();
        }
    }

    private void OnContentChanged(JsonElement root)
    {
        if (_loadingContentFromViewModel || _vm == null || _vm.IsSceneLoading) return;

        var html = root.GetProperty("html").GetString() ?? string.Empty;
        var plainText = root.GetProperty("plainText").GetString() ?? string.Empty;
        _vm.OnTextChanged(html, plainText);
    }

    private void OnFormattingChanged(JsonElement root)
    {
        if (_vm == null) return;

        var bold = root.GetProperty("bold").GetBoolean();
        var italic = root.GetProperty("italic").GetBoolean();
        var underline = root.GetProperty("underline").GetBoolean();
        var alignStr = root.GetProperty("alignment").GetString() ?? "left";
        var alignment = alignStr switch
        {
            "center" => TextAlignment.Center,
            "right" => TextAlignment.Right,
            "justify" => TextAlignment.Justify,
            _ => TextAlignment.Left
        };
        _vm.UpdateFormattingState(bold, italic, underline, alignment);
    }

    private void OnCaretPositionChanged(JsonElement root)
    {
        if (_vm == null) return;
        var line = root.GetProperty("line").GetInt32();
        var column = root.GetProperty("column").GetInt32();
        _vm.OnCaretPositionChanged(line, column);
    }

    private void OnEntityHover(JsonElement root)
    {
        if (_vm == null) return;
        var alias = root.GetProperty("alias").GetString() ?? string.Empty;
        var x = root.GetProperty("x").GetDouble();
        var y = root.GetProperty("y").GetDouble();
        _ = _vm.FocusPeekExtension.OnEntityHoverAsync(alias, x, y);
    }

    private void OnEntityExit()
    {
        _vm?.FocusPeekExtension.OnEntityExit();
    }

    private void OnPointerPressedInEditor()
    {
        _vm?.FocusPeekExtension.OnPointerPressed();
        NotifyActivePane();
    }

    /// <summary>
    /// Tells MainWindow that this pane is now the focused one so the context
    /// sidebar binds against this pane's active scene. Called whenever the
    /// editor receives a click.
    /// </summary>
    private void NotifyActivePane()
    {
        if (_vm == null) return;
        if (TopLevel.GetTopLevel(this) is MainWindow mw && mw.DataContext is MainWindowViewModel main)
            main.SetActivePane(_vm);
    }

    private void OnSaveRequested()
    {
        if (_vm != null)
            _ = _vm.SaveAsync();
    }

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Middle-click closes tab.
        var props = e.GetCurrentPoint(sender as Control).Properties;
        if (props.IsMiddleButtonPressed && sender is Control c && c.Tag is EditorOpenScene tab)
        {
            _ = _vm?.CloseTabAsync(tab);
            e.Handled = true;
            return;
        }
        // Any click in this pane = focus this pane.
        NotifyActivePane();
    }

    private async void OnTabCloseClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is Control c && c.Tag is EditorOpenScene tab)
            await _vm.CloseTabAsync(tab);
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? (s ?? string.Empty) : s[..max] + "…";

    private static EditorOpenScene? FindTabFromMenu(object? sender)
    {
        if (sender is not MenuItem mi) return null;
        // MenuItem inside ContextMenu inherits placement-target DataContext.
        if (mi.DataContext is EditorOpenScene tab) return tab;
        // Fallback: walk up to ContextMenu and read its Tag (set in XAML).
        Avalonia.StyledElement? p = mi.Parent;
        while (p is not null && p is not Avalonia.Controls.ContextMenu) p = p.Parent;
        if (p is Avalonia.Controls.ContextMenu cm && cm.Tag is EditorOpenScene cmTab) return cmTab;
        return null;
    }

    private async void OnTabContextCloseClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        var tab = FindTabFromMenu(sender);
        if (tab != null) await _vm.CloseTabAsync(tab);
    }

    private async void OnTabContextMoveClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        var tab = FindTabFromMenu(sender);
        if (tab == null) return;
        if (TopLevel.GetTopLevel(this) is not MainWindow mw
            || mw.DataContext is not MainWindowViewModel main)
            return;
        await main.MoveSceneTabAsync(_vm, tab);
    }

    private void OnZoom(JsonElement root)
    {
        if (_vm == null) return;
        var delta = root.GetProperty("delta").GetDouble();
        var newSize = Math.Clamp(_vm.EditorFontSize + delta, 8, 36);
        _vm.SetFontSize(newSize);
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

    // ── Settings & Theme ────────────────────────────────────────────

    private void ApplyEditorSettings()
    {
        if (!_webViewReady || _vm == null) return;

        ApplyTheme();
        ApplyFont();
        ApplyBookParagraphSpacing();
        ApplyBookWidth();
        ApplyLanguage();
    }

    private void ApplyTheme()
    {
        string bg = "#1e1e2e", fg = "#cdd6f4", selBg = "#45475a";
        if (App.Current?.TryGetResource("EditorBackground", App.Current.ActualThemeVariant, out var bgRes) == true
            && bgRes is ISolidColorBrush bgBrush)
            bg = FormatColor(bgBrush.Color);

        if (App.Current?.TryGetResource("NormalText", App.Current.ActualThemeVariant, out var fgRes) == true
            && fgRes is ISolidColorBrush fgBrush)
            fg = FormatColor(fgBrush.Color);

        ExecuteScript($"setTheme('{bg}','{fg}','{fg}','{selBg}')");
    }

    private void ApplyFont()
    {
        if (_vm == null) return;
        var family = _vm.EditorFontFamily.Replace("'", "\\'");
        ExecuteScript($"setFont('{family}',{_vm.EditorFontSize})");
    }

    private void ApplyBookParagraphSpacing()
    {
        if (_vm == null) return;
        var enabled = _vm.BookParagraphSpacingEnabled ? "true" : "false";
        ExecuteScript($"setBookParagraphSpacing({enabled})");
    }

    private void ApplyBookWidth()
    {
        if (_vm == null) return;
        var enabled = _vm.BookWidthEnabled ? "true" : "false";
        ExecuteScript($"setBookWidth({enabled},{_vm.BookEditorWidth:F0})");
    }

    private void ApplyLanguage()
    {
        var lang = Localization.Loc.Instance.CurrentLanguage;
        ExecuteScript($"setLanguage('{EscapeForSingleQuoteJs(lang)}')");
    }

    private void PushAutoReplacements()
    {
        if (!_webViewReady || _vm == null) return;
        var json = _vm.AutoReplacement.SerializePairsJson();
        ExecuteScript($"setAutoReplacements('{EscapeForSingleQuoteJs(json)}')");
    }

    private void PushDialogueCorrection()
    {
        if (!_webViewReady || _vm == null) return;
        var json = _vm.DialogueCorrection.SerializeConfigJson();
        ExecuteScript($"setDialogueCorrectionConfig('{EscapeForSingleQuoteJs(json)}')");
    }

    internal void PushEntityNames()
    {
        if (!_webViewReady || _vm == null) return;
        var json = _vm.FocusPeekExtension.GetEntityNamesJson();
        ExecuteScript($"setEntityNames('{EscapeForSingleQuoteJs(json)}')");
    }

    private void PushGrammarCheckState()
    {
        if (!_webViewReady || _vm == null) return;
        _vm.GrammarCheck.SetScriptExecutor(ExecuteScript);
        _vm.GrammarCheck.PushState();
    }

    private void PushContextMenuLabels()
    {
        if (!_webViewReady) return;
        var loc = Localization.Loc.Instance;
        var json = $"{{\"cut\":\"{EscapeForJsonValue(loc["editor.contextMenu.cut"])}\","
                 + $"\"copy\":\"{EscapeForJsonValue(loc["editor.contextMenu.copy"])}\","
                 + $"\"paste\":\"{EscapeForJsonValue(loc["editor.contextMenu.paste"])}\","
                 + $"\"selectAll\":\"{EscapeForJsonValue(loc["editor.contextMenu.selectAll"])}\"}}";
        ExecuteScript($"setContextMenuLabels('{EscapeForSingleQuoteJs(json)}')");
    }

    private void PushInlineActions()
    {
        if (!_webViewReady)
        {
            System.Diagnostics.Debug.WriteLine("[InlineActions] PushInlineActions skipped — webView not ready.");
            return;
        }
        var contributors = App.ExtensionManager?.Host.GetInlineActionContributors() ?? new List<Novalist.Sdk.Hooks.IInlineActionContributor>();
        System.Diagnostics.Debug.WriteLine($"[InlineActions] PushInlineActions. Host null? {App.ExtensionManager is null}. Contributors: {contributors.Count}");
        var flat = new List<object>();
        foreach (var c in contributors)
        {
            var actions = c.GetInlineActions().OrderBy(a => a.Priority).ToList();
            System.Diagnostics.Debug.WriteLine($"[InlineActions]  contributor={c.GetType().Name} actions={actions.Count} ids={string.Join(",", actions.Select(a => a.Id))}");
            foreach (var a in actions)
            {
                flat.Add(new
                {
                    id = a.Id,
                    label = a.Label,
                    group = a.Group ?? string.Empty,
                    icon = a.Icon ?? string.Empty,
                });
            }
        }
        var json = JsonSerializer.Serialize(flat);
        System.Diagnostics.Debug.WriteLine($"[InlineActions]  pushing JSON to JS: {json}");
        ExecuteScript($"setInlineActions({json})");
    }

    private async Task ExecuteInlineActionAsync(string actionId, string selectedText)
    {
        try
        {
            var contributors = App.ExtensionManager?.Host.GetInlineActionContributors();
            if (contributors == null) return;
            Novalist.Sdk.Hooks.IInlineActionContributor? matched = null;
            foreach (var c in contributors)
            {
                if (c.GetInlineActions().Any(a => string.Equals(a.Id, actionId, StringComparison.Ordinal)))
                {
                    matched = c;
                    break;
                }
            }
            if (matched == null) return;

            var request = new Novalist.Sdk.Hooks.InlineActionRequest
            {
                SelectedText = selectedText,
                SceneId = _vm?.CurrentScene?.Id ?? string.Empty,
                ChapterGuid = _vm?.CurrentScene?.ChapterGuid ?? string.Empty,
            };
            var result = await matched.ExecuteAsync(actionId, request, default).ConfigureAwait(true);

            var payload = JsonSerializer.Serialize(new
            {
                actionId,
                text = result.Text ?? string.Empty,
                disposition = result.Disposition == Novalist.Sdk.Hooks.InlineActionDisposition.InsertAfterSelection ? "insertAfter" : "replace",
                error = result.Error,
            });
            ExecuteScript($"applyInlineActionResult({payload})");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[InlineAction] {actionId} failed: {ex.Message}");
            var payload = JsonSerializer.Serialize(new { actionId, text = string.Empty, disposition = "replace", error = ex.Message });
            ExecuteScript($"applyInlineActionResult({payload})");
        }
    }

    private void OnGrammarCheckRequest(JsonElement root)
    {
        if (_vm == null) return;
        var plainText = root.GetProperty("plainText").GetString() ?? string.Empty;
        _ = _vm.GrammarCheck.CheckTextAsync(plainText);
    }

    // ── ViewModel Property Change Handling ──────────────────────────

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.IsSceneLoading) && _vm?.IsSceneLoading == true)
        {
            // Scene is about to change — nothing to flush, WebView content is read via messages
        }
        else if (e.PropertyName == nameof(EditorViewModel.CurrentScene))
        {
            if (_vm != null)
                SetContent(_vm.Content);
        }
        else if (e.PropertyName == nameof(EditorViewModel.IsDocumentOpen) && _vm?.IsDocumentOpen == true)
        {
            PushAutoReplacements();
            PushDialogueCorrection();
            PushEntityNames();
            PushGrammarCheckState();
            ExecuteScript("focusEditor()");
        }
        else if (e.PropertyName == nameof(EditorViewModel.IsDocumentOpen) && _vm?.IsDocumentOpen == false)
        {
            SetContent(string.Empty);
        }
        else if (e.PropertyName is nameof(EditorViewModel.EditorFontFamily) or nameof(EditorViewModel.EditorFontSize))
        {
            ApplyFont();
            ApplyBookWidth();
        }
        else if (e.PropertyName == nameof(EditorViewModel.BookParagraphSpacingEnabled))
        {
            ApplyBookParagraphSpacing();
        }
        else if (e.PropertyName is nameof(EditorViewModel.BookWidthEnabled) or nameof(EditorViewModel.BookEditorWidth))
        {
            ApplyBookWidth();
        }
        else if (e.PropertyName == nameof(EditorViewModel.AutoReplacement))
        {
            PushAutoReplacements();
        }
        else if (e.PropertyName == nameof(EditorViewModel.DialogueCorrection))
        {
            PushDialogueCorrection();
        }
        else if (e.PropertyName == nameof(EditorViewModel.GrammarCheck))
        {
            PushGrammarCheckState();
        }
    }

    private void OnEditorSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _vm?.FocusPeekExtension.OnEditorSizeChanged(e.NewSize);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private void ExecuteScript(string script)
    {
        if (_webViewReady && _webView != null)
            _ = _webView.InvokeScript(script);
    }

    /// <summary>
    /// Reinitializes the WebView2 control with a new language.
    /// WebView2's spellcheck / context-menu language is baked into the
    /// environment at creation time, so the only way to change it is to
    /// destroy and recreate the control.
    /// </summary>
    internal void ReinitializeWebView(string language)
    {
        if (_webView == null) return;

        // Store the language so the EnvironmentRequested handler can apply it
        // when the NEXT WebView2 environment is created.
        _reinitLanguage = language;

        // Capture current content before tearing down
        var currentContent = _vm?.IsDocumentOpen == true ? _vm.Content : null;

        // Unhook old WebView
        _webViewReady = false;
        _webView.EnvironmentRequested -= OnEnvironmentRequested;
        _webView.AdapterCreated -= OnAdapterCreated;
        _webView.NavigationStarted -= OnNavigationStarted;
        _webView.NavigationCompleted -= OnNavigationCompleted;
        _webView.WebMessageReceived -= OnWebMessageReceived;
        _webView.SizeChanged -= OnEditorSizeChanged;

        // Swap control: remove old, create new (preserve visibility state)
        var wasVisible = _webView.IsVisible;
        var parent = (Grid)_webView.Parent!;
        var idx = parent.Children.IndexOf(_webView);
        parent.Children.RemoveAt(idx);

        var newWebView = new NativeWebView
        {
            Name = "WebViewEditor",
            IsVisible = wasVisible,
            [!NativeWebView.IsHitTestVisibleProperty] =
                new Avalonia.Data.ReflectionBinding(nameof(EditorViewModel.IsDocumentOpen))
        };
        parent.Children.Insert(idx, newWebView);
        _webView = newWebView;

        // Update popup placement target
        FocusPeekPopup.PlacementTarget = newWebView;

        // Hook new WebView
        _webView.EnvironmentRequested += OnEnvironmentRequested;
        _webView.AdapterCreated += OnAdapterCreated;
        _webView.NavigationStarted += OnNavigationStarted;
        _webView.NavigationCompleted += OnNavigationCompleted;
        _webView.WebMessageReceived += OnWebMessageReceived;
        _webView.SizeChanged += OnEditorSizeChanged;

        // Queue content to be pushed after navigation completes
        _pendingContent = currentContent;

        NavigateToEditorPage();
    }

    private static string FormatColor(Color color)
        => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static string EscapeForSingleQuoteJs(string value)
        => value.Replace("\\", "\\\\").Replace("'", "\\'");

    private static string EscapeForJsonValue(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
