using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Localization;

namespace Novalist.Desktop.ViewModels;

/// <summary>Where a dragged node should land relative to a drop target.</summary>
public enum NodeDropPosition { Before, After, Inside }

public partial class MapViewModel : ObservableObject
{
    private readonly IMapService _mapService;
    private readonly IProjectService _projectService;

    [ObservableProperty]
    private ObservableCollection<MapReference> _maps = new();

    [ObservableProperty]
    private MapReference? _selectedMap;

    [ObservableProperty]
    private MapData? _activeMap;

    [ObservableProperty]
    private bool _isEditMode = true;

    [ObservableProperty]
    private bool _isLayerPanelOpen = true;

    [ObservableProperty]
    private bool _isPinPlaceMode;

    [ObservableProperty]
    private bool _isLabelPlaceMode;

    [ObservableProperty]
    private bool _isSplineMode;

    [ObservableProperty]
    private bool _isTerrainMode;

    [ObservableProperty]
    private bool _isBuildingMode;

    /// <summary>Global placement-size multiplier for new buildings.</summary>
    [ObservableProperty]
    private double _buildingScale = 1;

    public string SplineKind { get; private set; } = "road";
    public string SplinePreset { get; private set; } = "residential";
    public string TerrainType { get; private set; } = "grass";
    public string BuildingType { get; private set; } = "singleFamily";

    /// <summary>Host-supplied: set the global building placement scale.</summary>
    public Action<double>? PushSetBuildingScale { get; set; }

    partial void OnBuildingScaleChanged(double value) => PushSetBuildingScale?.Invoke(value);

    /// <summary>True while the map editor is showing the 3D view instead of the 2D editor.</summary>
    [ObservableProperty]
    private bool _is3DMode;

    /// <summary>True while the 3D view is initialising — WebView is hidden, loading overlay shown.</summary>
    [ObservableProperty]
    private bool _is3DLoading;

    /// <summary>Human-readable description of the current 3D load step.</summary>
    [ObservableProperty]
    private string _loading3DStatus = "Preparing 3D view…";

    /// <summary>0..1 progress through the 3D load sequence.</summary>
    [ObservableProperty]
    private double _loading3DProgress;

    /// <summary>Host-supplied: enter/exit the WebView 3D view.</summary>
    public Action<bool>? PushToggle3D { get; set; }

    partial void OnIs3DModeChanged(bool value) => PushToggle3D?.Invoke(value);

    /// <summary>User-authored profiles available for the spline preset pickers.</summary>
    [ObservableProperty]
    private ObservableCollection<MapProfileChoice> _customProfileChoices = new();

    /// <summary>Host-supplied: open the profile editor; returns the edited list or null.</summary>
    public Func<List<MapProfile>, Task<List<MapProfile>?>>? ManageProfilesRequested { get; set; }

    private void RebuildCustomProfileChoices()
    {
        CustomProfileChoices.Clear();
        if (ActiveMap == null) return;
        foreach (var p in ActiveMap.CustomProfiles)
            CustomProfileChoices.Add(new MapProfileChoice(p.Kind, "custom:" + p.Id, p.Name));
    }

    [RelayCommand]
    private async Task ManageProfilesAsync()
    {
        if (ActiveMap == null || ManageProfilesRequested == null) return;
        var result = await ManageProfilesRequested.Invoke(ActiveMap.CustomProfiles);
        if (result == null) return; // cancelled
        ActiveMap.CustomProfiles = result;
        RebuildCustomProfileChoices();
        await _mapService.SaveMapAsync(ActiveMap);
        PushMapJson();
    }

    // ── Selected spline (driven by map clicks) ──────────────────────────
    [ObservableProperty]
    private string? _selectedSplineId;

    public string SelectedSplineKind { get; private set; } = "road";
    public string SelectedSplinePreset { get; private set; } = string.Empty;

    [ObservableProperty]
    private string _selectedSplineMarkingStyle = string.Empty;

    public string SelectedSplineMarkingDisplay =>
        string.IsNullOrEmpty(SelectedSplineMarkingStyle) ? "(preset default)" : SelectedSplineMarkingStyle;

    // Per-part colour overrides for the selected spline.
    [ObservableProperty] private Avalonia.Media.Color _selectedSplineCasingColor = Avalonia.Media.Colors.Gray;
    [ObservableProperty] private Avalonia.Media.Color _selectedSplineFillColor = Avalonia.Media.Colors.White;
    [ObservableProperty] private Avalonia.Media.Color _selectedSplineMarkingColor = Avalonia.Media.Colors.White;

    // Closed-loop flag for the selected spline (roundabouts, lakes).
    [ObservableProperty] private bool _selectedSplineClosed;

    /// <summary>Host-supplied: set the selected spline's closed-loop flag.</summary>
    public Action<string, bool>? PushSetSplineClosed { get; set; }

    partial void OnSelectedSplineClosedChanged(bool value)
    {
        if (_suspendSplineColorPush || string.IsNullOrEmpty(SelectedSplineId)) return;
        PushSetSplineClosed?.Invoke(SelectedSplineId!, value);
    }

    private bool _suspendSplineColorPush;

    /// <summary>Host-supplied: set the selected spline's part colours
    /// (id, casingHex, fillHex, markingHex; "" on any clears that override).</summary>
    public Action<string, string, string, string>? PushSetSplineColors { get; set; }

    partial void OnSelectedSplineCasingColorChanged(Avalonia.Media.Color value) => PushSplineColors();
    partial void OnSelectedSplineFillColorChanged(Avalonia.Media.Color value) => PushSplineColors();
    partial void OnSelectedSplineMarkingColorChanged(Avalonia.Media.Color value) => PushSplineColors();

