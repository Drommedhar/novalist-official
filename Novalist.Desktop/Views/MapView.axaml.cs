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
        // Menu/flyout popups open over the WebView's HWND (airspace problem);
        // snapshot-hide the WebView while a flyout is open. On close, don't
        // re-show if a flyout command just opened a dialog overlay — MainWindow's
        // UpdateWebViewVisibility owns that case.
        WireFlyoutAirspace(MapFileButton.Flyout as MenuFlyout);
        WireFlyoutAirspace(SplineToolButton.Flyout as MenuFlyout);
        WireFlyoutAirspace(TerrainToolButton.Flyout as MenuFlyout);
        WireFlyoutAirspace(BuildingToolButton.Flyout as MenuFlyout);
    }

    private void WireFlyoutAirspace(MenuFlyout? flyout)
    {
        if (flyout == null) return;
        flyout.Opened += (_, _) => SetWebViewVisible(false);
        flyout.Closed += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is MainWindow mw && mw.IsDialogOverlayOpen)
                return;
            SetWebViewVisible(true);
        };
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
            // The 3D map view loads the three.js WebGPU build, which ships only
            // as ES modules. Chromium blocks module scripts over file:// (the
            // scheme map.html is served with on Windows) unless this flag is
            // set. Scope is limited to this WebView2 process loading its own
            // bundled Assets/Map files.
            webView2.AdditionalBrowserArguments = "--allow-file-access-from-files";
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
            _vm.PushFocusOnPin = null;
            _vm.AddImageRequested = null;
        }
        _vm = DataContext as MapViewModel;
        if (_vm == null) return;
        _vm.PushMapJsonRequested = PushMapJson;
        _vm.PushModeRequested = PushMode;
        _vm.PushFocusOnPin = pinId => ExecuteScript($"focusOnPin('{EscapeJs(pinId)}')");
        _vm.PushActiveLayerRequested = layerId => ExecuteScript($"setActiveLayer('{EscapeJs(layerId)}')");
        _vm.PushToolModeRequested = mode =>
        {
            ExecuteScript($"setToolMode('{EscapeJs(mode)}')");
            // The WebView needs keyboard focus for tool hotkeys (e.g. building 'R').
            if (mode != "select") _webView?.Focus();
        };
        _vm.PushUpdatePinColor = (pinId, hex) =>
            ExecuteScript($"updatePinColor('{EscapeJs(pinId)}','{EscapeJs(hex)}')");
        _vm.PushUpdateImageZoomRange = (imageId, min, max) =>
            ExecuteScript($"updateImageZoomRange('{EscapeJs(imageId)}', {min.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {max.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
        _vm.PushIsolateImage = imageId => ExecuteScript($"setIsolatedImage('{EscapeJs(imageId)}')");
        _vm.PushIsolateElement = (kind, id) =>
            ExecuteScript($"setIsolatedElement('{EscapeJs(kind)}','{EscapeJs(id)}')");
        _vm.PushSetElementZoomRange = (kind, id, min, max) =>
            ExecuteScript($"setElementZoomRange('{EscapeJs(kind)}','{EscapeJs(id)}', {min.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {max.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
        _vm.PushSplineDraftType = (kind, preset) =>
            ExecuteScript($"setSplineDraftType('{EscapeJs(kind)}','{EscapeJs(preset)}')");
        _vm.PushTerrainDraftType = type =>
            ExecuteScript($"setTerrainDraftType('{EscapeJs(type)}')");
        _vm.PushBuildingDraftType = type =>
            ExecuteScript($"setBuildingDraftType('{EscapeJs(type)}')");
        _vm.PushSetBuildingScale = s =>
            ExecuteScript($"setBuildingScale({s.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
        _vm.PushSetBuildingType = (id, type) =>
            ExecuteScript($"setBuildingType('{EscapeJs(id)}','{EscapeJs(type)}')");
        _vm.PushSetBuildingRoof = (id, kind, pitch) =>
            ExecuteScript($"setBuildingRoof('{EscapeJs(id)}','{EscapeJs(kind)}', {pitch.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
        _vm.PushSetBuildingFloors = (id, count) =>
            ExecuteScript($"setBuildingFloors('{EscapeJs(id)}', {count})");
        _vm.PushSetBuildingPlanZoom = (id, z) =>
            ExecuteScript($"setBuildingPlanZoom('{EscapeJs(id)}', {z.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
        _vm.PushDeleteBuilding = id => ExecuteScript($"deleteBuilding('{EscapeJs(id)}')");
        _vm.PushMoveBuildingZ = (id, dir) => ExecuteScript($"moveBuildingZ('{EscapeJs(id)}', {dir})");
        _vm.PushEditBuildingPlan = id => ExecuteScript($"editBuildingPlan('{EscapeJs(id)}')");
        _vm.PushSetShapeType = (id, type) =>
            ExecuteScript($"setShapeType('{EscapeJs(id)}','{EscapeJs(type)}')");
        _vm.PushSetShapeColor = (id, hex) =>
            ExecuteScript($"setShapeColor('{EscapeJs(id)}','{EscapeJs(hex)}')");
        _vm.PushSetShapeSmooth = (id, smooth) =>
            ExecuteScript($"setShapeSmooth('{EscapeJs(id)}', {(smooth ? "true" : "false")})");
        _vm.PushSetShapeBlend = (id, strength) =>
            ExecuteScript($"setShapeBlend('{EscapeJs(id)}', {strength.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
        _vm.PushDeleteShape = id => ExecuteScript($"deleteShape('{EscapeJs(id)}')");
        _vm.PushMoveShapeZ = (id, dir) => ExecuteScript($"moveShapeZ('{EscapeJs(id)}', {dir})");
        _vm.PushSetBorderOutline = (color, width) =>
            ExecuteScript($"setBorderOutline('{EscapeJs(color)}', {width.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
        _vm.PushClearBorder = () => ExecuteScript("clearBorder()");
        _vm.PushSetSplinePreset = (id, kind, preset) =>
            ExecuteScript($"setSplinePreset('{EscapeJs(id)}','{EscapeJs(kind)}','{EscapeJs(preset)}')");
        _vm.PushDeleteSpline = id => ExecuteScript($"deleteSpline('{EscapeJs(id)}')");
        _vm.PushMoveSplineZ = (id, dir) => ExecuteScript($"moveSplineZ('{EscapeJs(id)}', {dir})");
        _vm.PushSetKnotBlend = (id, idx, factor) =>
            ExecuteScript($"setKnotBlend('{EscapeJs(id)}', {idx}, {factor.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
        _vm.PushSetKnotType = (id, idx, preset) =>
            ExecuteScript($"setKnotType('{EscapeJs(id)}', {idx}, '{EscapeJs(preset)}')");
        _vm.PushSetSplineMarkingStyle = (id, style) =>
            ExecuteScript($"setSplineMarkingStyle('{EscapeJs(id)}','{EscapeJs(style)}')");
        _vm.PushSetKnotMarkingStyle = (id, idx, style) =>
            ExecuteScript($"setKnotMarkingStyle('{EscapeJs(id)}', {idx}, '{EscapeJs(style)}')");
        _vm.PushSetKnotSharpness = (id, idx, val) =>
            ExecuteScript($"setKnotSharpness('{EscapeJs(id)}', {idx}, {val.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
        _vm.PushSetSplineColors = (id, casing, fill, marking) =>
            ExecuteScript($"setSplineColors('{EscapeJs(id)}','{EscapeJs(casing)}','{EscapeJs(fill)}','{EscapeJs(marking)}')");
        _vm.PushSetSplineClosed = (id, closed) =>
            ExecuteScript($"setSplineClosed('{EscapeJs(id)}', {(closed ? "true" : "false")})");
        _vm.PushSetLabelFontSize = (id, size) =>
            ExecuteScript($"setLabelFontSize('{EscapeJs(id)}', {size.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
        _vm.PushSetLabelFontFamily = (id, family) =>
            ExecuteScript($"setLabelFontFamily('{EscapeJs(id)}','{EscapeJs(family)}')");
        _vm.PushSetLabelAlign = (id, align) =>
            ExecuteScript($"setLabelAlign('{EscapeJs(id)}','{EscapeJs(align)}')");
        _vm.PushSetLabelColor = (id, hex) =>
            ExecuteScript($"setLabelColor('{EscapeJs(id)}','{EscapeJs(hex)}')");
        _vm.PushDeleteLabel = id => ExecuteScript($"deleteLabel('{EscapeJs(id)}')");
        _vm.AddImageRequested = (relPath, w, h) =>
            ExecuteScript($"addImageToMap('{EscapeJs(relPath)}', {w.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {h.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
        _vm.RequestMapJsonFromViewAsync = RequestMapJsonAsync;
        _vm.PushToggle3D = on =>
        {
            if (on)
            {
                // Start the loading overlay BEFORE the WebView is asked to
                // build, and hide the WebView so the user doesn't see a
                // black canvas while assets load.
                _vm.Is3DLoading = true;
                _vm.Loading3DProgress = 0.0;
                _vm.Loading3DStatus = Localization.Loc.T("map.loading3DInitialising");
                if (_webView != null) _webView.IsVisible = false;
            }
            else
            {
                // Exiting back to 2D — make sure the overlay is dismissed.
                _vm.Is3DLoading = false;
            }
            ExecuteScript(on ? "Map3D.enter()" : "Map3D.exit()");
        };
    }

    // Steps map3d.js emits during enter(). Each one bumps progress + updates
    // the loading overlay's status text. Order MUST match the JS sequence in
    // map3d.js Map3D.enter().
    private void UpdateLoadingFromStep(string? step)
    {
        if (_vm == null) return;
        switch (step)
        {
            case "before-build":
                _vm.Loading3DProgress = 0.10;
                _vm.Loading3DStatus = Localization.Loc.T("map.loading3DAssets");
                break;
            case "after-tree-assets":
                _vm.Loading3DProgress = 0.55;
                _vm.Loading3DStatus = Localization.Loc.T("map.loading3DScene");
                break;
            case "after-build":
                _vm.Loading3DProgress = 0.85;
                _vm.Loading3DStatus = Localization.Loc.T("map.loading3DCamera");
                break;
            case "after-frame":
                _vm.Loading3DProgress = 0.95;
                _vm.Loading3DStatus = Localization.Loc.T("map.loading3DAlmost");
                break;
        }
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
        PushMapStrings();
    }

    private async Task PushEntityOptionsAsync()
    {
        if (_vm?.PushEntityOptions == null) return;
        await _vm.PushEntityOptions.Invoke(json =>
            ExecuteScript($"setEntityOptions({System.Text.Json.JsonSerializer.Serialize(json)})"));
    }

    // Localised strings for the JS-drawn context menus (knot + label menus).
    private void PushMapStrings()
    {
        var json = JsonSerializer.Serialize(new
        {
            knotDelete = Localization.Loc.T("map.knotDelete"),
            knotClearOverride = Localization.Loc.T("map.knotClearOverride"),
            knotTypePrefix = Localization.Loc.T("map.knotTypePrefix"),
            labelEditText = Localization.Loc.T("map.labelEditText"),
            ctxDelete = Localization.Loc.T("map.ctxDelete"),
            clipHint = Localization.Loc.T("map.clipHint"),
            clipDone = Localization.Loc.T("map.clipDone"),
            clipClear = Localization.Loc.T("map.clipClear"),
            clipCancel = Localization.Loc.T("map.clipCancel"),
            splineEditHint = Localization.Loc.T("map.splineEditHint"),
            splineEditDone = Localization.Loc.T("map.splineEditDone"),
            widthHandleTip = Localization.Loc.T("map.widthHandleTip"),
            ctxEdit = Localization.Loc.T("map.ctxEdit"),
            moveToLayer = Localization.Loc.T("map.moveToLayer"),
            knotAddBefore = Localization.Loc.T("map.knotAddBefore"),
            knotAddAfter = Localization.Loc.T("map.knotAddAfter"),
            knotClearDirection = Localization.Loc.T("map.knotClearDirection"),
            splineDelete = Localization.Loc.T("map.splineDelete"),
            rotHandleTip = Localization.Loc.T("map.rotHandleTip"),
            shapeDelete = Localization.Loc.T("map.shapeDelete"),
            terrainEditHint = Localization.Loc.T("map.terrainEditHint"),
            buildingDelete = Localization.Loc.T("map.buildingDelete"),
            bldRotateTip = Localization.Loc.T("map.bldRotateTip"),
            fpWall = Localization.Loc.T("map.fpWall"),
            fpDoor = Localization.Loc.T("map.fpDoor"),
            fpWindow = Localization.Loc.T("map.fpWindow"),
            fpStairs = Localization.Loc.T("map.fpStairs"),
            fpLabel = Localization.Loc.T("map.fpLabel"),
            fpPin = Localization.Loc.T("map.fpPin"),
            fpFlipSide = Localization.Loc.T("map.fpFlipSide"),
            fpHint = Localization.Loc.T("map.fpHint"),
            fpNoFloorsWarn = Localization.Loc.T("map.fpNoFloorsWarn"),
            cancel = Localization.Loc.T("map.cancel"),
            borderEditHint = Localization.Loc.T("map.borderEditHint"),
            bbIdle = Localization.Loc.T("map.bbIdle"),
            bbIdleHint = Localization.Loc.T("map.bbIdleHint"),
            bbClipLabel = Localization.Loc.T("map.bbClipLabel"),
            bbFloorPlanLabel = Localization.Loc.T("map.bbFloorPlanLabel"),
            bbSplineLabel = Localization.Loc.T("map.bbSplineLabel"),
            bbTerrainLabel = Localization.Loc.T("map.bbTerrainLabel"),
            bbBorderLabel = Localization.Loc.T("map.bbBorderLabel"),
            bbBuildingLabel = Localization.Loc.T("map.bbBuildingLabel"),
            bbPinLabel = Localization.Loc.T("map.bbPinLabel"),
            bbLabelSelLabel = Localization.Loc.T("map.bbLabelSelLabel"),
            bbImageLabel = Localization.Loc.T("map.bbImageLabel"),
            bbSplineDraft = Localization.Loc.T("map.bbSplineDraft"),
            bbTerrainDraft = Localization.Loc.T("map.bbTerrainDraft"),
            bbBorderDraft = Localization.Loc.T("map.bbBorderDraft"),
            bbBuildingPlace = Localization.Loc.T("map.bbBuildingPlace"),
            bbBuildingHint = Localization.Loc.T("map.bbBuildingHint"),
            bbBuildingEditHint = Localization.Loc.T("map.bbBuildingEditHint"),
            bbEditFloorPlan = Localization.Loc.T("map.bbEditFloorPlan"),
            bbSelectedHint = Localization.Loc.T("map.bbSelectedHint"),
            bbClickToAddPoints = Localization.Loc.T("map.bbClickToAddPoints"),
            kbdEscCancelEnterCommit = Localization.Loc.T("map.kbdEscCancelEnterCommit"),
            kbdBuildingPlace = Localization.Loc.T("map.kbdBuildingPlace"),
            kbdFinishDelete = Localization.Loc.T("map.kbdFinishDelete"),
            kbdFinish = Localization.Loc.T("map.kbdFinish"),
            kbdEscDeselect = Localization.Loc.T("map.kbdEscDeselect"),
            kbdDeleteRemoveEscDeselect = Localization.Loc.T("map.kbdDeleteRemoveEscDeselect"),
            hudMap = Localization.Loc.T("map.hudMap"),
            hudZoom = Localization.Loc.T("map.hudZoom"),
            hudLayer = Localization.Loc.T("map.hudLayer"),
            hudSplineDraft = Localization.Loc.T("map.hudSplineDraft"),
            hudTerrainDraft = Localization.Loc.T("map.hudTerrainDraft"),
            hudBorderDraft = Localization.Loc.T("map.hudBorderDraft"),
            hudWallDraft = Localization.Loc.T("map.hudWallDraft"),
            hudKnots = Localization.Loc.T("map.hudKnots"),
            hudVerts = Localization.Loc.T("map.hudVerts"),
            hudPts = Localization.Loc.T("map.hudPts"),
            bbLayer = Localization.Loc.T("map.bbLayer"),
            bbMore = Localization.Loc.T("map.bbMore"),
            bbColors = Localization.Loc.T("map.bbColors"),
            bbKnot = Localization.Loc.T("map.bbKnot"),
            // Bottom-bar selects need pre-localized [value, label] pairs.
            buildingTypesJson = JsonSerializer.Serialize(new[] {
                new[] { "singleFamily", Localization.Loc.T("map.bldSingleFamily") },
                new[] { "rowHome",      Localization.Loc.T("map.bldRowHome") },
                new[] { "school",       Localization.Loc.T("map.bldSchool") },
                new[] { "police",       Localization.Loc.T("map.bldPolice") },
                new[] { "fireStation",  Localization.Loc.T("map.bldFireStation") },
                new[] { "hall",         Localization.Loc.T("map.bldHall") },
                new[] { "playground",   Localization.Loc.T("map.bldPlayground") },
                new[] { "trainStation", Localization.Loc.T("map.bldTrainStation") },
            }),
            roofKindsJson = JsonSerializer.Serialize(new[] {
                new[] { "gable", Localization.Loc.T("map.roofGable") },
                new[] { "hip",   Localization.Loc.T("map.roofHip") },
                new[] { "flat",  Localization.Loc.T("map.roofFlat") },
            }),
            terrainTypesJson = JsonSerializer.Serialize(new[] {
                new[] { "grass",    Localization.Loc.T("map.terrainGrass") },
                new[] { "forest",   Localization.Loc.T("map.terrainForest") },
                new[] { "concrete", Localization.Loc.T("map.terrainConcrete") },
                new[] { "sand",     Localization.Loc.T("map.terrainSand") },
                new[] { "hills",    Localization.Loc.T("map.terrainHills") },
                new[] { "mountain", Localization.Loc.T("map.terrainMountain") },
                new[] { "water",    Localization.Loc.T("map.terrainWater") },
            }),
            splinePresetsJson = JsonSerializer.Serialize(new[] {
                new[] { "road:motorway",    Localization.Loc.T("map.roadMotorway") },
                new[] { "road:primary",     Localization.Loc.T("map.roadPrimary") },
                new[] { "road:secondary",   Localization.Loc.T("map.roadSecondary") },
                new[] { "road:residential", Localization.Loc.T("map.roadResidential") },
                new[] { "road:service",     Localization.Loc.T("map.roadService") },
                new[] { "road:pedestrian",  Localization.Loc.T("map.roadPedestrian") },
                new[] { "road:trail",       Localization.Loc.T("map.roadTrail") },
                new[] { "road:track",       Localization.Loc.T("map.roadTrack") },
                new[] { "river:brook",      Localization.Loc.T("map.riverBrook") },
                new[] { "river:stream",     Localization.Loc.T("map.riverStream") },
                new[] { "river:river",      Localization.Loc.T("map.riverRiver") },
                new[] { "river:canal",      Localization.Loc.T("map.riverCanal") },
                new[] { "river:estuary",    Localization.Loc.T("map.riverEstuary") },
            }),
            markingStylesJson = JsonSerializer.Serialize(new[] {
                new[] { "",             Localization.Loc.T("map.markPresetDefault") },
                new[] { "none",         Localization.Loc.T("map.markNone") },
                new[] { "single",       Localization.Loc.T("map.markSingle") },
                new[] { "dashed",       Localization.Loc.T("map.markDashed") },
                new[] { "double",       Localization.Loc.T("map.markDouble") },
                new[] { "solid-dashed", Localization.Loc.T("map.markSolidDashed") },
            }),
            knotTypesJson = JsonSerializer.Serialize(new[] {
                new[] { "",             Localization.Loc.T("map.knotClearType") },
                new[] { "motorway",     Localization.Loc.T("map.roadMotorway") },
                new[] { "primary",      Localization.Loc.T("map.roadPrimary") },
                new[] { "secondary",    Localization.Loc.T("map.roadSecondary") },
                new[] { "residential",  Localization.Loc.T("map.roadResidential") },
                new[] { "service",      Localization.Loc.T("map.roadService") },
                new[] { "pedestrian",   Localization.Loc.T("map.roadPedestrian") },
                new[] { "trail",        Localization.Loc.T("map.roadTrail") },
                new[] { "track",        Localization.Loc.T("map.roadTrack") },
                new[] { "brook",        Localization.Loc.T("map.riverBrook") },
                new[] { "stream",       Localization.Loc.T("map.riverStream") },
                new[] { "river",        Localization.Loc.T("map.riverRiver") },
                new[] { "canal",        Localization.Loc.T("map.riverCanal") },
                new[] { "estuary",      Localization.Loc.T("map.riverEstuary") },
            }),
            // Selected-element bottom-bar labels.
            pinLabel = Localization.Loc.T("map.pinLabel"),
            pinLabelWatermark = Localization.Loc.T("map.pinLabelWatermark"),
            pinEntity = Localization.Loc.T("map.pinEntity"),
            pinEntityWatermark = Localization.Loc.T("map.pinEntityWatermark"),
            pinColor = Localization.Loc.T("map.pinColor"),
            labelFontSize = Localization.Loc.T("map.labelFontSize"),
            labelColor = Localization.Loc.T("map.labelColor"),
            alignLeft = Localization.Loc.T("map.alignLeft"),
            alignCenter = Localization.Loc.T("map.alignCenter"),
            alignRight = Localization.Loc.T("map.alignRight"),
            shapeType = Localization.Loc.T("map.shapeType"),
            shapeColor = Localization.Loc.T("map.shapeColor"),
            shapeSmooth = Localization.Loc.T("map.shapeSmooth"),
            shapeBlend = Localization.Loc.T("map.shapeBlend"),
            shapeForward = Localization.Loc.T("map.shapeForward"),
            shapeBackward = Localization.Loc.T("map.shapeBackward"),
            buildingType = Localization.Loc.T("map.buildingType"),
            buildingRoof = Localization.Loc.T("map.buildingRoof"),
            buildingFloors = Localization.Loc.T("map.buildingFloors"),
            buildingPlanZoom = Localization.Loc.T("map.buildingPlanZoom"),
            roofPitch = Localization.Loc.T("map.roofPitch"),
            splineType = Localization.Loc.T("map.splineType"),
            splineClosed = Localization.Loc.T("map.splineClosed"),
            splineMarking = Localization.Loc.T("map.splineMarking"),
            splineBlend = Localization.Loc.T("map.splineBlend"),
            knotSharpness = Localization.Loc.T("map.knotSharpness"),
            colorCasing = Localization.Loc.T("map.colorCasing"),
            colorFill = Localization.Loc.T("map.colorFill"),
            colorMarking = Localization.Loc.T("map.colorMarking"),
            colorReset = Localization.Loc.T("map.colorReset"),
            borderOutlineColor = Localization.Loc.T("map.borderOutlineColor"),
            borderOutlineWidth = Localization.Loc.T("map.borderOutlineWidth"),
            borderClear = Localization.Loc.T("map.borderClear"),
            clip = Localization.Loc.T("map.imageMenuClip"),
            imageMinZoom = Localization.Loc.T("map.imageMinZoom"),
            imageMaxZoom = Localization.Loc.T("map.imageMaxZoom"),
        });
        ExecuteScript($"setMapStrings({JsonSerializer.Serialize(json)})");
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
                    _ = PushEntityOptionsAsync();
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
                case "cancelSplineMode":
                    if (_vm != null) _vm.IsSplineMode = false;
                    break;
                case "cancelLabelPlace":
                    if (_vm != null) _vm.IsLabelPlaceMode = false;
                    break;
                case "cancelTerrainMode":
                    if (_vm != null) _vm.IsTerrainMode = false;
                    break;
                case "cancelBuildingMode":
                    if (_vm != null) _vm.IsBuildingMode = false;
                    break;
                case "cancelBorderMode":
                    // No VM flag for border tool mode; JS sends this after commit. No-op.
                    break;
                case "floorPlanEditEntered":
                    _vm?.OnFloorPlanEditEntered();
                    break;
                case "floorPlanEditExited":
                    _vm?.OnFloorPlanEditExited();
                    break;
                case "pinDeselected":
                    _vm?.SetSelectedPin(null, null);
                    break;
                case "toast":
                    Console.Error.WriteLine("[MapView] toast: " + (doc.RootElement.TryGetProperty("text", out var tt) ? tt.GetString() : ""));
                    break;
                case "buildingSelected":
                    var bId = doc.RootElement.TryGetProperty("buildingId", out var bid) ? bid.GetString() : null;
                    var bType = doc.RootElement.TryGetProperty("buildingType", out var bt) ? bt.GetString() ?? "singleFamily" : "singleFamily";
                    var bRoof = doc.RootElement.TryGetProperty("roofKind", out var brk) ? brk.GetString() ?? "gable" : "gable";
                    var bPitch = doc.RootElement.TryGetProperty("roofPitch", out var brp) ? brp.GetDouble() : 0.5;
                    var bFloors = doc.RootElement.TryGetProperty("floorCount", out var bfc) ? bfc.GetInt32() : 1;
                    var bPlanZoom = doc.RootElement.TryGetProperty("planMinZoom", out var bpz) ? bpz.GetDouble() : 4;
                    _vm?.SetSelectedBuilding(bId, bType, bRoof, bPitch, bFloors, bPlanZoom);
                    break;
                case "buildingDeselected":
                    _vm?.SetSelectedBuilding(null, "singleFamily", "gable", 0.5, 1, 4);
                    break;
                case "shapeSelected":
                    var shpId = doc.RootElement.TryGetProperty("shapeId", out var shi) ? shi.GetString() : null;
                    var shpType = doc.RootElement.TryGetProperty("shapeType", out var sht) ? sht.GetString() ?? "grass" : "grass";
                    var shpColor = doc.RootElement.TryGetProperty("color", out var shc) ? shc.GetString() ?? "#8db360" : "#8db360";
                    var shpSmooth = !doc.RootElement.TryGetProperty("smooth", out var shs) || shs.GetBoolean();
                    var shpBlend = doc.RootElement.TryGetProperty("blend", out var shb) ? shb.GetDouble() : 0;
                    _vm?.SetSelectedShape(shpId, shpType, shpColor, shpSmooth, shpBlend);
                    break;
                case "shapeDeselected":
                    _vm?.SetSelectedShape(null, "grass", "#8db360", true, 0);
                    break;
                case "borderSelected":
                    var bColor = doc.RootElement.TryGetProperty("outlineColor", out var boc) ? boc.GetString() ?? "#1c1a18" : "#1c1a18";
                    var bWidth = doc.RootElement.TryGetProperty("outlineWidth", out var bow) ? bow.GetDouble() : 4;
                    _vm?.SetBorderSelection(true, bColor, bWidth);
                    break;
                case "borderDeselected":
                    _vm?.SetBorderSelection(false, "#1c1a18", 4);
                    break;
                case "labelSelected":
                    var lblId = doc.RootElement.TryGetProperty("labelId", out var lid2) ? lid2.GetString() : null;
                    var lblSize = doc.RootElement.TryGetProperty("fontSize", out var lfs) ? lfs.GetDouble() : 18;
                    var lblFam = doc.RootElement.TryGetProperty("fontFamily", out var lff) ? lff.GetString() ?? "" : "";
                    var lblAlign = doc.RootElement.TryGetProperty("align", out var lal) ? lal.GetString() ?? "center" : "center";
                    var lblColor = doc.RootElement.TryGetProperty("color", out var lcl) ? lcl.GetString() ?? "#ffffff" : "#ffffff";
                    _vm?.SetSelectedLabel(lblId, lblSize, lblFam, lblAlign, lblColor);
                    break;
                case "labelDeselected":
                    _vm?.SetSelectedLabel(null, 18, "", "center", "#ffffff");
                    break;
                case "splineSelected":
                    var splId = doc.RootElement.TryGetProperty("splineId", out var spi) ? spi.GetString() : null;
                    var splKind = doc.RootElement.TryGetProperty("kind", out var spk) ? spk.GetString() : "road";
                    var splPreset = doc.RootElement.TryGetProperty("preset", out var spp) ? spp.GetString() : "";
                    var selKnot = doc.RootElement.TryGetProperty("selectedKnot", out var skn) ? skn.GetInt32() : 0;
                    var knotType = doc.RootElement.TryGetProperty("knotType", out var ktp2) ? ktp2.GetString() ?? "" : "";
                    var knotBlend = doc.RootElement.TryGetProperty("knotBlend", out var kbp) ? kbp.GetDouble() : 1.0;
                    var markStyle = doc.RootElement.TryGetProperty("markingStyle", out var msp) ? msp.GetString() ?? "" : "";
                    var knotMark = doc.RootElement.TryGetProperty("knotMarking", out var kmp) ? kmp.GetString() ?? "" : "";
                    var casingHex = doc.RootElement.TryGetProperty("casingColor", out var cch) ? cch.GetString() ?? "" : "";
                    var fillHex = doc.RootElement.TryGetProperty("fillColor", out var fch) ? fch.GetString() ?? "" : "";
                    var markHex = doc.RootElement.TryGetProperty("markingColor", out var mch) ? mch.GetString() ?? "" : "";
                    var splClosed = doc.RootElement.TryGetProperty("closed", out var spcl) && spcl.GetBoolean();
                    var knotSharp = doc.RootElement.TryGetProperty("knotSharpness", out var ksh) ? ksh.GetDouble() : 0;
                    _vm?.SetSelectedSpline(splId, splKind ?? "road", splPreset ?? "", selKnot, knotType, knotBlend,
                        markStyle, knotMark, casingHex, fillHex, markHex, splClosed, knotSharp);
                    break;
                case "splineDeselected":
                    _vm?.SetSelectedSpline(null, "road", string.Empty, -1, string.Empty, 1.0,
                        string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, false, 0);
                    break;
                // requestMoveToLayer removed — inline bottom-bar layer select.
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
                // pinContext removed — pin properties are edited inline in the bottom bar.
                // imageContext removed — image properties + layer move are inline.
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
                case "map3dStep":
                    var step = doc.RootElement.TryGetProperty("step", out var sp) ? sp.GetString() : null;
                    Console.Error.WriteLine($"[Map3D] step={step}");
                    UpdateLoadingFromStep(step);
                    break;
                case "map3dLoading":
                    Console.Error.WriteLine("[Map3D] loading");
                    if (_vm != null)
                    {
                        _vm.Is3DLoading = true;
                        _vm.Loading3DProgress = 0.05;
                        _vm.Loading3DStatus = "Loading 3D view…";
                    }
                    if (_webView != null) _webView.IsVisible = false;
                    break;
                case "map3dEntered":
                    Console.Error.WriteLine("[Map3D] entered");
                    if (_vm != null)
                    {
                        _vm.Loading3DProgress = 1.0;
                        _vm.Loading3DStatus = Localization.Loc.T("map.loading3DReady");
                        _vm.Is3DLoading = false;
                    }
                    if (_webView != null) _webView.IsVisible = true;
                    break;
                case "map3dExited":
                    if (_vm != null) _vm.Is3DLoading = false;
                    if (_webView != null) _webView.IsVisible = true;
                    break;
                case "map3dError":
                    var errMsg = doc.RootElement.TryGetProperty("message", out var em)
                        ? em.GetString() ?? "(unknown)"
                        : "(unknown)";
                    Console.Error.WriteLine($"[Map3D] ERROR: {errMsg}");
                    Toast.Show?.Invoke(
                        Localization.Loc.T("map.loading3DError", errMsg),
                        ToastSeverity.Error);
                    if (_vm != null)
                    {
                        _vm.Is3DLoading = false;
                        // Flip back to 2D — setter triggers PushToggle3D(false),
                        // which calls Map3D.exit() so the JS side tears down too.
                        _vm.Is3DMode = false;
                    }
                    if (_webView != null) _webView.IsVisible = true;
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

    // HandleRequestMoveToLayerAsync / HandleImageContextAsync / HandlePinContextAsync
    // removed — every element now exposes layer move + properties inline in the bottom
    // bar (no LayerPickerDialog, no MapPinDialog).

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

    private void OnAddPinClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        // Place an empty pin at the viewport center; user fills fields in the bottom bar.
        ExecuteScript("addPinAtCenter('', '', '', '')");
    }

    private Task HandlePlacePinAtAsync(JsonElement root)
    {
        if (_vm == null) return Task.CompletedTask;
        var x = root.TryGetProperty("x", out var xp) ? xp.GetDouble() : 0;
        var y = root.TryGetProperty("y", out var yp) ? yp.GetDouble() : 0;
        var xs = x.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var ys = y.ToString(System.Globalization.CultureInfo.InvariantCulture);
        // Drop an empty pin and immediately auto-select it so the bottom bar
        // shows its inline editor.
        ExecuteScript($"addPinAtPoint({xs}, {ys}, '', '', '', '')");
        _vm.IsPinPlaceMode = false;
        return Task.CompletedTask;
    }

    private void OnDeleteSelectedClick(object? sender, RoutedEventArgs e)
        => ExecuteScript("deleteSelected()");

    private void OnEditClipClick(object? sender, RoutedEventArgs e)
        => ExecuteScript("toggleClipEditOnSelected()");

    private void OnSplinePresetClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is not Control c || c.Tag is not string tag) return;
        var parts = tag.Split(':');
        if (parts.Length != 2) return;
        _vm.SetSplinePreset(parts[0], parts[1]);
    }

    // Terrain type pick from the tool-rail flyout → enter terrain-draw mode.
    private void OnTerrainTypeClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is Control c && c.Tag is string type)
            _vm.SetTerrainType(type);
    }

    // Terrain type pick from the selected-shape properties panel.
    private void OnShapeTypeClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is Control c && c.Tag is string type)
            _vm.SetSelectedShapeType(type);
    }

    // Building type pick from the tool-rail flyout → enter placement mode.
    private void OnBuildingTypeClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is Control c && c.Tag is string type)
            _vm.SetBuildingType(type);
    }

    // Building type pick from the selected-building properties panel.
    private void OnSelectedBuildingTypeClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is Control c && c.Tag is string type)
            _vm.SetSelectedBuildingType(type);
    }

    // Roof kind pick from the selected-building properties panel.
    private void OnBuildingRoofKindClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is Control c && c.Tag is string kind)
            _vm.SetSelectedBuildingRoofKind(kind);
    }

    // Custom-profile pick from the tool-button flyout → enter draw mode with it.
    private void OnToolCustomProfilePick(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is Control c && c.Tag is MapProfileChoice ch)
            _vm.SetSplinePreset(ch.Kind, ch.PresetKey);
    }

    // Custom-profile pick from the selected-spline properties flyout.
    private void OnPropCustomProfilePick(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is Control c && c.Tag is MapProfileChoice ch)
            _vm.ChangeSelectedSplinePreset(ch.Kind, ch.PresetKey);
    }

    private void OnManageProfilesClick(object? sender, RoutedEventArgs e)
        => _vm?.ManageProfilesCommand.Execute(null);

    // Preset picker inside the selected-spline properties block.
    private void OnSplinePropPresetClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is not Control c || c.Tag is not string tag) return;
        var parts = tag.Split(':');
        if (parts.Length != 2) return;
        _vm.ChangeSelectedSplinePreset(parts[0], parts[1]);
    }

    // Per-knot type-override picker in the spline properties block.
    // MenuItem.Tag is the preset key ("" clears the override).
    private void OnKnotTypeClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is not MenuItem mi) return;
        _vm.SetSelectedKnotType(mi.Tag as string ?? string.Empty);
    }

    // Spline-level centerline picker. MenuItem.Tag is the style key ("" = preset default).
    private void OnSplineMarkingStyleClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is not MenuItem mi) return;
        _vm.SetSelectedSplineMarkingStyle(mi.Tag as string ?? string.Empty);
    }

    // Label text alignment. Button.Tag is "left" | "center" | "right".
    private void OnLabelAlignClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is Control c && c.Tag is string align)
            _vm.SetSelectedLabelAlign(align);
    }

    // Per-knot centerline override. MenuItem.Tag is the style key ("" = inherit spline).
    private void OnKnotMarkingStyleClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is not MenuItem mi) return;
        _vm.SetSelectedKnotMarkingStyle(mi.Tag as string ?? string.Empty);
    }

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

    private void OnPropElementMinZoomChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is not NumericUpDown nud || nud.Tag is not LayerElementRow row) return;
        var newVal = e.NewValue.HasValue ? (double?)(double)e.NewValue.Value : null;
        if (Nullable.Equals(newVal, row.MinZoom)) return;
        _ = _vm.SetElementZoomRangeAsync(row, newVal, row.MaxZoom);
    }

    private void OnPropElementMaxZoomChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is not NumericUpDown nud || nud.Tag is not LayerElementRow row) return;
        var newVal = e.NewValue.HasValue ? (double?)(double)e.NewValue.Value : null;
        if (Nullable.Equals(newVal, row.MaxZoom)) return;
        _ = _vm.SetElementZoomRangeAsync(row, row.MinZoom, newVal);
    }

    private void OnPropElementIsolateClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is Control c && c.Tag is LayerElementRow row)
            _vm.ToggleIsolateElement(row);
    }

    private void ExecuteScript(string script)
    {
        if (_webViewReady && _webView != null)
            _ = _webView.InvokeScript(script);
    }

    private static string EscapeJs(string value)
        => (value ?? string.Empty).Replace("\\", "\\\\").Replace("'", "\\'");
}
