using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Reactive;
using Avalonia.Threading;
using Novalist.Core.Models;
using Avalonia.Platform;
using Novalist.Desktop.Utilities;
using Novalist.Desktop.ViewModels;

namespace Novalist.Desktop.Views;

public partial class MapView : UserControl
{
    private MapViewModel? _vm;
    private NativeWebView? _webView;
    private bool _webViewReady;
    private string? _pendingMapJson;
    private string? _pendingMode;
    private Avalonia.Controls.Image? _snapshotImage;

    /// <summary>
    /// Hides or shows the native WebView to work around the airspace problem
    /// where the WebView2 HWND renders on top of all Avalonia overlays. Mirrors
    /// EditorView.SetWebViewVisible — captures a bitmap snapshot on hide.
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
        _snapshotImage = new Avalonia.Controls.Image
        {
            Stretch = Avalonia.Media.Stretch.Uniform,
            IsHitTestVisible = false,
            IsVisible = false,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        var idx = MapHost.Children.IndexOf(_webView);
        MapHost.Children.Insert(idx + 1, _snapshotImage);
    }

    public MapView()
    {
        InitializeComponent();
        TryCreateWebView();
        DataContextChanged += OnDataContextChanged;
        // The File menu popup opens over the WebView's HWND (airspace problem);
        // hide the WebView (snapshot) while the flyout is open.
        if (MapFileButton.Flyout is MenuFlyout fileFlyout)
        {
            fileFlyout.Opened += (_, _) => SetWebViewVisible(false);
            fileFlyout.Closed += (_, _) =>
            {
                // A menu command (New/Rename/Delete map) may have opened a dialog
                // overlay before the flyout closed — don't re-show the WebView on
                // top of it. MainWindow's UpdateWebViewVisibility owns that case.
                if (TopLevel.GetTopLevel(this) is MainWindow mw && mw.IsDialogOverlayOpen)
                    return;
                SetWebViewVisible(true);
            };
        }
    }