    private static string Hex(Avalonia.Media.Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private void PushSplineColors()
    {
        if (_suspendSplineColorPush || string.IsNullOrEmpty(SelectedSplineId)) return;
        PushSetSplineColors?.Invoke(SelectedSplineId,
            Hex(SelectedSplineCasingColor), Hex(SelectedSplineFillColor), Hex(SelectedSplineMarkingColor));
    }

    [RelayCommand]
    private void ResetSplineColors()
    {
        if (string.IsNullOrEmpty(SelectedSplineId)) return;
        // Empty strings clear the overrides; JS re-emits splineSelected so the
        // pickers snap back to the preset colours.
        PushSetSplineColors?.Invoke(SelectedSplineId, string.Empty, string.Empty, string.Empty);
    }

    // The single currently-focused knot of the selected spline.
    [ObservableProperty]
    private int _selectedKnotIndex = -1;

    [ObservableProperty]
    private string _selectedKnotType = string.Empty;

    [ObservableProperty]
    private double _selectedKnotBlend = 1.0;

    [ObservableProperty]
    private string _selectedKnotMarking = string.Empty;

    [ObservableProperty]
    private double _selectedKnotSharpness;

    public bool HasSelectedSpline => !string.IsNullOrEmpty(SelectedSplineId);
    public string SelectedKnotLabel => SelectedKnotIndex >= 0 ? $"Knot {SelectedKnotIndex + 1}" : string.Empty;
    public string SelectedKnotTypeDisplay =>
        string.IsNullOrEmpty(SelectedKnotType) ? "(base type)" : SelectedKnotType;
    public string SelectedKnotMarkingDisplay =>
        string.IsNullOrEmpty(SelectedKnotMarking) ? "(spline default)" : SelectedKnotMarking;

    /// <summary>Host-supplied: change a committed spline's preset (id, kind, preset).</summary>
    public Action<string, string, string>? PushSetSplinePreset { get; set; }

    /// <summary>Host-supplied: delete a committed spline by id.</summary>
    public Action<string>? PushDeleteSpline { get; set; }

    /// <summary>Host-supplied: reorder a spline in its layer z-stack (id, +1 = forward, -1 = back).</summary>
    public Action<string, int>? PushMoveSplineZ { get; set; }

    /// <summary>Host-supplied: set a knot's blend factor (id, knotIndex, 0..1).</summary>
    public Action<string, int, double>? PushSetKnotBlend { get; set; }

    /// <summary>Host-supplied: set a knot's type override (id, knotIndex, preset; "" clears).</summary>
    public Action<string, int, string>? PushSetKnotType { get; set; }

    /// <summary>Host-supplied: set a spline's centerline marking style (id, style; "" = preset default).</summary>
    public Action<string, string>? PushSetSplineMarkingStyle { get; set; }

    /// <summary>Host-supplied: set a knot's centerline override (id, knotIndex, style; "" = inherit).</summary>
    public Action<string, int, string>? PushSetKnotMarkingStyle { get; set; }

    /// <summary>Host-supplied: set a knot's corner sharpness (id, knotIndex, 0..1).</summary>
    public Action<string, int, double>? PushSetKnotSharpness { get; set; }

    private bool _suspendKnotPush;

    public void SetSelectedSpline(string? id, string kind, string preset,
        int selKnot, string knotType, double knotBlend, string markingStyle, string knotMarking,
        string casingHex, string fillHex, string markingHex, bool closed, double knotSharpness)
    {
        SelectedSplineId = id;
        SelectedSplineKind = string.IsNullOrEmpty(kind) ? "road" : kind;
        SelectedSplinePreset = preset ?? string.Empty;
        _suspendKnotPush = true;
        SelectedKnotIndex = id == null ? -1 : selKnot;
        SelectedKnotType = knotType ?? string.Empty;
        SelectedKnotBlend = knotBlend;
        SelectedKnotMarking = knotMarking ?? string.Empty;
        SelectedKnotSharpness = knotSharpness;
        SelectedSplineMarkingStyle = markingStyle ?? string.Empty;
        _suspendKnotPush = false;
        _suspendSplineColorPush = true;
        if (Avalonia.Media.Color.TryParse(casingHex, out var cc)) SelectedSplineCasingColor = cc;
        if (Avalonia.Media.Color.TryParse(fillHex, out var fc)) SelectedSplineFillColor = fc;
        if (Avalonia.Media.Color.TryParse(markingHex, out var mc)) SelectedSplineMarkingColor = mc;
        SelectedSplineClosed = closed;
        _suspendSplineColorPush = false;
        OnPropertyChanged(nameof(HasSelectedSpline));
        OnPropertyChanged(nameof(SelectedSplineKind));
        OnPropertyChanged(nameof(SelectedSplinePreset));
        OnPropertyChanged(nameof(SelectedKnotLabel));
        OnPropertyChanged(nameof(SelectedKnotTypeDisplay));
        OnPropertyChanged(nameof(SelectedKnotMarkingDisplay));
        OnPropertyChanged(nameof(SelectedSplineMarkingDisplay));
    }

    partial void OnSelectedKnotMarkingChanged(string value)
        => OnPropertyChanged(nameof(SelectedKnotMarkingDisplay));

    /// <summary>Set the focused knot's centerline override ("" = inherit the spline style).</summary>
    public void SetSelectedKnotMarkingStyle(string style)
    {
        if (string.IsNullOrEmpty(SelectedSplineId) || SelectedKnotIndex < 0) return;
        SelectedKnotMarking = style;
        PushSetKnotMarkingStyle?.Invoke(SelectedSplineId, SelectedKnotIndex, style);
    }

    partial void OnSelectedSplineMarkingStyleChanged(string value)
        => OnPropertyChanged(nameof(SelectedSplineMarkingDisplay));

    /// <summary>Set the selected spline's centerline marking style ("" = preset default).</summary>
    public void SetSelectedSplineMarkingStyle(string style)
    {
        if (string.IsNullOrEmpty(SelectedSplineId)) return;
        SelectedSplineMarkingStyle = style;
        PushSetSplineMarkingStyle?.Invoke(SelectedSplineId, style);
    }

    partial void OnSelectedSplineIdChanged(string? value)
        => OnPropertyChanged(nameof(HasSelectedSpline));

    partial void OnSelectedKnotIndexChanged(int value)
        => OnPropertyChanged(nameof(SelectedKnotLabel));

    partial void OnSelectedKnotTypeChanged(string value)
        => OnPropertyChanged(nameof(SelectedKnotTypeDisplay));

    partial void OnSelectedKnotBlendChanged(double value)
    {
        if (_suspendKnotPush || string.IsNullOrEmpty(SelectedSplineId) || SelectedKnotIndex < 0) return;
        PushSetKnotBlend?.Invoke(SelectedSplineId, SelectedKnotIndex, value);
    }

    partial void OnSelectedKnotSharpnessChanged(double value)
    {
        if (_suspendKnotPush || string.IsNullOrEmpty(SelectedSplineId) || SelectedKnotIndex < 0) return;
        PushSetKnotSharpness?.Invoke(SelectedSplineId, SelectedKnotIndex, value);
    }

    /// <summary>Set the focused knot's type override ("" clears).</summary>
    public void SetSelectedKnotType(string preset)
    {
        if (string.IsNullOrEmpty(SelectedSplineId) || SelectedKnotIndex < 0) return;
        SelectedKnotType = preset;
        PushSetKnotType?.Invoke(SelectedSplineId, SelectedKnotIndex, preset);
    }

    public void ChangeSelectedSplinePreset(string kind, string preset)
    {
        if (string.IsNullOrEmpty(SelectedSplineId)) return;
        SelectedSplineKind = kind;
        SelectedSplinePreset = preset;
        OnPropertyChanged(nameof(SelectedSplineKind));
        OnPropertyChanged(nameof(SelectedSplinePreset));
        PushSetSplinePreset?.Invoke(SelectedSplineId, kind, preset);
    }

    [RelayCommand]
    private void DeleteSelectedSpline()
    {
        if (string.IsNullOrEmpty(SelectedSplineId)) return;
        PushDeleteSpline?.Invoke(SelectedSplineId);
        SetSelectedSpline(null, "road", string.Empty, -1, string.Empty, 1.0,
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, false, 0);
    }

    [RelayCommand]
    private void MoveSelectedSplineForward()
    {
        if (!string.IsNullOrEmpty(SelectedSplineId)) PushMoveSplineZ?.Invoke(SelectedSplineId!, 1);
    }

    [RelayCommand]
    private void MoveSelectedSplineBackward()
    {
        if (!string.IsNullOrEmpty(SelectedSplineId)) PushMoveSplineZ?.Invoke(SelectedSplineId!, -1);
    }

    /// <summary>Flattened, depth-aware list of every currently-expanded layer node —
    /// the layer panel binds to this. Rebuilt whenever the tree or expansion changes.</summary>
    [ObservableProperty]
    private ObservableCollection<LayerNodeRow> _visibleRows = new();

    /// <summary>The node selected in the panel; drives the Properties section.</summary>
    [ObservableProperty]
    private LayerNodeRow? _selectedNode;

    public bool HasSelectedNode => SelectedNode != null;

    private readonly List<LayerNodeRow> _rootRows = new();

    // ── Host-supplied delegates ─────────────────────────────────────────
    public Action<string>? PushToolModeRequested { get; set; }
    public Action<string>? PushActiveLayerRequested { get; set; }
    public Action<string>? PushMapJsonRequested { get; set; }
    public Func<Task<string?>>? RequestMapJsonFromViewAsync { get; set; }
    public Action<string>? PushModeRequested { get; set; }
    public Action<string, double, double>? AddImageRequested { get; set; }
    public Func<string, string, string, Task<string?>>? ShowInputDialog { get; set; }
    public Func<Task<(string RelativePath, double Width, double Height)?>>? PickImageRequested { get; set; }
    public Func<Task<(string Label, string EntityId, string EntityType, string Color)?>>? PromptPinDetailsRequested { get; set; }
    public Func<string, string, string, string, string, Task<(string Label, string EntityId, string EntityType, string Color, bool Delete)?>>? RequestPinEditDialog { get; set; }
    public Func<string, string, Task<bool>>? ShowConfirmDialog { get; set; }
    public Func<List<(string Id, string Name)>, string, Task<string?>>? PromptLayerPicker { get; set; }
    public Action<string, string>? PushUpdatePinColor { get; set; }
    public Action<string, double, double>? PushUpdateImageZoomRange { get; set; }
    /// <summary>Host-supplied: isolate a single image in the view (empty = clear).</summary>
    public Action<string>? PushIsolateImage { get; set; }
    /// <summary>Host-supplied: isolate a spline / pin / label (empty kind+id = clear).</summary>
    public Action<string, string>? PushIsolateElement { get; set; }
    /// <summary>Host-supplied: set a spline / pin / label visible-zoom range (kind, id, min, max).</summary>
    public Action<string, string, double, double>? PushSetElementZoomRange { get; set; }

    /// <summary>Host-supplied: tell WebView which road/river preset new splines use.</summary>
    public Action<string, string>? PushSplineDraftType { get; set; }
    /// <summary>Host-supplied: tell WebView which terrain type new shapes use.</summary>
    public Action<string>? PushTerrainDraftType { get; set; }
    /// <summary>Host-supplied: tell WebView which building type new buildings use.</summary>
    public Action<string>? PushBuildingDraftType { get; set; }

    private bool AnyOtherToolMode(string except)
        => (except != "pin" && IsPinPlaceMode)
        || (except != "label" && IsLabelPlaceMode)
        || (except != "spline" && IsSplineMode)
        || (except != "terrain" && IsTerrainMode)
        || (except != "building" && IsBuildingMode);

    partial void OnIsPinPlaceModeChanged(bool value)
    {
        if (value) { IsSplineMode = false; IsLabelPlaceMode = false; IsTerrainMode = false; IsBuildingMode = false; }
        PushToolModeRequested?.Invoke(value ? "add-pin" : "select");
    }

    partial void OnIsLabelPlaceModeChanged(bool value)
    {
        if (value) { IsSplineMode = false; IsPinPlaceMode = false; IsTerrainMode = false; IsBuildingMode = false; }
        PushToolModeRequested?.Invoke(value ? "add-label" : "select");
    }

    partial void OnIsSplineModeChanged(bool value)
    {
        if (value)
        {
            IsPinPlaceMode = false;
            IsLabelPlaceMode = false;
            IsTerrainMode = false;
            IsBuildingMode = false;
            PushSplineDraftType?.Invoke(SplineKind, SplinePreset);
            PushToolModeRequested?.Invoke("spline");
        }
        else if (!AnyOtherToolMode("spline"))
        {
            PushToolModeRequested?.Invoke("select");
        }
    }

    partial void OnIsTerrainModeChanged(bool value)
    {
        if (value)
        {
            IsPinPlaceMode = false;
            IsLabelPlaceMode = false;
            IsSplineMode = false;
            IsBuildingMode = false;
            PushTerrainDraftType?.Invoke(TerrainType);
            PushToolModeRequested?.Invoke("terrain");
        }
        else if (!AnyOtherToolMode("terrain"))
        {
            PushToolModeRequested?.Invoke("select");
        }
    }

    partial void OnIsBuildingModeChanged(bool value)
    {
        if (value)
        {
            IsPinPlaceMode = false;
            IsLabelPlaceMode = false;
            IsSplineMode = false;
            IsTerrainMode = false;
            PushBuildingDraftType?.Invoke(BuildingType);
            PushToolModeRequested?.Invoke("building");
        }
        else if (!AnyOtherToolMode("building"))
        {
            PushToolModeRequested?.Invoke("select");
        }
    }

    /// <summary>Picks a road/river preset and enters spline-draw mode.</summary>
    public void SetSplinePreset(string kind, string preset)
    {
        SplineKind = kind;
        SplinePreset = preset;
        if (IsSplineMode)
        {
            PushSplineDraftType?.Invoke(kind, preset);
            PushToolModeRequested?.Invoke("spline");
        }
        else
        {
            IsSplineMode = true; // OnIsSplineModeChanged pushes type + mode
        }
    }

    /// <summary>Picks a terrain type and enters terrain-draw mode.</summary>
    public void SetTerrainType(string type)
    {
        TerrainType = type;
        if (IsTerrainMode)
        {
            PushTerrainDraftType?.Invoke(type);
            PushToolModeRequested?.Invoke("terrain");
        }
        else
        {
            IsTerrainMode = true; // OnIsTerrainModeChanged pushes type + mode
        }
    }

    /// <summary>Picks a building type and enters building-placement mode.</summary>
    public void SetBuildingType(string type)
    {
        BuildingType = type;
        if (IsBuildingMode)
        {
            PushBuildingDraftType?.Invoke(type);
            PushToolModeRequested?.Invoke("building");
        }
        else
        {
            IsBuildingMode = true; // OnIsBuildingModeChanged pushes type + mode
        }
    }

    // ── Pin selection (driven by map clicks) ────────────────────────────
    [ObservableProperty]
    private string? _selectedPinId;

    [ObservableProperty]
    private Avalonia.Media.Color _selectedPinColor = Avalonia.Media.Colors.Goldenrod;

    public bool HasSelectedPin => !string.IsNullOrEmpty(SelectedPinId);

    private bool _suspendPinColorPush;

    public void SetSelectedPin(string? pinId, string? colorHex)
    {
        _suspendPinColorPush = true;
        SelectedPinId = pinId;
        if (!string.IsNullOrWhiteSpace(colorHex) && Avalonia.Media.Color.TryParse(colorHex, out var c))
            SelectedPinColor = c;
        _suspendPinColorPush = false;
        OnPropertyChanged(nameof(HasSelectedPin));
    }

    partial void OnSelectedPinIdChanged(string? value)
        => OnPropertyChanged(nameof(HasSelectedPin));

    partial void OnSelectedPinColorChanged(Avalonia.Media.Color value)
    {
        if (_suspendPinColorPush) return;
        if (string.IsNullOrEmpty(SelectedPinId)) return;
        var hex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
        PushUpdatePinColor?.Invoke(SelectedPinId, hex);
    }

    // ── Label selection (driven by map clicks) ──────────────────────────
    [ObservableProperty]
    private string? _selectedLabelId;

    [ObservableProperty]
    private double _selectedLabelFontSize = 18;

    [ObservableProperty]
    private string _selectedLabelFontFamily = string.Empty;

    [ObservableProperty]
    private string _selectedLabelAlign = "center";

    [ObservableProperty]
    private Avalonia.Media.Color _selectedLabelColor = Avalonia.Media.Colors.White;

    public bool HasSelectedLabel => !string.IsNullOrEmpty(SelectedLabelId);

    public string SelectedLabelFontDisplay =>
        LabelFontChoices.FirstOrDefault(c => c.Css == SelectedLabelFontFamily)?.Name ?? "Default";

    /// <summary>Available label font choices for the properties-panel picker.</summary>
    public IReadOnlyList<LabelFontChoice> LabelFontChoices { get; } = new[]
    {
        new LabelFontChoice("Default", string.Empty),
        new LabelFontChoice("Serif", "Georgia, 'Times New Roman', serif"),
        new LabelFontChoice("Sans-serif", "'Segoe UI', Arial, sans-serif"),
        new LabelFontChoice("Monospace", "Consolas, 'Courier New', monospace"),
    };

    /// <summary>Host-supplied: set the selected label's font size.</summary>
    public Action<string, double>? PushSetLabelFontSize { get; set; }
    /// <summary>Host-supplied: set the selected label's CSS font-family.</summary>
    public Action<string, string>? PushSetLabelFontFamily { get; set; }
    /// <summary>Host-supplied: set the selected label's text alignment.</summary>
    public Action<string, string>? PushSetLabelAlign { get; set; }
    /// <summary>Host-supplied: set the selected label's text colour (hex).</summary>
    public Action<string, string>? PushSetLabelColor { get; set; }
    /// <summary>Host-supplied: delete a label by id.</summary>
    public Action<string>? PushDeleteLabel { get; set; }

    private bool _suspendLabelPush;

    public void SetSelectedLabel(string? id, double fontSize, string fontFamily, string align, string colorHex)
    {
        _suspendLabelPush = true;
        SelectedLabelId = id;
        SelectedLabelFontSize = fontSize <= 0 ? 18 : fontSize;
        SelectedLabelFontFamily = fontFamily ?? string.Empty;
        SelectedLabelAlign = string.IsNullOrEmpty(align) ? "center" : align;
        if (Avalonia.Media.Color.TryParse(string.IsNullOrEmpty(colorHex) ? "#ffffff" : colorHex, out var lc))
            SelectedLabelColor = lc;
        _suspendLabelPush = false;
        OnPropertyChanged(nameof(HasSelectedLabel));
        OnPropertyChanged(nameof(SelectedLabelFontDisplay));
    }

    partial void OnSelectedLabelIdChanged(string? value)
        => OnPropertyChanged(nameof(HasSelectedLabel));

    partial void OnSelectedLabelFontSizeChanged(double value)
    {
        if (_suspendLabelPush || string.IsNullOrEmpty(SelectedLabelId)) return;
        PushSetLabelFontSize?.Invoke(SelectedLabelId!, value);
    }

    partial void OnSelectedLabelFontFamilyChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedLabelFontDisplay));
        if (_suspendLabelPush || string.IsNullOrEmpty(SelectedLabelId)) return;
        PushSetLabelFontFamily?.Invoke(SelectedLabelId!, value ?? string.Empty);
    }

    /// <summary>Set the selected label's text alignment ("left"|"center"|"right").</summary>
    public void SetSelectedLabelAlign(string align)
    {
        if (string.IsNullOrEmpty(SelectedLabelId)) return;
        SelectedLabelAlign = align;
        PushSetLabelAlign?.Invoke(SelectedLabelId!, align);
    }

    partial void OnSelectedLabelColorChanged(Avalonia.Media.Color value)
    {
        if (_suspendLabelPush || string.IsNullOrEmpty(SelectedLabelId)) return;
        PushSetLabelColor?.Invoke(SelectedLabelId!, $"#{value.R:X2}{value.G:X2}{value.B:X2}");
    }

    [RelayCommand]
    private void DeleteSelectedLabel()
    {
        if (string.IsNullOrEmpty(SelectedLabelId)) return;
        PushDeleteLabel?.Invoke(SelectedLabelId!);
        SetSelectedLabel(null, 18, string.Empty, "center", "#ffffff");
    }

    // ── Shape selection (terrain, driven by map clicks) ─────────────────
    [ObservableProperty] private string? _selectedShapeId;
    [ObservableProperty] private string _selectedShapeType = "grass";
    [ObservableProperty] private Avalonia.Media.Color _selectedShapeColor = Avalonia.Media.Colors.YellowGreen;
    [ObservableProperty] private bool _selectedShapeSmooth = true;
    [ObservableProperty] private double _selectedShapeBlend;

    public bool HasSelectedShape => !string.IsNullOrEmpty(SelectedShapeId);

    /// <summary>Host-supplied: set the selected shape's terrain type.</summary>
    public Action<string, string>? PushSetShapeType { get; set; }
    /// <summary>Host-supplied: set the selected shape's fill colour (hex).</summary>
    public Action<string, string>? PushSetShapeColor { get; set; }
    /// <summary>Host-supplied: set the selected shape's smooth flag.</summary>
    public Action<string, bool>? PushSetShapeSmooth { get; set; }
    /// <summary>Host-supplied: set the selected shape's edge-blend strength.</summary>
    public Action<string, double>? PushSetShapeBlend { get; set; }
    /// <summary>Host-supplied: delete a shape by id.</summary>
    public Action<string>? PushDeleteShape { get; set; }
    /// <summary>Host-supplied: reorder a shape in its layer z-stack (id, +1 = forward, -1 = back).</summary>
    public Action<string, int>? PushMoveShapeZ { get; set; }

    private bool _suspendShapePush;

    public void SetSelectedShape(string? id, string type, string colorHex, bool smooth, double blend)
    {
        _suspendShapePush = true;
        SelectedShapeId = id;
        SelectedShapeType = string.IsNullOrEmpty(type) ? "grass" : type;
        if (Avalonia.Media.Color.TryParse(string.IsNullOrEmpty(colorHex) ? "#8db360" : colorHex, out var c))
            SelectedShapeColor = c;
        SelectedShapeSmooth = smooth;
        SelectedShapeBlend = blend;
        _suspendShapePush = false;
        OnPropertyChanged(nameof(HasSelectedShape));
    }

    partial void OnSelectedShapeIdChanged(string? value) => OnPropertyChanged(nameof(HasSelectedShape));

    partial void OnSelectedShapeColorChanged(Avalonia.Media.Color value)
    {
        if (_suspendShapePush || string.IsNullOrEmpty(SelectedShapeId)) return;
        PushSetShapeColor?.Invoke(SelectedShapeId!, $"#{value.R:X2}{value.G:X2}{value.B:X2}");
    }

    partial void OnSelectedShapeSmoothChanged(bool value)
    {
        if (_suspendShapePush || string.IsNullOrEmpty(SelectedShapeId)) return;
        PushSetShapeSmooth?.Invoke(SelectedShapeId!, value);
    }

    partial void OnSelectedShapeBlendChanged(double value)
    {
        if (_suspendShapePush || string.IsNullOrEmpty(SelectedShapeId)) return;
        PushSetShapeBlend?.Invoke(SelectedShapeId!, value);
    }

    /// <summary>Change the selected shape's terrain type (re-seeds its colour in the WebView).</summary>
    public void SetSelectedShapeType(string type)
    {
        if (string.IsNullOrEmpty(SelectedShapeId)) return;
        SelectedShapeType = type;
        PushSetShapeType?.Invoke(SelectedShapeId!, type);
    }

    [RelayCommand]
    private void DeleteSelectedShape()
    {
        if (string.IsNullOrEmpty(SelectedShapeId)) return;
        PushDeleteShape?.Invoke(SelectedShapeId!);
        SetSelectedShape(null, "grass", "#8db360", true, 0);
    }

    [RelayCommand]
    private void MoveSelectedShapeForward()
    {
        if (!string.IsNullOrEmpty(SelectedShapeId)) PushMoveShapeZ?.Invoke(SelectedShapeId!, 1);
    }

    [RelayCommand]
    private void MoveSelectedShapeBackward()
    {
        if (!string.IsNullOrEmpty(SelectedShapeId)) PushMoveShapeZ?.Invoke(SelectedShapeId!, -1);
    }

    // ── Map border (clip boundary) ──────────────────────────────────────
    [ObservableProperty] private bool _borderEditActive;
    [ObservableProperty] private Avalonia.Media.Color _borderOutlineColor = Avalonia.Media.Color.Parse("#1c1a18");
    [ObservableProperty] private double _borderOutlineWidth = 4;

    /// <summary>Host-supplied: set the border outline colour + width (hex, world units).</summary>
    public Action<string, double>? PushSetBorderOutline { get; set; }
    /// <summary>Host-supplied: remove the map border entirely.</summary>
    public Action? PushClearBorder { get; set; }

    private bool _suspendBorderPush;

    public void SetBorderSelection(bool active, string colorHex, double width)
    {
        _suspendBorderPush = true;
        BorderEditActive = active;
        if (Avalonia.Media.Color.TryParse(string.IsNullOrEmpty(colorHex) ? "#1c1a18" : colorHex, out var c))
            BorderOutlineColor = c;
        BorderOutlineWidth = width > 0 ? width : 4;
        _suspendBorderPush = false;
    }

    partial void OnBorderOutlineColorChanged(Avalonia.Media.Color value)
    {
        if (_suspendBorderPush || !BorderEditActive) return;
        PushSetBorderOutline?.Invoke($"#{value.R:X2}{value.G:X2}{value.B:X2}", BorderOutlineWidth);
    }

    partial void OnBorderOutlineWidthChanged(double value)
    {
        if (_suspendBorderPush || !BorderEditActive) return;
        var c = BorderOutlineColor;
        PushSetBorderOutline?.Invoke($"#{c.R:X2}{c.G:X2}{c.B:X2}", value);
    }

    /// <summary>Activate the map-border tool — draws a new border, or edits the existing one.</summary>
    [RelayCommand]
    private void EditMapBorder()
    {
        IsPinPlaceMode = false;
        IsLabelPlaceMode = false;
        IsSplineMode = false;
        IsTerrainMode = false;
        PushToolModeRequested?.Invoke("border");
    }

    [RelayCommand]
    private void ClearMapBorder()
    {
        PushClearBorder?.Invoke();
        SetBorderSelection(false, "#1c1a18", 4);
    }

    // ── Building selection (driven by map clicks) ───────────────────────
    [ObservableProperty] private string? _selectedBuildingId;
    [ObservableProperty] private string _selectedBuildingType = "singleFamily";
    [ObservableProperty] private string _selectedBuildingRoofKind = "gable";
    [ObservableProperty] private double _selectedBuildingRoofPitch = 0.5;
    [ObservableProperty] private int _selectedBuildingFloorCount = 1;
    [ObservableProperty] private double _selectedBuildingPlanZoom = 4;

    public bool HasSelectedBuilding => !string.IsNullOrEmpty(SelectedBuildingId);

    /// <summary>Host-supplied: set the selected building's type.</summary>
    public Action<string, string>? PushSetBuildingType { get; set; }
    /// <summary>Host-supplied: set the building's roof (id, kind, pitch).</summary>
    public Action<string, string, double>? PushSetBuildingRoof { get; set; }
    /// <summary>Host-supplied: set the building's floor count.</summary>
    public Action<string, int>? PushSetBuildingFloors { get; set; }
    /// <summary>Host-supplied: set the building's floor-plan reveal zoom.</summary>
    public Action<string, double>? PushSetBuildingPlanZoom { get; set; }
    /// <summary>Host-supplied: delete a building by id.</summary>
    public Action<string>? PushDeleteBuilding { get; set; }
    /// <summary>Host-supplied: reorder a building in its layer z-stack (id, +1/-1).</summary>
    public Action<string, int>? PushMoveBuildingZ { get; set; }
    /// <summary>Host-supplied: enter in-place floor-plan editing for a building.</summary>
    public Action<string>? PushEditBuildingPlan { get; set; }

    private bool _suspendBuildingPush;

    public void SetSelectedBuilding(string? id, string type, string roofKind, double roofPitch,
        int floorCount, double planZoom)
    {
        _suspendBuildingPush = true;
        SelectedBuildingId = id;
        SelectedBuildingType = string.IsNullOrEmpty(type) ? "singleFamily" : type;
        SelectedBuildingRoofKind = string.IsNullOrEmpty(roofKind) ? "gable" : roofKind;
        SelectedBuildingRoofPitch = roofPitch;
        SelectedBuildingFloorCount = floorCount;
        SelectedBuildingPlanZoom = planZoom > 0 ? planZoom : 4;
        _suspendBuildingPush = false;
        OnPropertyChanged(nameof(HasSelectedBuilding));
    }

    partial void OnSelectedBuildingIdChanged(string? value) => OnPropertyChanged(nameof(HasSelectedBuilding));

    partial void OnSelectedBuildingRoofPitchChanged(double value)
    {
        if (_suspendBuildingPush || string.IsNullOrEmpty(SelectedBuildingId)) return;
        PushSetBuildingRoof?.Invoke(SelectedBuildingId!, SelectedBuildingRoofKind, value);
    }

    partial void OnSelectedBuildingFloorCountChanged(int value)
    {
        if (_suspendBuildingPush || string.IsNullOrEmpty(SelectedBuildingId)) return;
        PushSetBuildingFloors?.Invoke(SelectedBuildingId!, value);
    }

    partial void OnSelectedBuildingPlanZoomChanged(double value)
    {
        if (_suspendBuildingPush || string.IsNullOrEmpty(SelectedBuildingId)) return;
        PushSetBuildingPlanZoom?.Invoke(SelectedBuildingId!, value);
    }

    /// <summary>Change the selected building's type.</summary>
    public void SetSelectedBuildingType(string type)
    {
        if (string.IsNullOrEmpty(SelectedBuildingId)) return;
        SelectedBuildingType = type;
        PushSetBuildingType?.Invoke(SelectedBuildingId!, type);
    }

    /// <summary>Change the selected building's roof kind (keeps the current pitch).</summary>
    public void SetSelectedBuildingRoofKind(string kind)
    {
        if (string.IsNullOrEmpty(SelectedBuildingId)) return;
        SelectedBuildingRoofKind = kind;
        PushSetBuildingRoof?.Invoke(SelectedBuildingId!, kind, SelectedBuildingRoofPitch);
    }

    [RelayCommand]
    private void DeleteSelectedBuilding()
    {
        if (string.IsNullOrEmpty(SelectedBuildingId)) return;
        PushDeleteBuilding?.Invoke(SelectedBuildingId!);
        SetSelectedBuilding(null, "singleFamily", "gable", 0.5, 1, 4);
    }

    [RelayCommand]
    private void MoveSelectedBuildingForward()
    {
        if (!string.IsNullOrEmpty(SelectedBuildingId)) PushMoveBuildingZ?.Invoke(SelectedBuildingId!, 1);
    }

    [RelayCommand]
    private void MoveSelectedBuildingBackward()
    {
        if (!string.IsNullOrEmpty(SelectedBuildingId)) PushMoveBuildingZ?.Invoke(SelectedBuildingId!, -1);
    }

    [RelayCommand]
    private void EditSelectedBuildingPlan()
    {
        if (!string.IsNullOrEmpty(SelectedBuildingId)) PushEditBuildingPlan?.Invoke(SelectedBuildingId!);
    }

    /// <summary>Called when the WebView reports an image was selected on the map.
    /// Selects the owning layer node in the panel so the Properties section follows.</summary>
    public void SelectImageFromView(string imageId)
    {
        SetSelectedPin(null, null);
        var owner = FindImageOwner(imageId);
        if (owner == null) return;
        var row = FindRow(owner.Id);
        if (row != null)
        {
            ExpandAncestors(row);
            SelectedNode = row;
            ActiveLayerId = row.NodeId; // keeps the panel list highlight in sync
        }
    }

    [ObservableProperty]
    private string? _activeLayerId;

    public bool HasMap => ActiveMap != null;

    public Task<(string Label, string EntityId, string EntityType, string Color)?> PromptPinDetailsAsync()
        => PromptPinDetailsRequested != null
            ? PromptPinDetailsRequested.Invoke()
            : Task.FromResult<(string, string, string, string)?>(("Pin", string.Empty, string.Empty, string.Empty));

    public Task<(string Label, string EntityId, string EntityType, string Color, bool Delete)?> RequestPinEditAsync(
        string pinId, string label, string entityId, string entityType, string color)
        => RequestPinEditDialog != null
            ? RequestPinEditDialog.Invoke(pinId, label, entityId, entityType, color)
            : Task.FromResult<(string, string, string, string, bool)?>(null);

    public Task<string?> PromptMoveImageToLayerAsync(string currentLayerId)
    {
        if (ActiveMap == null || PromptLayerPicker == null) return Task.FromResult<string?>(null);
        var options = new List<(string Id, string Name)>();
        WalkNodes(ActiveMap.Layers, (n, depth) => options.Add((n.Id, new string(' ', depth * 2) + n.Name)));
        return PromptLayerPicker.Invoke(options, currentLayerId);
    }

    public async Task UpdateInitialViewAsync(double centerX, double centerY, double zoom)
    {
        if (ActiveMap == null) return;
        ActiveMap.InitialView = new MapViewport { CenterX = centerX, CenterY = centerY, Zoom = zoom };
        await _mapService.SaveMapAsync(ActiveMap);
    }

    public MapViewModel(IMapService mapService, IProjectService projectService)
    {
        _mapService = mapService;
        _projectService = projectService;
    }

    // ── Map list ────────────────────────────────────────────────────────
    public void RefreshMaps()
    {
        Maps.Clear();
        var book = _projectService.ActiveBook;
        if (book == null) return;
        foreach (var m in book.Maps) Maps.Add(m);
        if (SelectedMap == null && Maps.Count > 0) SelectedMap = Maps[0];
    }

    partial void OnSelectedMapChanged(MapReference? value)
    {
        if (value == null) { ActiveMap = null; return; }
        _ = LoadActiveMapAsync(value.Id);
    }

    partial void OnIsEditModeChanged(bool value)
        => PushModeRequested?.Invoke(value ? "edit" : "view");

    partial void OnSelectedNodeChanged(LayerNodeRow? value)
        => OnPropertyChanged(nameof(HasSelectedNode));

    private async Task LoadActiveMapAsync(string id)
    {
        ActiveMap = await _mapService.LoadMapAsync(id);
        OnPropertyChanged(nameof(HasMap));
        ActiveLayerId = null;
        RebuildTree();
        RebuildCustomProfileChoices();
        if (ActiveMap != null)
        {
            PushMapJson();
            PushModeRequested?.Invoke(IsEditMode ? "edit" : "view");
            if (ActiveLayerId != null) PushActiveLayerRequested?.Invoke(ActiveLayerId);
        }
    }

    [RelayCommand]
    private void OpenMap(MapReference? reference)
    {
        if (reference != null) SelectedMap = reference;
    }

    [RelayCommand]
    private async Task CreateMapAsync()
    {
        if (ShowInputDialog == null) return;
        var name = await ShowInputDialog.Invoke(Loc.T("map.createTitle"), Loc.T("map.createPrompt"), "Map 1");
        if (string.IsNullOrWhiteSpace(name)) return;
        var map = await _mapService.CreateMapAsync(name.Trim());
        RefreshMaps();
        SelectedMap = Maps.FirstOrDefault(m => m.Id == map.Id);
    }

    [RelayCommand]
    private async Task RenameMapAsync(MapReference? reference)
    {
        if (reference == null || ShowInputDialog == null) return;
        var newName = await ShowInputDialog.Invoke(Loc.T("map.renameTitle"), Loc.T("map.renamePrompt"), reference.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == reference.Name) return;
        await _mapService.RenameMapAsync(reference.Id, newName.Trim());
        RefreshMaps();
        SelectedMap = Maps.FirstOrDefault(m => m.Id == reference.Id);
    }

    [RelayCommand]
    private async Task DeleteMapConfirmAsync(MapReference? reference)
    {
        if (reference == null) return;
        if (ShowConfirmDialog != null)
        {
            var ok = await ShowConfirmDialog.Invoke(
                Loc.T("map.deleteTitle"),
                string.Format(Loc.T("map.deleteMessage"), reference.Name));
            if (!ok) return;
        }
        await _mapService.DeleteMapAsync(reference.Id);
        RefreshMaps();
    }

    [RelayCommand]
    private async Task DeleteMapAsync(MapReference? reference)
    {
        if (reference == null) return;
        await _mapService.DeleteMapAsync(reference.Id);
        RefreshMaps();
    }

    [RelayCommand]
    private void ToggleMode() => IsEditMode = !IsEditMode;

    [RelayCommand]
    private void ToggleLayerPanel() => IsLayerPanelOpen = !IsLayerPanelOpen;

    // ── Recursive tree helpers (on the ActiveMap model) ─────────────────
    private static void WalkNodes(IEnumerable<MapLayerNode> nodes, Action<MapLayerNode, int> cb, int depth = 0)
    {
        foreach (var n in nodes)
        {
            cb(n, depth);
            if (n.Children.Count > 0) WalkNodes(n.Children, cb, depth + 1);
        }
    }

    private MapLayerNode? FindNode(string id)
    {
        MapLayerNode? found = null;
        if (ActiveMap != null) WalkNodes(ActiveMap.Layers, (n, _) => { if (n.Id == id) found = n; });
        return found;
    }

    private MapLayerNode? FindImageOwner(string imageId)
    {
        MapLayerNode? found = null;
        if (ActiveMap != null)
            WalkNodes(ActiveMap.Layers, (n, _) => { if (found == null && n.Images.Any(i => i.Id == imageId)) found = n; });
        return found;
    }

    /// <summary>Returns the list that directly contains <paramref name="id"/> plus the
    /// parent node (null if the node is a root).</summary>
    private (List<MapLayerNode> list, MapLayerNode? parent)? FindContainer(string id)
    {
        if (ActiveMap == null) return null;
        (List<MapLayerNode>, MapLayerNode?)? result = null;
        void Recurse(List<MapLayerNode> list, MapLayerNode? parent)
        {
            if (result != null) return;
            if (list.Any(n => n.Id == id)) { result = (list, parent); return; }
            foreach (var n in list) Recurse(n.Children, n);
        }
        Recurse(ActiveMap.Layers, null);
        return result;
    }

    private LayerNodeRow? FindRow(string id)
    {
        LayerNodeRow? found = null;
        void Recurse(IEnumerable<LayerNodeRow> rows)
        {
            foreach (var r in rows)
            {
                if (r.NodeId == id) { found = r; return; }
                Recurse(r.Children);
                if (found != null) return;
            }
        }
        Recurse(_rootRows);
        return found;
    }

    private void ExpandAncestors(LayerNodeRow row)
    {
        var p = row.Parent;
        while (p != null)
        {
            if (!p.IsExpanded)
            {
                p.IsExpanded = true;
                var node = FindNode(p.NodeId);
                if (node != null) node.Expanded = true;
            }
            p = p.Parent;
        }
        FlattenVisible();
    }

    private static string NewId(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);

    // ── Tree → row construction ─────────────────────────────────────────
    private void RebuildTree()
    {
        _rootRows.Clear();
        if (ActiveMap != null)
            foreach (var node in ActiveMap.Layers)
                _rootRows.Add(BuildRow(node, null, 0));

        // Default the active layer to the first leaf if unset / stale.
        if (ActiveLayerId == null || FindNode(ActiveLayerId) == null)
        {
            var firstLeaf = FirstLeaf();
            ActiveLayerId = firstLeaf?.Id;
        }
        MarkActive();
        FlattenVisible();
    }

    private LayerNodeRow BuildRow(MapLayerNode node, LayerNodeRow? parent, int depth)
    {
        var row = new LayerNodeRow(node, parent, depth);
        foreach (var child in node.Children)
            row.Children.Add(BuildRow(child, row, depth + 1));
        foreach (var img in node.Images)
            row.Images.Add(new ImageRow(img));
        PopulateElementRows(row, node);
        return row;
    }

    // Splines + shapes live on the node; pins / labels are top-level but carry a LayerId.
    private void PopulateElementRows(LayerNodeRow row, MapLayerNode node)
    {
        row.Splines.Clear();
        row.Pins.Clear();
        row.Labels.Clear();
        row.Shapes.Clear();
        row.Buildings.Clear();
        foreach (var sp in node.Splines)
            row.Splines.Add(new LayerElementRow("spline", sp.Id, SplineDisplayName(sp), sp.MinZoom, sp.MaxZoom));
        foreach (var sh in node.Shapes)
            row.Shapes.Add(new LayerElementRow("shape", sh.Id, ShapeDisplayName(sh), sh.MinZoom, sh.MaxZoom));
        foreach (var bd in node.Buildings)
            row.Buildings.Add(new LayerElementRow("building", bd.Id,
                string.IsNullOrEmpty(bd.Type) ? "building" : bd.Type, bd.MinZoom, bd.MaxZoom));
        if (ActiveMap == null) return;
        foreach (var pin in ActiveMap.Pins.Where(p => p.LayerId == node.Id))
            row.Pins.Add(new LayerElementRow("pin", pin.Id,
                string.IsNullOrWhiteSpace(pin.Label) ? "(pin)" : pin.Label, pin.MinZoom, pin.MaxZoom));
        foreach (var lbl in ActiveMap.Labels.Where(l => l.LayerId == node.Id))
            row.Labels.Add(new LayerElementRow("label", lbl.Id,
                string.IsNullOrWhiteSpace(lbl.Text) ? "(label)" : lbl.Text.Replace("\n", " "),
                lbl.MinZoom, lbl.MaxZoom));
    }

    private static string SplineDisplayName(MapSpline sp)
    {
        var preset = sp.Preset;
        if (preset.StartsWith("custom:", StringComparison.Ordinal)) preset = "custom";
        return string.IsNullOrEmpty(preset) ? sp.Kind : $"{sp.Kind}: {preset}";
    }

    private static string ShapeDisplayName(MapShape sh)
        => string.IsNullOrEmpty(sh.Type) ? "shape" : sh.Type;

    private void FlattenVisible()
    {
        VisibleRows.Clear();
        void Recurse(IEnumerable<LayerNodeRow> rows)
        {
            foreach (var r in rows)
            {
                VisibleRows.Add(r);
                if (r.HasChildren && r.IsExpanded) Recurse(r.Children);
            }
        }
        Recurse(_rootRows);
    }

    private void MarkActive()
    {
        void Recurse(IEnumerable<LayerNodeRow> rows)
        {
            foreach (var r in rows)
            {
                r.IsActive = r.NodeId == ActiveLayerId;
                Recurse(r.Children);
            }
        }
        Recurse(_rootRows);
    }

    private MapLayerNode? FirstLeaf()
    {
        MapLayerNode? leaf = null;
        if (ActiveMap != null)
            WalkNodes(ActiveMap.Layers, (n, _) => { if (leaf == null && n.Children.Count == 0) leaf = n; });
        return leaf;
    }

    partial void OnActiveLayerIdChanged(string? value)
    {
        MarkActive();
        if (value != null) PushActiveLayerRequested?.Invoke(value);
    }

    // ── Panel commands ──────────────────────────────────────────────────
    [RelayCommand]
    private void SelectNode(LayerNodeRow? row)
    {
        if (row == null) return;
        SelectedNode = row;
        ActiveLayerId = row.NodeId;
    }

    [RelayCommand]
    private void ToggleExpand(LayerNodeRow? row)
    {
        if (row == null) return;
        row.IsExpanded = !row.IsExpanded;
        var node = FindNode(row.NodeId);
        if (node != null) node.Expanded = row.IsExpanded;
        FlattenVisible();
        _ = _mapService.SaveMapAsync(ActiveMap!);
    }

    [RelayCommand]
    private async Task ToggleNodeHiddenAsync(LayerNodeRow? row)
    {
        if (row == null) return;
        await PersistFromViewAsync();
        var node = FindNode(row.NodeId);
        if (node == null) return;
        node.Hidden = !node.Hidden;
        row.Hidden = node.Hidden;
        await SaveAndPushNoRebuildAsync();
    }

    [RelayCommand]
    private async Task ToggleNodeLockedAsync(LayerNodeRow? row)
    {
        if (row == null) return;
        await PersistFromViewAsync();
        var node = FindNode(row.NodeId);
        if (node == null) return;
        node.Locked = !node.Locked;
        row.Locked = node.Locked;
        await SaveAndPushNoRebuildAsync();
    }

    /// <summary>Inline rename committed from the panel (double-click → edit).</summary>
    public async Task CommitNodeRenameAsync(LayerNodeRow row, string newName)
    {
        newName = (newName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(newName)) return;
        await PersistFromViewAsync();
        var node = FindNode(row.NodeId);
        if (node == null) return;
        // Compare against the model (the source of truth), not row.Name —
        // PersistFromViewAsync may have just re-synced the row from the WebView.
        if (node.Name == newName) { row.Name = newName; return; }
        node.Name = newName;
        row.Name = newName;
        await SaveAndPushNoRebuildAsync();
    }

    [RelayCommand]
    private async Task AddLayerAsync()
    {
        if (ActiveMap == null) return;
        await PersistFromViewAsync();
        var node = new MapLayerNode { Id = NewId("layer"), Name = $"Layer {ActiveMap.Layers.Count + 1}" };
        ActiveMap.Layers.Add(node);
        ActiveLayerId = node.Id;
        await SaveRebuildPushAsync();
        SelectedNode = FindRow(node.Id);
    }

    [RelayCommand]
    private async Task AddChildLayerAsync(LayerNodeRow? row)
    {
        if (ActiveMap == null || row == null) return;
        await PersistFromViewAsync();
        var parent = FindNode(row.NodeId);
        if (parent == null) return;
        var node = new MapLayerNode { Id = NewId("layer"), Name = $"Layer {parent.Children.Count + 1}" };
        parent.Children.Add(node);
        parent.Expanded = true;
        ActiveLayerId = node.Id;
        await SaveRebuildPushAsync();
        SelectedNode = FindRow(node.Id);
    }

    [RelayCommand]
    private async Task DeleteNodeAsync(LayerNodeRow? row)
    {
        if (ActiveMap == null || row == null) return;
        if (ShowConfirmDialog != null)
        {
            var ok = await ShowConfirmDialog.Invoke(
                Loc.T("map.layerDeleteTitle"),
                string.Format(Loc.T("map.layerDeleteMessage"), row.Name));
            if (!ok) return;
        }
        await PersistFromViewAsync();
        var container = FindContainer(row.NodeId);
        if (container == null) return;
        container.Value.list.RemoveAll(n => n.Id == row.NodeId);
        if (SelectedNode?.NodeId == row.NodeId) SelectedNode = null;
        await SaveRebuildPushAsync();
    }

    /// <summary>Drag-drop: move <paramref name="dragId"/> relative to <paramref name="targetId"/>.</summary>
    public async Task MoveNodeAsync(string dragId, string targetId, NodeDropPosition position)
    {
        if (ActiveMap == null || dragId == targetId) return;
        // Reject dropping a node into its own subtree.
        var dragNode = FindNode(dragId);
        if (dragNode == null) return;
        if (IsDescendant(dragNode, targetId)) return;

        await PersistFromViewAsync();
        dragNode = FindNode(dragId);
        if (dragNode == null) return;

        var srcContainer = FindContainer(dragId);
        if (srcContainer == null) return;
        srcContainer.Value.list.Remove(dragNode);

        if (position == NodeDropPosition.Inside)
        {
            var targetNode = FindNode(targetId);
            if (targetNode == null) { srcContainer.Value.list.Add(dragNode); return; }
            targetNode.Children.Add(dragNode);
            targetNode.Expanded = true;
        }
        else
        {
            var dstContainer = FindContainer(targetId);
            if (dstContainer == null) { srcContainer.Value.list.Add(dragNode); return; }
            var idx = dstContainer.Value.list.FindIndex(n => n.Id == targetId);
            if (idx < 0) { srcContainer.Value.list.Add(dragNode); return; }
            dstContainer.Value.list.Insert(position == NodeDropPosition.After ? idx + 1 : idx, dragNode);
        }
        await SaveRebuildPushAsync();
    }

    /// <summary>Drag-drop onto empty panel space: move the node to the end of the root list.</summary>
    public async Task MoveNodeToRootAsync(string dragId)
    {
        if (ActiveMap == null) return;
        var srcContainer = FindContainer(dragId);
        if (srcContainer == null) return;
        // Already a root node at the end — nothing to do.
        if (srcContainer.Value.parent == null
            && ActiveMap.Layers.Count > 0
            && ActiveMap.Layers[^1].Id == dragId) return;
        await PersistFromViewAsync();
        var node = FindNode(dragId);
        if (node == null) return;
        var container = FindContainer(dragId);
        if (container == null) return;
        container.Value.list.Remove(node);
        ActiveMap.Layers.Add(node);
        await SaveRebuildPushAsync();
    }

    private bool IsDescendant(MapLayerNode node, string candidateId)
    {
        foreach (var c in node.Children)
        {
            if (c.Id == candidateId) return true;
            if (IsDescendant(c, candidateId)) return true;
        }
        return false;
    }

    // ── Properties section (selected node) ──────────────────────────────
    public async Task SetNodeOpacityAsync(LayerNodeRow row, double opacity)
    {
        var node = FindNode(row.NodeId);
        if (node == null) return;
        var clamped = Math.Round(Math.Max(0, Math.Min(1, opacity)), 2);
        if (Math.Abs(node.Opacity - clamped) < 0.005) return;
        node.Opacity = clamped;
        row.Opacity = clamped;
        await SaveAndPushNoRebuildAsync();
    }

    public async Task SetNodeZoomRangeAsync(LayerNodeRow row, double? min, double? max)
    {
        var node = FindNode(row.NodeId);
        if (node == null) return;
        node.MinZoom = (min.HasValue && min.Value > 0) ? min : null;
        node.MaxZoom = (max.HasValue && max.Value > 0) ? max : null;
        row.MinZoom = node.MinZoom;
        row.MaxZoom = node.MaxZoom;
        await SaveAndPushNoRebuildAsync();
    }

    public async Task SetNodeConnectedSetAsync(LayerNodeRow row, bool value)
    {
        var node = FindNode(row.NodeId);
        if (node == null || node.IsConnectedSet == value) return;
        node.IsConnectedSet = value;
        if (value && string.IsNullOrEmpty(node.DefaultMemberLayerId) && node.Children.Count > 0)
            node.DefaultMemberLayerId = node.Children[0].Id;
        row.IsConnectedSet = value;
        row.ActiveMemberLayerId = node.DefaultMemberLayerId;
        await SaveAndPushNoRebuildAsync();
    }

    public async Task SetNodeActiveMemberAsync(LayerNodeRow row, string? memberId)
    {
        var node = FindNode(row.NodeId);
        if (node == null) return;
        var v = string.IsNullOrEmpty(memberId) ? null : memberId;
        if (node.DefaultMemberLayerId == v) return;
        node.DefaultMemberLayerId = v;
        row.ActiveMemberLayerId = v;
        await SaveAndPushNoRebuildAsync();
    }

    public async Task SetImageZoomRangeAsync(ImageRow imgRow, double? min, double? max)
    {
        if (ActiveMap == null) return;
        var owner = FindImageOwner(imgRow.ImageId);
        var img = owner?.Images.FirstOrDefault(i => i.Id == imgRow.ImageId);
        if (img == null) return;
        img.MinZoom = (min.HasValue && min.Value > 0) ? min : null;
        img.MaxZoom = (max.HasValue && max.Value > 0) ? max : null;
        imgRow.MinZoom = img.MinZoom;
        imgRow.MaxZoom = img.MaxZoom;
        PushUpdateImageZoomRange?.Invoke(imgRow.ImageId, min ?? 0, max ?? 0);
        await _mapService.SaveMapAsync(ActiveMap);
    }

    /// <summary>Isolate toggle — view-only, not persisted. Only one element
    /// (image, spline, pin or label) is isolated at a time.</summary>
    public void ToggleIsolateImage(ImageRow imgRow)
    {
        var nowIsolated = !imgRow.IsIsolated;
        foreach (var r in _rootRows) ClearAllIsolate(r);
        imgRow.IsIsolated = nowIsolated;
        PushIsolateImage?.Invoke(nowIsolated ? imgRow.ImageId : string.Empty);
    }

    /// <summary>Isolate toggle for a spline / pin / label row.</summary>
    public void ToggleIsolateElement(LayerElementRow row)
    {
        var nowIsolated = !row.IsIsolated;
        foreach (var r in _rootRows) ClearAllIsolate(r);
        row.IsIsolated = nowIsolated;
        PushIsolateElement?.Invoke(nowIsolated ? row.Kind : string.Empty,
                                   nowIsolated ? row.Id : string.Empty);
    }

    private static void ClearAllIsolate(LayerNodeRow row)
    {
        foreach (var im in row.Images) im.IsIsolated = false;
        foreach (var e in row.Splines) e.IsIsolated = false;
        foreach (var e in row.Pins) e.IsIsolated = false;
        foreach (var e in row.Labels) e.IsIsolated = false;
        foreach (var c in row.Children) ClearAllIsolate(c);
    }

    /// <summary>Set the visible-zoom range of a spline / pin / label.</summary>
    public async Task SetElementZoomRangeAsync(LayerElementRow row, double? min, double? max)
    {
        if (ActiveMap == null) return;
        var mn = (min.HasValue && min.Value > 0) ? min : null;
        var mx = (max.HasValue && max.Value > 0) ? max : null;
        if (!ApplyElementZoom(row.Kind, row.Id, mn, mx)) return;
        row.MinZoom = mn;
        row.MaxZoom = mx;
        PushSetElementZoomRange?.Invoke(row.Kind, row.Id, min ?? 0, max ?? 0);
        await _mapService.SaveMapAsync(ActiveMap);
    }

    private bool ApplyElementZoom(string kind, string id, double? min, double? max)
    {
        if (ActiveMap == null) return false;
        if (kind == "pin")
        {
            var pin = ActiveMap.Pins.FirstOrDefault(p => p.Id == id);
            if (pin == null) return false;
            pin.MinZoom = min; pin.MaxZoom = max; return true;
        }
        if (kind == "label")
        {
            var lbl = ActiveMap.Labels.FirstOrDefault(l => l.Id == id);
            if (lbl == null) return false;
            lbl.MinZoom = min; lbl.MaxZoom = max; return true;
        }
        if (kind == "spline")
        {
            MapSpline? sp = null;
            WalkNodes(ActiveMap.Layers, (n, _) =>
            {
                sp ??= n.Splines.FirstOrDefault(s => s.Id == id);
            });
            if (sp == null) return false;
            sp.MinZoom = min; sp.MaxZoom = max; return true;
        }
        if (kind == "shape")
        {
            MapShape? sh = null;
            WalkNodes(ActiveMap.Layers, (n, _) =>
            {
                sh ??= n.Shapes.FirstOrDefault(s => s.Id == id);
            });
            if (sh == null) return false;
            sh.MinZoom = min; sh.MaxZoom = max; return true;
        }
        if (kind == "building")
        {
            MapBuilding? bd = null;
            WalkNodes(ActiveMap.Layers, (n, _) =>
            {
                bd ??= n.Buildings.FirstOrDefault(b => b.Id == id);
            });
            if (bd == null) return false;
            bd.MinZoom = min; bd.MaxZoom = max; return true;
        }
        return false;
    }

    // ── Image add / pin / etc ───────────────────────────────────────────
    [RelayCommand]
    private async Task AddImageAsync()
    {
        if (PickImageRequested == null) return;
        var picked = await PickImageRequested.Invoke();
        if (picked == null) return;
        AddImageRequested?.Invoke(picked.Value.RelativePath, picked.Value.Width, picked.Value.Height);
    }

    // ── Persistence ─────────────────────────────────────────────────────
    private void PushMapJson()
    {
        if (ActiveMap == null) return;
        var json = System.Text.Json.JsonSerializer.Serialize(ActiveMap);
        PushMapJsonRequested?.Invoke(json);
    }

    private async Task SaveRebuildPushAsync()
    {
        if (ActiveMap == null) return;
        await _mapService.SaveMapAsync(ActiveMap);
        RebuildTree();
        PushMapJson();
    }

    /// <summary>Save + push JSON without rebuilding rows — keeps in-flight panel inputs alive.</summary>
    private async Task SaveAndPushNoRebuildAsync()
    {
        if (ActiveMap == null) return;
        await _mapService.SaveMapAsync(ActiveMap);
        PushMapJson();
    }

    /// <summary>Pulls JSON back from the WebView and saves. Syncs existing rows in-place
    /// (no rebuild) so in-flight panel inputs survive.</summary>
    public async Task PersistFromViewAsync()
    {
        if (ActiveMap == null || RequestMapJsonFromViewAsync == null) return;
        var json = await RequestMapJsonFromViewAsync.Invoke();
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var updated = System.Text.Json.JsonSerializer.Deserialize<MapData>(json);
            if (updated == null) return;
            updated.Id = ActiveMap.Id;
            updated.FileName = ActiveMap.FileName;
            ActiveMap = updated;
            SyncRowsFromActiveMap();
            await _mapService.SaveMapAsync(updated);
        }
        catch { }
    }

    /// <summary>Refreshes existing rows from ActiveMap. If the node set changed
    /// (count mismatch — e.g. an image moved layers, a node deleted JS-side),
    /// falls back to a full rebuild.</summary>
    private void SyncRowsFromActiveMap()
    {
        if (ActiveMap == null) return;
        var modelIds = new HashSet<string>();
        WalkNodes(ActiveMap.Layers, (n, _) => modelIds.Add(n.Id));
        var rowIds = new HashSet<string>();
        void Collect(IEnumerable<LayerNodeRow> rows)
        {
            foreach (var r in rows) { rowIds.Add(r.NodeId); Collect(r.Children); }
        }
        Collect(_rootRows);
        if (!modelIds.SetEquals(rowIds))
        {
            RebuildTree();
            return;
        }
        // Same node set — sync field values + image lists in place.
        void Sync(IEnumerable<LayerNodeRow> rows)
        {
            foreach (var r in rows)
            {
                var n = FindNode(r.NodeId);
                if (n != null)
                {
                    if (r.Name != n.Name) r.Name = n.Name;
                    if (r.Hidden != n.Hidden) r.Hidden = n.Hidden;
                    if (r.Locked != n.Locked) r.Locked = n.Locked;
                    if (Math.Abs(r.Opacity - n.Opacity) > 0.001) r.Opacity = n.Opacity;
                    if (r.IsConnectedSet != n.IsConnectedSet) r.IsConnectedSet = n.IsConnectedSet;
                    if (r.ActiveMemberLayerId != n.DefaultMemberLayerId) r.ActiveMemberLayerId = n.DefaultMemberLayerId;
                    if (!Nullable.Equals(r.MinZoom, n.MinZoom)) r.MinZoom = n.MinZoom;
                    if (!Nullable.Equals(r.MaxZoom, n.MaxZoom)) r.MaxZoom = n.MaxZoom;
                    SyncImages(r, n);
                    SyncElements(r, n);
                }
                Sync(r.Children);
            }
        }
        Sync(_rootRows);
    }

    private static void SyncImages(LayerNodeRow row, MapLayerNode node)
    {
        var modelIds = node.Images.Select(i => i.Id).ToList();
        var rowIds = row.Images.Select(i => i.ImageId).ToList();
        if (modelIds.SequenceEqual(rowIds))
        {
            foreach (var imgRow in row.Images)
            {
                var img = node.Images.First(i => i.Id == imgRow.ImageId);
                if (!Nullable.Equals(imgRow.MinZoom, img.MinZoom)) imgRow.MinZoom = img.MinZoom;
                if (!Nullable.Equals(imgRow.MaxZoom, img.MaxZoom)) imgRow.MaxZoom = img.MaxZoom;
            }
            return;
        }
        row.Images.Clear();
        foreach (var img in node.Images) row.Images.Add(new ImageRow(img));
    }

    private void SyncElements(LayerNodeRow row, MapLayerNode node)
    {
        SyncElementList(row.Splines, "spline",
            node.Splines.Select(s => (s.Id, SplineDisplayName(s), s.MinZoom, s.MaxZoom)).ToList());
        SyncElementList(row.Shapes, "shape",
            node.Shapes.Select(s => (s.Id, ShapeDisplayName(s), s.MinZoom, s.MaxZoom)).ToList());
        SyncElementList(row.Buildings, "building",
            node.Buildings.Select(b => (b.Id,
                string.IsNullOrEmpty(b.Type) ? "building" : b.Type, b.MinZoom, b.MaxZoom)).ToList());
        if (ActiveMap == null) return;
        SyncElementList(row.Pins, "pin",
            ActiveMap.Pins.Where(p => p.LayerId == node.Id)
                .Select(p => (p.Id, string.IsNullOrWhiteSpace(p.Label) ? "(pin)" : p.Label, p.MinZoom, p.MaxZoom))
                .ToList());
        SyncElementList(row.Labels, "label",
            ActiveMap.Labels.Where(l => l.LayerId == node.Id)
                .Select(l => (l.Id, string.IsNullOrWhiteSpace(l.Text) ? "(label)" : l.Text.Replace("\n", " "),
                    l.MinZoom, l.MaxZoom))
                .ToList());
    }

    private static void SyncElementList(ObservableCollection<LayerElementRow> rows, string kind,
        List<(string Id, string Name, double? Min, double? Max)> desired)
    {
        if (rows.Select(r => r.Id).SequenceEqual(desired.Select(d => d.Id)))
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (!Nullable.Equals(rows[i].MinZoom, desired[i].Min)) rows[i].MinZoom = desired[i].Min;
                if (!Nullable.Equals(rows[i].MaxZoom, desired[i].Max)) rows[i].MaxZoom = desired[i].Max;
            }
            return;
        }
        rows.Clear();
        foreach (var d in desired) rows.Add(new LayerElementRow(kind, d.Id, d.Name, d.Min, d.Max));
    }
}

/// <summary>Recursive panel row for a layer node. Depth drives indentation;
/// Children is the real subtree; the panel binds to the VM's flattened VisibleRows.</summary>
public partial class LayerNodeRow : ObservableObject
{
    public string NodeId { get; }
    public LayerNodeRow? Parent { get; }
    public int Depth { get; }
    public ObservableCollection<LayerNodeRow> Children { get; } = new();
    public ObservableCollection<ImageRow> Images { get; } = new();
    public ObservableCollection<LayerElementRow> Splines { get; } = new();
    public ObservableCollection<LayerElementRow> Pins { get; } = new();
    public ObservableCollection<LayerElementRow> Labels { get; } = new();
    public ObservableCollection<LayerElementRow> Shapes { get; } = new();
    public ObservableCollection<LayerElementRow> Buildings { get; } = new();

    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _hidden;
    [ObservableProperty] private bool _locked;
    [ObservableProperty] private double _opacity;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isConnectedSet;
    [ObservableProperty] private string? _activeMemberLayerId;
    [ObservableProperty] private double? _minZoom;
    [ObservableProperty] private double? _maxZoom;
    [ObservableProperty] private bool _isRenaming;

    public bool HasChildren => Children.Count > 0;
    public Avalonia.Thickness IndentThickness => new(Depth * 16, 0, 0, 0);