    private void TryCreateWebView()
    {
        try
        {
            _webView = new NativeWebView();
            MapHost.Children.Insert(0, _webView);
            _webView.EnvironmentRequested += OnEnvironmentRequested;
            _webView.NavigationCompleted += OnNavCompleted;
            _webView.WebMessageReceived += OnWebMessageReceived;
            NavigateToMapPage();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MapView] WebView create failed: {ex}");
            _webView = null;
        }
    }

    private void OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs e)
    {
        if (e is WindowsWebView2EnvironmentRequestedEventArgs webView2)
        {
            webView2.UserDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Novalist", "WebView2", "map");
        }
    }

    private void NavigateToMapPage()
    {
        if (_webView == null) return;
        var path = ResolveMapHtmlPath();
        if (path == null) return;
        if (OperatingSystem.IsMacOS())
            _webView.NavigateToString(File.ReadAllText(path));
        else
            _webView.Source = new Uri(path);
    }

    private static string? ResolveMapHtmlPath()
    {
        var basePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Map", "map.html");
        if (File.Exists(basePath)) return basePath;
        var macBundle = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "Resources", "Assets", "Map", "map.html"));
        if (File.Exists(macBundle)) return macBundle;
        return null;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null)
        {
            _vm.PushMapJsonRequested = null;
            _vm.PushModeRequested = null;
            _vm.AddImageRequested = null;
        }
        _vm = DataContext as MapViewModel;
        if (_vm == null) return;
        _vm.PushMapJsonRequested = PushMapJson;
        _vm.PushModeRequested = PushMode;
        _vm.PushActiveLayerRequested = layerId => ExecuteScript($"setActiveLayer('{EscapeJs(layerId)}')");
        _vm.PushToolModeRequested = mode => ExecuteScript($"setToolMode('{EscapeJs(mode)}')");
        _vm.PushUpdatePinColor = (pinId, hex) =>
            ExecuteScript($"updatePinColor('{EscapeJs(pinId)}','{EscapeJs(hex)}')");
        _vm.PushUpdateImageZoomRange = (imageId, min, max) =>
            ExecuteScript($"updateImageZoomRange('{EscapeJs(imageId)}', {min.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {max.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
        _vm.PushIsolateImage = imageId => ExecuteScript($"setIsolatedImage('{EscapeJs(imageId)}')");
        _vm.AddImageRequested = (relPath, w, h) =>
            ExecuteScript($"addImageToMap('{EscapeJs(relPath)}', {w.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {h.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
        _vm.RequestMapJsonFromViewAsync = RequestMapJsonAsync;
    }

    private void OnNavCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        _webViewReady = true;
        PushImageBaseUrl();
        PushContextMenuLabels();
        if (_pendingMapJson != null) { PushMapJson(_pendingMapJson); _pendingMapJson = null; }
        if (_pendingMode != null) { PushMode(_pendingMode); _pendingMode = null; }
    }

    private void PushContextMenuLabels()
    {
        var move = Localization.Loc.T("map.imageMenuMove");
        var clip = Localization.Loc.T("map.imageMenuClip");
        var del = Localization.Loc.T("map.imageMenuDelete");
        ExecuteScript($"setContextMenuLabels('{EscapeJs(move)}','{EscapeJs(clip)}','{EscapeJs(del)}')");
    }

    private void PushImageBaseUrl()
    {
        if (_vm == null) return;
        var bookRoot = App.ProjectService.ActiveBookRoot;
        if (bookRoot == null) { Console.Error.WriteLine("[MapView] PushImageBaseUrl: bookRoot is null"); return; }
        // Image paths are stored relative to the book root (e.g. "Images/foo.png"),
        // matching the convention used by EntityService.GetProjectImages.
        var uri = new Uri(bookRoot + Path.DirectorySeparatorChar).AbsoluteUri;
        Console.Error.WriteLine($"[MapView] setImageBaseUrl => {uri}");
        ExecuteScript($"setImageBaseUrl('{EscapeJs(uri)}')");
    }

    private void PushMapJson(string json)
    {
        if (!_webViewReady) { _pendingMapJson = json; Console.Error.WriteLine("[MapView] PushMapJson queued (webview not ready)"); return; }
        Console.Error.WriteLine($"[MapView] PushMapJson length={json?.Length ?? 0}");
        PushImageBaseUrl();
        ExecuteScript($"setMapData({JsonSerializer.Serialize(json)})");
    }

    private void PushMode(string mode)
    {
        if (!_webViewReady) { _pendingMode = mode; return; }
        ExecuteScript($"setMode('{EscapeJs(mode)}')");
    }

    private TaskCompletionSource<string?>? _pendingJsonResponse;

    private Task<string?> RequestMapJsonAsync()
    {
        if (!_webViewReady) return Task.FromResult<string?>(null);
        _pendingJsonResponse = new TaskCompletionSource<string?>();
        ExecuteScript("sendMessage({type:'mapJson', json: getMapData()})");
        return _pendingJsonResponse.Task;
    }

    private void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Body)) return;
        try
        {
            using var doc = JsonDocument.Parse(e.Body);
            var type = doc.RootElement.GetProperty("type").GetString();
            switch (type)
            {
                case "ready":
                    Console.Error.WriteLine("[MapView] JS ready");
                    _webViewReady = true;
                    PushImageBaseUrl();
                    PushContextMenuLabels();
                    if (_pendingMapJson != null) { var p = _pendingMapJson; _pendingMapJson = null; PushMapJson(p); }
                    if (_pendingMode != null) { var p = _pendingMode; _pendingMode = null; PushMode(p); }
                    break;
                case "mapChanged":
                    Console.Error.WriteLine("[MapView] JS mapChanged");
                    _ = HandleMapChangedAsync();
                    break;
                case "pinClick":
                    var entityId = doc.RootElement.TryGetProperty("entityId", out var eid) ? eid.GetString() : null;
                    if (!string.IsNullOrEmpty(entityId))
                        OpenEntityByIdAsync(entityId!);
                    break;
                case "placePinAt":
                    _ = HandlePlacePinAtAsync(doc.RootElement);
                    break;
                case "cancelPinPlace":
                    if (_vm != null) _vm.IsPinPlaceMode = false;
                    break;
                case "pinSelected":
                    var pid = doc.RootElement.TryGetProperty("pinId", out var pp) ? pp.GetString() : null;
                    var pcolor = doc.RootElement.TryGetProperty("color", out var cc) ? cc.GetString() : null;
                    _vm?.SetSelectedPin(pid, pcolor);
                    break;
                case "imageSelected":
                    var imgId = doc.RootElement.TryGetProperty("imageId", out var ii) ? ii.GetString() : null;
                    if (!string.IsNullOrEmpty(imgId)) _vm?.SelectImageFromView(imgId!);
                    break;
                case "selectionCleared":
                    _vm?.SetSelectedPin(null, null);
                    break;
                case "pinContext":
                    _ = HandlePinContextAsync(doc.RootElement);
                    break;
                case "imageContext":
                    _ = HandleImageContextAsync(doc.RootElement);
                    break;
                case "mapJson":
                    var js = doc.RootElement.TryGetProperty("json", out var jp) ? jp.GetString() : null;
                    _pendingJsonResponse?.TrySetResult(js);
                    _pendingJsonResponse = null;
                    break;
                case "viewChanged":
                    _ = HandleViewChangedAsync(doc.RootElement);
                    break;
                case "log":
                    var txt = doc.RootElement.TryGetProperty("text", out var tp) ? tp.GetString() : null;
                    Console.Error.WriteLine($"[MapJS] {txt}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MapView] OnWebMessageReceived parse error: {ex}");
        }
    }

    private async Task HandleMapChangedAsync()
    {
        if (_vm == null) return;
        await _vm.PersistFromViewAsync();
    }

    private Task HandleViewChangedAsync(JsonElement root)
    {
        if (_vm?.ActiveMap == null) return Task.CompletedTask;
        var cx = root.TryGetProperty("centerX", out var x) ? x.GetDouble() : 0;
        var cy = root.TryGetProperty("centerY", out var y) ? y.GetDouble() : 0;
        var z = root.TryGetProperty("zoom", out var zp) ? zp.GetDouble() : 1;
        return _vm.UpdateInitialViewAsync(cx, cy, z);
    }

    private async Task HandleImageContextAsync(JsonElement root)
    {
        if (_vm == null) return;
        var imageId = root.GetProperty("imageId").GetString() ?? string.Empty;
        var layerId = root.GetProperty("layerId").GetString() ?? string.Empty;
        if (string.IsNullOrEmpty(imageId) || string.IsNullOrEmpty(layerId)) return;
        var targetLayerId = await _vm.PromptMoveImageToLayerAsync(layerId);
        if (string.IsNullOrEmpty(targetLayerId) || targetLayerId == layerId) return;
        ExecuteScript($"moveImageToLayer('{EscapeJs(layerId)}','{EscapeJs(imageId)}','{EscapeJs(targetLayerId)}')");
    }

    private async Task HandlePinContextAsync(JsonElement root)
    {
        if (_vm == null) return;
        var pinId = root.GetProperty("pinId").GetString() ?? string.Empty;
        var label = root.TryGetProperty("label", out var lp) ? lp.GetString() ?? string.Empty : string.Empty;
        var entityId = root.TryGetProperty("entityId", out var eid) ? eid.GetString() ?? string.Empty : string.Empty;
        var entityType = root.TryGetProperty("entityType", out var et) ? et.GetString() ?? string.Empty : string.Empty;
        var color = root.TryGetProperty("color", out var cp) ? cp.GetString() ?? string.Empty : string.Empty;

        var result = await _vm.RequestPinEditAsync(pinId, label, entityId, entityType, color);
        if (result == null) return; // cancelled
        if (result.Value.Delete)
        {
            ExecuteScript($"deletePin('{EscapeJs(pinId)}')");
        }
        else
        {
            ExecuteScript($"updatePin('{EscapeJs(pinId)}','{EscapeJs(result.Value.Label)}','{EscapeJs(result.Value.EntityType)}','{EscapeJs(result.Value.EntityId)}','{EscapeJs(result.Value.Color)}')");
        }
    }

    private void OpenEntityByIdAsync(string entityId)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow mw && mw.DataContext is MainWindowViewModel main)
        {
            _ = main.OpenEntityByIdAsync(entityId);
        }
    }

    private void OnMapItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control c && c.Tag is MapReference mr && _vm != null)
            _vm.SelectedMap = mr;
    }

    private void OnOpenMapItemClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control c && c.Tag is MapReference mr && _vm != null)
        {
            _vm.SelectedMap = mr;
            (MapFileButton.Flyout as MenuFlyout)?.Hide();
        }
    }

    private void OnRenameMapClick(object? sender, RoutedEventArgs e)
    {
        var mr = FindMapRefFromMenu(sender);
        if (mr != null && _vm != null) _vm.RenameMapCommand.Execute(mr);
    }

    private void OnDeleteMapClick(object? sender, RoutedEventArgs e)
    {
        var mr = FindMapRefFromMenu(sender);
        if (mr != null && _vm != null) _vm.DeleteMapConfirmCommand.Execute(mr);
    }

    private static MapReference? FindMapRefFromMenu(object? sender)
    {
        if (sender is not MenuItem mi) return null;
        if (mi.DataContext is MapReference dc) return dc;
        Avalonia.StyledElement? p = mi.Parent;
        while (p is not null and not Avalonia.Controls.ContextMenu) p = p.Parent;
        if (p is Avalonia.Controls.ContextMenu cm && cm.Tag is MapReference tag) return tag;
        return null;
    }

    private async void OnAddPinClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        var label = await _vm.PromptPinDetailsAsync();
        if (label == null) return; // cancelled
        var (text, entityId, entityType, color) = label.Value;
        ExecuteScript($"addPinAtCenter('{EscapeJs(text)}', '{EscapeJs(entityType)}', '{EscapeJs(entityId)}', '{EscapeJs(color)}')");
    }

    private async Task HandlePlacePinAtAsync(JsonElement root)
    {
        if (_vm == null) return;
        var x = root.TryGetProperty("x", out var xp) ? xp.GetDouble() : 0;
        var y = root.TryGetProperty("y", out var yp) ? yp.GetDouble() : 0;
        var details = await _vm.PromptPinDetailsAsync();
        if (details == null)
        {
            _vm.IsPinPlaceMode = false;
            return;
        }
        var (text, entityId, entityType, color) = details.Value;
        var xs = x.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var ys = y.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ExecuteScript($"addPinAtPoint({xs}, {ys}, '{EscapeJs(text)}', '{EscapeJs(entityType)}', '{EscapeJs(entityId)}', '{EscapeJs(color)}')");
        _vm.IsPinPlaceMode = false;
    }

    private void OnDeleteSelectedClick(object? sender, RoutedEventArgs e)
        => ExecuteScript("deleteSelected()");

    private void OnEditClipClick(object? sender, RoutedEventArgs e)
        => ExecuteScript("toggleClipEditOnSelected()");

    private void OnZoomToFitClick(object? sender, RoutedEventArgs e)
        => ExecuteScript("zoomToFit()");


    private void OnResetViewClick(object? sender, RoutedEventArgs e)
        => ExecuteScript("resetView()");

    // ── Layer-panel row handlers ────────────────────────────────────────
    private void OnNodeRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is Control c && c.Tag is LayerNodeRow row)
            _vm.SelectNodeCommand.Execute(row);
    }

    private void OnNodeToggleExpand(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is Control c && c.Tag is LayerNodeRow row)
            _vm.ToggleExpandCommand.Execute(row);
    }

    private void OnNodeToggleHidden(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is Control c && c.Tag is LayerNodeRow row)
            _vm.ToggleNodeHiddenCommand.Execute(row);
    }

    private void OnNodeToggleLocked(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is Control c && c.Tag is LayerNodeRow row)
            _vm.ToggleNodeLockedCommand.Execute(row);
    }

    private void OnNodeAddChild(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is Control c && c.Tag is LayerNodeRow row)
            _vm.AddChildLayerCommand.Execute(row);
    }

    private void OnNodeDelete(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is Control c && c.Tag is LayerNodeRow row)
            _vm.DeleteNodeCommand.Execute(row);
    }

    // Inline rename: double-click name → editable; commit on Enter / lost focus.
    private void OnNodeNameDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control c && c.Tag is LayerNodeRow row)
        {
            row.IsRenaming = true;
            // Focus the textbox once it materialises.
            Dispatcher.UIThread.Post(() =>
            {
                if (c is Panel panel)
                    foreach (var child in panel.Children)
                        if (child is TextBox tb && tb.IsVisible) { tb.Focus(); tb.SelectAll(); }
            }, DispatcherPriority.Input);
        }
    }

    private void OnNodeNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb || tb.Tag is not LayerNodeRow row) return;
        if (e.Key == Key.Enter) { CommitNodeRename(tb, row); e.Handled = true; }
        else if (e.Key == Key.Escape) { row.IsRenaming = false; e.Handled = true; }
    }

    private void OnNodeNameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.Tag is LayerNodeRow row && row.IsRenaming)
            CommitNodeRename(tb, row);
    }

    private void CommitNodeRename(TextBox tb, LayerNodeRow row)
    {
        row.IsRenaming = false;
        if (_vm != null) _ = _vm.CommitNodeRenameAsync(row, tb.Text ?? string.Empty);
    }

    // ── Drag-and-drop reorder / re-parent ───────────────────────────────
    private static readonly DataFormat<string> NodeDragFormat =
        DataFormat.CreateInProcessFormat<string>("novalist/map-layer-node");

    private LayerNodeRow? _dragRow;
    private Point _dragStart;
    private bool _dragArmed;
    private PointerPressedEventArgs? _dragPressedArgs;

    private void OnNodeDragPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control c && c.Tag is LayerNodeRow row)
        {
            _dragRow = row;
            _dragStart = e.GetPosition(this);
            _dragArmed = true;
            _dragPressedArgs = e;
            // Pressing anywhere on the row selects it (not just the name).
            _vm?.SelectNodeCommand.Execute(row);
        }
    }

    private async void OnNodeDragMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragArmed || _dragRow == null || _dragPressedArgs == null) return;
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.Y - _dragStart.Y) < 6 && Math.Abs(pos.X - _dragStart.X) < 6) return;
        _dragArmed = false;
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(NodeDragFormat, _dragRow.NodeId));
        try { await DragDrop.DoDragDropAsync(_dragPressedArgs, transfer, DragDropEffects.Move); }
        catch { /* drag cancelled */ }
        _dragRow = null;
        _dragPressedArgs = null;
    }

    private void OnNodeDragReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragArmed = false;
        _dragPressedArgs = null;
    }

    private void OnNodeDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer?.Contains(NodeDragFormat) == true
            ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnNodeDrop(object? sender, DragEventArgs e)
    {
        if (_vm == null) return;
        var dragId = e.DataTransfer?.TryGetValue(NodeDragFormat);
        if (string.IsNullOrEmpty(dragId)) return;
        if (sender is not Control c || c.Tag is not LayerNodeRow targetRow) return;
        // Drop position from cursor Y within the row: top third = Before,
        // bottom third = After, middle = Inside (nest as child).
        var pos = e.GetPosition(c);
        var h = c.Bounds.Height;
        NodeDropPosition where;
        if (pos.Y < h * 0.3) where = NodeDropPosition.Before;
        else if (pos.Y > h * 0.7) where = NodeDropPosition.After;
        else where = NodeDropPosition.Inside;
        _ = _vm.MoveNodeAsync(dragId!, targetRow.NodeId, where);
        e.Handled = true;
    }

    // Empty space below all rows = move the dragged layer back to the root level.
    private void OnRootDropZoneDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer?.Contains(NodeDragFormat) == true
            ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnRootDropZoneDrop(object? sender, DragEventArgs e)
    {
        if (_vm == null) return;
        var dragId = e.DataTransfer?.TryGetValue(NodeDragFormat);
        if (string.IsNullOrEmpty(dragId)) return;
        _ = _vm.MoveNodeToRootAsync(dragId!);
        e.Handled = true;
    }

    // ── Properties section handlers ─────────────────────────────────────
    private void OnPropOpacityChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_vm?.SelectedNode == null) return;
        if (!e.NewValue.HasValue) return;
        var opacity = (double)e.NewValue.Value / 100.0;
        if (Math.Abs(opacity - _vm.SelectedNode.Opacity) < 0.005) return;
        _ = _vm.SetNodeOpacityAsync(_vm.SelectedNode, opacity);
    }

    private void OnPropMinZoomChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        var row = _vm?.SelectedNode;
        if (row == null) return;
        var newVal = e.NewValue.HasValue ? (double?)(double)e.NewValue.Value : null;
        if (Nullable.Equals(newVal, row.MinZoom)) return;
        _ = _vm!.SetNodeZoomRangeAsync(row, newVal, row.MaxZoom);
    }

    private void OnPropMaxZoomChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        var row = _vm?.SelectedNode;
        if (row == null) return;
        var newVal = e.NewValue.HasValue ? (double?)(double)e.NewValue.Value : null;
        if (Nullable.Equals(newVal, row.MaxZoom)) return;
        _ = _vm!.SetNodeZoomRangeAsync(row, row.MinZoom, newVal);
    }

    private void OnPropConnectedSetClick(object? sender, RoutedEventArgs e)
    {
        if (_vm?.SelectedNode == null) return;
        if (sender is CheckBox cb) _ = _vm.SetNodeConnectedSetAsync(_vm.SelectedNode, cb.IsChecked == true);
    }

    private void OnPropActiveMemberChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_vm?.SelectedNode == null) return;
        if (sender is ComboBox cb && cb.SelectedValue is string id)
            _ = _vm.SetNodeActiveMemberAsync(_vm.SelectedNode, id);
    }

    private void OnPropImageMinZoomChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is not NumericUpDown nud || nud.Tag is not ImageRow row) return;
        var newVal = e.NewValue.HasValue ? (double?)(double)e.NewValue.Value : null;
        if (Nullable.Equals(newVal, row.MinZoom)) return;
        _ = _vm.SetImageZoomRangeAsync(row, newVal, row.MaxZoom);
    }

    private void OnPropImageMaxZoomChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is not NumericUpDown nud || nud.Tag is not ImageRow row) return;
        var newVal = e.NewValue.HasValue ? (double?)(double)e.NewValue.Value : null;
        if (Nullable.Equals(newVal, row.MaxZoom)) return;
        _ = _vm.SetImageZoomRangeAsync(row, row.MinZoom, newVal);
    }

    private void OnPropImageIsolateClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is Control c && c.Tag is ImageRow row)
            _vm.ToggleIsolateImage(row);
    }

    private void ExecuteScript(string script)
    {
        if (_webViewReady && _webView != null)
            _ = _webView.InvokeScript(script);
    }

    private static string EscapeJs(string value)
        => (value ?? string.Empty).Replace("\\", "\\\\").Replace("'", "\\'");
}