    public LayerNodeRow(MapLayerNode node, LayerNodeRow? parent, int depth)
    {
        NodeId = node.Id;
        Parent = parent;
        Depth = depth;
        _name = node.Name;
        _hidden = node.Hidden;
        _locked = node.Locked;
        _opacity = node.Opacity;
        _isExpanded = node.Expanded;
        _isConnectedSet = node.IsConnectedSet;
        _activeMemberLayerId = node.DefaultMemberLayerId;
        _minZoom = node.MinZoom;
        _maxZoom = node.MaxZoom;
    }
}

/// <summary>Properties-section row for an image on the selected layer node.</summary>
public partial class ImageRow : ObservableObject
{
    public string ImageId { get; }
    public string Path { get; }
    public string DisplayName { get; }

    [ObservableProperty] private double? _minZoom;
    [ObservableProperty] private double? _maxZoom;
    [ObservableProperty] private bool _isIsolated;

    public ImageRow(MapImage img)
    {
        ImageId = img.Id;
        Path = img.Path;
        DisplayName = System.IO.Path.GetFileName(img.Path);
        _minZoom = img.MinZoom;
        _maxZoom = img.MaxZoom;
    }
}

/// <summary>Properties-section row for a spline / pin / label on the selected
/// layer node — same controls as <see cref="ImageRow"/> (zoom range + isolate).</summary>
public partial class LayerElementRow : ObservableObject
{
    /// <summary>"spline" | "pin" | "label".</summary>
    public string Kind { get; }
    public string Id { get; }
    public string DisplayName { get; }

    [ObservableProperty] private double? _minZoom;
    [ObservableProperty] private double? _maxZoom;
    [ObservableProperty] private bool _isIsolated;

    public LayerElementRow(string kind, string id, string displayName, double? minZoom, double? maxZoom)
    {
        Kind = kind;
        Id = id;
        DisplayName = displayName;
        _minZoom = minZoom;
        _maxZoom = maxZoom;
    }
}

/// <summary>A custom-profile entry for the spline preset pickers.</summary>
public sealed class MapProfileChoice
{
    public string Kind { get; }
    /// <summary>The preset key to assign — "custom:&lt;id&gt;".</summary>
    public string PresetKey { get; }
    public string Name { get; }

    public MapProfileChoice(string kind, string presetKey, string name)
    {
        Kind = string.IsNullOrEmpty(kind) ? "road" : kind;
        PresetKey = presetKey;
        Name = name;
    }
}

/// <summary>A font option for the map-label properties picker. <see cref="Css"/>
/// is the font-family stack pushed to the WebView ("" = the map's default).</summary>
public sealed record LabelFontChoice(string Name, string Css);

