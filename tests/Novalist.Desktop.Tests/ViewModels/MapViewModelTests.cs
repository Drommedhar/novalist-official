using Avalonia.Media;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Localization;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

[Collection("Avalonia")]
public class MapViewModelTests
{
    static MapViewModelTests()
    {
        var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Locales");
        Loc.Instance.Initialize(dir, "en");
    }

    private sealed class H
    {
        public IMapService Map = null!;
        public IProjectService Proj = null!;
        public BookData Book = new();
        public MapViewModel Vm = null!;
        public Dictionary<string, MapData> Maps = new();
    }

    private static H Build(bool withBook = true)
    {
        var h = new H();
        h.Map = Substitute.For<IMapService>();
        h.Map.SaveMapAsync(Arg.Any<MapData>()).Returns(Task.CompletedTask);
        h.Map.DeleteMapAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        h.Map.RenameMapAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.CompletedTask);
        h.Map.LoadMapAsync(Arg.Any<string>()).Returns(ci => Task.FromResult(h.Maps.GetValueOrDefault((string)ci[0])));

        h.Proj = Substitute.For<IProjectService>();
        h.Proj.ActiveBook.Returns(withBook ? h.Book : null);

        h.Vm = new MapViewModel(h.Map, h.Proj);
        return h;
    }

    private static MapData SampleMap(string id = "m1")
    {
        var leaf = new MapLayerNode
        {
            Id = "L1", Name = "Layer 1", Expanded = true,
            Splines = { new MapSpline { Id = "sp1", Kind = "road", Preset = "residential" } },
            Shapes = { new MapShape { Id = "sh1", Type = "grass" } },
            Buildings = { new MapBuilding { Id = "bd1", Type = "singleFamily" } },
            Images = { new MapImage { Id = "img1", Path = "maps/pic.png" } },
        };
        var group = new MapLayerNode { Id = "G1", Name = "Group", Expanded = true, Children = { leaf } };
        return new MapData
        {
            Id = id, Name = "Map One", FileName = id + ".json",
            Layers = { group },
            Pins = { new MapPin { Id = "pin1", LayerId = "L1", Label = "Town" } },
            Labels = { new MapLabel { Id = "lbl1", LayerId = "L1", Text = "River" } },
            CustomProfiles = { new MapProfile { Id = "cp1", Kind = "road", Name = "Custom Road" } },
        };
    }

    private static H Loaded()
    {
        var h = Build();
        var map = SampleMap();
        h.Maps["m1"] = map;
        h.Book.Maps.Add(new MapReference { Id = "m1", Name = "Map One", FileName = "m1.json" });
        h.Vm.RefreshMaps();
        h.Vm.SelectedMap = h.Vm.Maps[0]; // triggers synchronous LoadActiveMapAsync
        return h;
    }

    // ── Map list ────────────────────────────────────────────────────
    [AvaloniaFact]
    public void RefreshMaps_NoBook_Empty()
    {
        var h = Build(withBook: false);
        h.Vm.RefreshMaps();
        Assert.Empty(h.Vm.Maps);
    }

    [AvaloniaFact]
    public void RefreshMaps_PopulatesAndSelectsFirst()
    {
        var h = Build();
        h.Book.Maps.Add(new MapReference { Id = "m1", Name = "M" });
        h.Vm.RefreshMaps();
        Assert.Single(h.Vm.Maps);
        Assert.NotNull(h.Vm.SelectedMap);
    }

    [AvaloniaFact]
    public void LoadActiveMap_BuildsTree()
    {
        var h = Loaded();
        Assert.NotNull(h.Vm.ActiveMap);
        Assert.True(h.Vm.HasMap);
        Assert.NotEmpty(h.Vm.VisibleRows);
        Assert.NotEmpty(h.Vm.CustomProfileChoices);
    }

    [AvaloniaFact]
    public void SelectedMap_Null_ClearsActive()
    {
        var h = Loaded();
        h.Vm.SelectedMap = null;
        Assert.Null(h.Vm.ActiveMap);
    }

    [AvaloniaFact]
    public void OpenMap_SetsSelected()
    {
        var h = Loaded();
        var other = new MapReference { Id = "m2", Name = "Two" };
        h.Maps["m2"] = SampleMap("m2");
        h.Vm.OpenMapCommand.Execute(other);
        Assert.Equal("m2", h.Vm.SelectedMap!.Id);
    }

    [AvaloniaFact]
    public async Task CreateMap_PromptsAndSelects()
    {
        var h = Build();
        h.Maps["new"] = SampleMap("new");
        h.Map.CreateMapAsync(Arg.Any<string>()).Returns(ci =>
        {
            var m = new MapData { Id = "new", Name = (string)ci[0] };
            h.Book.Maps.Add(new MapReference { Id = "new", Name = m.Name });
            return Task.FromResult(m);
        });
        h.Vm.ShowInputDialog = (_, _, def) => Task.FromResult<string?>("My Map");
        await h.Vm.CreateMapCommand.ExecuteAsync(null);
        await h.Map.Received().CreateMapAsync("My Map");
        Assert.Equal("new", h.Vm.SelectedMap!.Id);
    }

    [AvaloniaFact]
    public async Task CreateMap_CancelledOrNoDialog_NoOp()
    {
        var h = Build();
        await h.Vm.CreateMapCommand.ExecuteAsync(null); // no dialog
        h.Vm.ShowInputDialog = (_, _, _) => Task.FromResult<string?>("  ");
        await h.Vm.CreateMapCommand.ExecuteAsync(null); // blank
        await h.Map.DidNotReceive().CreateMapAsync(Arg.Any<string>());
    }

    [AvaloniaFact]
    public async Task RenameMap_Renames()
    {
        var h = Loaded();
        h.Vm.ShowInputDialog = (_, _, _) => Task.FromResult<string?>("Renamed");
        await h.Vm.RenameMapCommand.ExecuteAsync(h.Vm.Maps[0]);
        await h.Map.Received().RenameMapAsync("m1", "Renamed");
    }

    [AvaloniaFact]
    public async Task RenameMap_Guards()
    {
        var h = Loaded();
        await h.Vm.RenameMapCommand.ExecuteAsync(null); // null ref
        h.Vm.ShowInputDialog = (_, _, _) => Task.FromResult<string?>("Map One"); // unchanged
        await h.Vm.RenameMapCommand.ExecuteAsync(h.Vm.Maps[0]);
        await h.Map.DidNotReceive().RenameMapAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [AvaloniaFact]
    public async Task DeleteMapConfirm_YesNo()
    {
        var h = Loaded();
        h.Vm.ShowConfirmDialog = (_, _) => Task.FromResult(false);
        await h.Vm.DeleteMapConfirmCommand.ExecuteAsync(h.Vm.Maps[0]);
        await h.Map.DidNotReceive().DeleteMapAsync(Arg.Any<string>());

        h.Vm.ShowConfirmDialog = (_, _) => Task.FromResult(true);
        await h.Vm.DeleteMapConfirmCommand.ExecuteAsync(h.Vm.Maps[0]);
        await h.Map.Received().DeleteMapAsync("m1");

        await h.Vm.DeleteMapConfirmCommand.ExecuteAsync(null); // null guard
    }

    [AvaloniaFact]
    public async Task DeleteMap_Direct()
    {
        var h = Loaded();
        await h.Vm.DeleteMapCommand.ExecuteAsync(h.Vm.Maps[0]);
        await h.Map.Received().DeleteMapAsync("m1");
        await h.Vm.DeleteMapCommand.ExecuteAsync(null);
    }

    [AvaloniaFact]
    public void ToggleMode_AndLayerPanel()
    {
        var h = Loaded();
        var pushed = new List<string>();
        h.Vm.PushModeRequested = m => pushed.Add(m);
        var before = h.Vm.IsEditMode;
        h.Vm.ToggleModeCommand.Execute(null);
        Assert.NotEqual(before, h.Vm.IsEditMode);
        Assert.Contains(h.Vm.IsEditMode ? "edit" : "view", pushed);

        var lp = h.Vm.IsLayerPanelOpen;
        h.Vm.ToggleLayerPanelCommand.Execute(null);
        Assert.NotEqual(lp, h.Vm.IsLayerPanelOpen);
    }

    // ── Tool modes ──────────────────────────────────────────────────
    [AvaloniaFact]
    public void ToolModes_MutuallyExclusive_AndPush()
    {
        var h = Loaded();
        var tool = new List<string>();
        h.Vm.PushToolModeRequested = t => tool.Add(t);
        h.Vm.PushSplineDraftType = (_, _) => { };
        h.Vm.PushTerrainDraftType = _ => { };
        h.Vm.PushBuildingDraftType = _ => { };

        h.Vm.IsPinPlaceMode = true;
        Assert.Contains("add-pin", tool);
        h.Vm.IsLabelPlaceMode = true;
        Assert.False(h.Vm.IsPinPlaceMode); // pin turned off
        h.Vm.IsSplineMode = true;
        Assert.False(h.Vm.IsLabelPlaceMode);
        h.Vm.IsTerrainMode = true;
        Assert.False(h.Vm.IsSplineMode);
        h.Vm.IsBuildingMode = true;
        Assert.False(h.Vm.IsTerrainMode);
        h.Vm.IsBuildingMode = false; // none other -> select
        Assert.Contains("select", tool);
    }

    [AvaloniaFact]
    public void SetSplineTerrainBuildingPresets()
    {
        var h = Loaded();
        h.Vm.PushSplineDraftType = (_, _) => { };
        h.Vm.PushTerrainDraftType = _ => { };
        h.Vm.PushBuildingDraftType = _ => { };
        h.Vm.PushToolModeRequested = _ => { };

        h.Vm.SetSplinePreset("river", "canal");
        Assert.Equal("river", h.Vm.SplineKind);
        Assert.True(h.Vm.IsSplineMode);
        h.Vm.SetSplinePreset("road", "motorway"); // already in spline mode -> push again
        Assert.Equal("motorway", h.Vm.SplinePreset);

        h.Vm.SetTerrainType("forest");
        Assert.Equal("forest", h.Vm.TerrainType);
        Assert.True(h.Vm.IsTerrainMode);
        h.Vm.SetTerrainType("water");

        h.Vm.SetBuildingType("school");
        Assert.Equal("school", h.Vm.BuildingType);
        Assert.True(h.Vm.IsBuildingMode);
        h.Vm.SetBuildingType("police");
    }

    [AvaloniaFact]
    public void Toggle3D_AndBuildingScale()
    {
        var h = Loaded();
        bool? toggled = null;
        double? scale = null;
        h.Vm.PushToggle3D = v => toggled = v;
        h.Vm.PushSetBuildingScale = v => scale = v;
        h.Vm.Is3DMode = true;
        Assert.True(toggled);
        h.Vm.BuildingScale = 2.5;
        Assert.Equal(2.5, scale);
    }

    // ── Spline selection ────────────────────────────────────────────
    [AvaloniaFact]
    public void Spline_SelectColorsKnotsPresetDelete()
    {
        var h = Loaded();
        var colorPushes = 0;
        string? deleted = null;
        var moves = new List<int>();
        h.Vm.PushSetSplineColors = (_, _, _, _) => colorPushes++;
        h.Vm.PushDeleteSpline = id => deleted = id;
        h.Vm.PushMoveSplineZ = (_, d) => moves.Add(d);
        h.Vm.PushSetSplineClosed = (_, _) => { };
        h.Vm.PushSetSplinePreset = (_, _, _) => { };
        h.Vm.PushSetKnotBlend = (_, _, _) => { };
        h.Vm.PushSetKnotType = (_, _, _) => { };
        h.Vm.PushSetKnotSharpness = (_, _, _) => { };
        h.Vm.PushSetKnotMarkingStyle = (_, _, _) => { };
        h.Vm.PushSetSplineMarkingStyle = (_, _) => { };

        h.Vm.SetSelectedSpline("sp1", "road", "residential", 0, "", 1.0, "", "", "#101010", "#202020", "#303030", true, 0.2);
        Assert.True(h.Vm.HasSelectedSpline);
        Assert.Equal("Knot 1", h.Vm.SelectedKnotLabel);

        h.Vm.SelectedSplineFillColor = Colors.Red;     // pushes colors
        h.Vm.SelectedSplineClosed = false;
        h.Vm.SelectedKnotBlend = 0.5;                  // pushes knot blend
        h.Vm.SelectedKnotSharpness = 0.9;
        Assert.True(colorPushes > 0);

        h.Vm.SetSelectedKnotType("sharp");
        Assert.Equal("sharp", h.Vm.SelectedKnotType);
        h.Vm.SetSelectedKnotMarkingStyle("dashed");
        h.Vm.SetSelectedSplineMarkingStyle("solid");
        h.Vm.ChangeSelectedSplinePreset("river", "canal");
        Assert.Equal("canal", h.Vm.SelectedSplinePreset);

        h.Vm.MoveSelectedSplineForwardCommand.Execute(null);
        h.Vm.MoveSelectedSplineBackwardCommand.Execute(null);
        Assert.Equal(new[] { 1, -1 }, moves);

        h.Vm.ResetSplineColorsCommand.Execute(null);
        h.Vm.DeleteSelectedSplineCommand.Execute(null);
        Assert.Equal("sp1", deleted);
        Assert.False(h.Vm.HasSelectedSpline);
    }

    [AvaloniaFact]
    public void Spline_NoSelection_Guards()
    {
        var h = Loaded();
        h.Vm.ResetSplineColorsCommand.Execute(null);
        h.Vm.DeleteSelectedSplineCommand.Execute(null);
        h.Vm.MoveSelectedSplineForwardCommand.Execute(null);
        h.Vm.MoveSelectedSplineBackwardCommand.Execute(null);
        h.Vm.SetSelectedKnotType("x");
        h.Vm.SetSelectedKnotMarkingStyle("x");
        h.Vm.SetSelectedSplineMarkingStyle("x");
        h.Vm.ChangeSelectedSplinePreset("road", "x");
        Assert.False(h.Vm.HasSelectedSpline);
    }

    // ── Pin / Label / Shape / Building selection ────────────────────
    [AvaloniaFact]
    public void Pin_Selection_ColorPush()
    {
        var h = Loaded();
        (string id, string hex)? pushed = null;
        h.Vm.PushUpdatePinColor = (id, hex) => pushed = (id, hex);
        h.Vm.SetSelectedPin("pin1", "#FF0000");
        Assert.True(h.Vm.HasSelectedPin);
        h.Vm.SelectedPinColor = Colors.Blue;
        Assert.NotNull(pushed);
        h.Vm.SetSelectedPin(null, null);
        h.Vm.SelectedPinColor = Colors.Green; // no id -> no push
    }

    [AvaloniaFact]
    public void Label_Selection_AllProps()
    {
        var h = Loaded();
        var fontSize = new List<double>();
        h.Vm.PushSetLabelFontSize = (_, s) => fontSize.Add(s);
        h.Vm.PushSetLabelFontFamily = (_, _) => { };
        h.Vm.PushSetLabelAlign = (_, _) => { };
        h.Vm.PushSetLabelColor = (_, _) => { };
        string? deleted = null;
        h.Vm.PushDeleteLabel = id => deleted = id;

        h.Vm.SetSelectedLabel("lbl1", 0, "", "", "");
        Assert.True(h.Vm.HasSelectedLabel);
        Assert.Equal(18, h.Vm.SelectedLabelFontSize); // <=0 -> default 18
        Assert.Equal("Default", h.Vm.SelectedLabelFontDisplay);

        h.Vm.SelectedLabelFontSize = 24;
        h.Vm.SelectedLabelFontFamily = "Georgia, 'Times New Roman', serif";
        Assert.Equal("Serif", h.Vm.SelectedLabelFontDisplay);
        h.Vm.SetSelectedLabelAlign("left");
        Assert.Equal("left", h.Vm.SelectedLabelAlign);
        h.Vm.SelectedLabelColor = Colors.Cyan;
        Assert.Contains(24, fontSize);

        h.Vm.DeleteSelectedLabelCommand.Execute(null);
        Assert.Equal("lbl1", deleted);
        h.Vm.DeleteSelectedLabelCommand.Execute(null); // no selection guard
    }

    [AvaloniaFact]
    public void Shape_Selection_AllProps()
    {
        var h = Loaded();
        h.Vm.PushSetShapeColor = (_, _) => { };
        h.Vm.PushSetShapeSmooth = (_, _) => { };
        h.Vm.PushSetShapeBlend = (_, _) => { };
        h.Vm.PushSetShapeType = (_, _) => { };
        string? deleted = null;
        var moves = new List<int>();
        h.Vm.PushDeleteShape = id => deleted = id;
        h.Vm.PushMoveShapeZ = (_, d) => moves.Add(d);

        h.Vm.SetSelectedShape("sh1", "", "", true, 0);
        Assert.True(h.Vm.HasSelectedShape);
        Assert.Equal("grass", h.Vm.SelectedShapeType);
        h.Vm.SelectedShapeColor = Colors.Brown;
        h.Vm.SelectedShapeSmooth = false;
        h.Vm.SelectedShapeBlend = 0.3;
        h.Vm.SetSelectedShapeType("forest");
        Assert.Equal("forest", h.Vm.SelectedShapeType);
        h.Vm.MoveSelectedShapeForwardCommand.Execute(null);
        h.Vm.MoveSelectedShapeBackwardCommand.Execute(null);
        Assert.Equal(new[] { 1, -1 }, moves);
        h.Vm.DeleteSelectedShapeCommand.Execute(null);
        Assert.Equal("sh1", deleted);
    }

    [AvaloniaFact]
    public void Building_Selection_AllProps()
    {
        var h = Loaded();
        h.Vm.PushSetBuildingRoof = (_, _, _) => { };
        h.Vm.PushSetBuildingFloors = (_, _) => { };
        h.Vm.PushSetBuildingPlanZoom = (_, _) => { };
        h.Vm.PushSetBuildingType = (_, _) => { };
        h.Vm.PushEditBuildingPlan = _ => { };
        string? deleted = null;
        var moves = new List<int>();
        h.Vm.PushDeleteBuilding = id => deleted = id;
        h.Vm.PushMoveBuildingZ = (_, d) => moves.Add(d);

        h.Vm.SetSelectedBuilding("bd1", "", "", 0.5, 2, 0);
        Assert.True(h.Vm.HasSelectedBuilding);
        Assert.Equal("singleFamily", h.Vm.SelectedBuildingType);
        Assert.Equal("gable", h.Vm.SelectedBuildingRoofKind);
        Assert.Equal(4, h.Vm.SelectedBuildingPlanZoom); // <=0 -> 4

        h.Vm.SelectedBuildingRoofPitch = 0.8;
        h.Vm.SelectedBuildingFloorCount = 3;
        h.Vm.SelectedBuildingPlanZoom = 6;
        h.Vm.SetSelectedBuildingType("school");
        h.Vm.SetSelectedBuildingRoofKind("flat");
        Assert.Equal("flat", h.Vm.SelectedBuildingRoofKind);

        Assert.True(h.Vm.EditSelectedBuildingPlanCommand.CanExecute(null));
        h.Vm.EditSelectedBuildingPlanCommand.Execute(null);
        h.Vm.OnFloorPlanEditEntered();
        Assert.True(h.Vm.IsFloorPlanEditActive);
        Assert.False(h.Vm.EditSelectedBuildingPlanCommand.CanExecute(null));
        h.Vm.OnFloorPlanEditExited();
        Assert.False(h.Vm.IsFloorPlanEditActive);

        h.Vm.MoveSelectedBuildingForwardCommand.Execute(null);
        h.Vm.MoveSelectedBuildingBackwardCommand.Execute(null);
        Assert.Equal(new[] { 1, -1 }, moves);
        h.Vm.DeleteSelectedBuildingCommand.Execute(null);
        Assert.Equal("bd1", deleted);
    }

    // ── Border ──────────────────────────────────────────────────────
    [AvaloniaFact]
    public void Border_EditClearAndPush()
    {
        var h = Loaded();
        (string hex, double w)? outline = null;
        var cleared = false;
        h.Vm.PushSetBorderOutline = (hex, w) => outline = (hex, w);
        h.Vm.PushClearBorder = () => cleared = true;
        h.Vm.PushToolModeRequested = _ => { };

        h.Vm.SetBorderSelection(true, "#000000", 6);
        Assert.True(h.Vm.BorderEditActive);
        h.Vm.BorderOutlineColor = Colors.White;
        h.Vm.BorderOutlineWidth = 8;
        Assert.NotNull(outline);

        h.Vm.EditMapBorderCommand.Execute(null);
        h.Vm.ClearMapBorderCommand.Execute(null);
        Assert.True(cleared);
        Assert.False(h.Vm.BorderEditActive);
    }

    // ── Layer tree CRUD ─────────────────────────────────────────────
    [AvaloniaFact]
    public async Task Layer_AddChildDeleteSelectExpand()
    {
        var h = Loaded();
        h.Vm.PushActiveLayerRequested = _ => { };
        h.Vm.PushMapJsonRequested = _ => { };

        await h.Vm.AddLayerCommand.ExecuteAsync(null);
        Assert.NotNull(h.Vm.SelectedNode);

        var group = h.Vm.VisibleRows.First(r => r.NodeId == "G1");
        await h.Vm.AddChildLayerCommand.ExecuteAsync(group);

        h.Vm.SelectNodeCommand.Execute(group);
        Assert.True(h.Vm.HasSelectedNode);
        Assert.Equal("G1", h.Vm.ActiveLayerId);

        h.Vm.ToggleExpandCommand.Execute(group);
        Assert.False(group.IsExpanded);
        h.Vm.ToggleExpandCommand.Execute(group);

        h.Vm.ShowConfirmDialog = (_, _) => Task.FromResult(true);
        var added = h.Vm.VisibleRows.First(r => r.Name.StartsWith("Layer"));
        await h.Vm.DeleteNodeCommand.ExecuteAsync(added);
    }

    [AvaloniaFact]
    public async Task Layer_ToggleHiddenLockedRename()
    {
        var h = Loaded();
        h.Vm.PushMapJsonRequested = _ => { };
        var leaf = h.Vm.VisibleRows.First(r => r.NodeId == "L1");
        await h.Vm.ToggleNodeHiddenCommand.ExecuteAsync(leaf);
        Assert.True(leaf.Hidden);
        await h.Vm.ToggleNodeLockedCommand.ExecuteAsync(leaf);
        Assert.True(leaf.Locked);
        await h.Vm.CommitNodeRenameAsync(leaf, "Renamed Leaf");
        Assert.Equal("Renamed Leaf", leaf.Name);
        await h.Vm.CommitNodeRenameAsync(leaf, "   "); // blank -> no-op
        await h.Vm.CommitNodeRenameAsync(leaf, "Renamed Leaf"); // unchanged
    }

    [AvaloniaFact]
    public async Task Layer_MoveNode_BeforeAfterInside_AndDescendantReject()
    {
        var h = Loaded();
        h.Vm.PushMapJsonRequested = _ => { };
        // add a second root layer to move around
        await h.Vm.AddLayerCommand.ExecuteAsync(null);
        var newId = h.Vm.SelectedNode!.NodeId;

        await h.Vm.MoveNodeAsync(newId, "G1", NodeDropPosition.Before);
        await h.Vm.MoveNodeAsync(newId, "G1", NodeDropPosition.After);
        await h.Vm.MoveNodeAsync(newId, "G1", NodeDropPosition.Inside);
        // descendant reject: move group into its own child
        await h.Vm.MoveNodeAsync("G1", "L1", NodeDropPosition.Inside);
        // same id no-op
        await h.Vm.MoveNodeAsync("G1", "G1", NodeDropPosition.Before);
        await h.Vm.MoveNodeToRootAsync(newId);
    }

    // ── Node / image / element properties ───────────────────────────
    [AvaloniaFact]
    public async Task NodeProperties_OpacityZoomConnectedMember()
    {
        var h = Loaded();
        h.Vm.PushMapJsonRequested = _ => { };
        var group = h.Vm.VisibleRows.First(r => r.NodeId == "G1");
        await h.Vm.SetNodeOpacityAsync(group, 0.5);
        Assert.Equal(0.5, group.Opacity);
        await h.Vm.SetNodeOpacityAsync(group, 0.5); // unchanged -> early return
        await h.Vm.SetNodeZoomRangeAsync(group, 1, 10);
        Assert.Equal(1, group.MinZoom);
        await h.Vm.SetNodeConnectedSetAsync(group, true);
        Assert.True(group.IsConnectedSet); // defaults member to first child "L1"
        await h.Vm.SetNodeActiveMemberAsync(group, null); // differs from "L1" -> applies
        Assert.Null(group.ActiveMemberLayerId);
        await h.Vm.SetNodeActiveMemberAsync(group, null); // unchanged -> early return
    }

    [AvaloniaFact]
    public async Task Image_ZoomRangeAndIsolate()
    {
        var h = Loaded();
        h.Vm.PushUpdateImageZoomRange = (_, _, _) => { };
        h.Vm.PushIsolateImage = _ => { };
        var leaf = h.Vm.VisibleRows.First(r => r.NodeId == "L1");
        var img = leaf.Images[0];
        await h.Vm.SetImageZoomRangeAsync(img, 2, 8);
        Assert.Equal(2, img.MinZoom);
        h.Vm.ToggleIsolateImage(img);
        Assert.True(img.IsIsolated);
        h.Vm.ToggleIsolateImage(img);
        Assert.False(img.IsIsolated);
    }

    [AvaloniaFact]
    public async Task Element_ZoomRange_AllKinds_AndIsolate()
    {
        var h = Loaded();
        h.Vm.PushSetElementZoomRange = (_, _, _, _) => { };
        h.Vm.PushIsolateElement = (_, _) => { };
        var leaf = h.Vm.VisibleRows.First(r => r.NodeId == "L1");

        await h.Vm.SetElementZoomRangeAsync(leaf.Splines[0], 1, 5);
        await h.Vm.SetElementZoomRangeAsync(leaf.Shapes[0], 1, 5);
        await h.Vm.SetElementZoomRangeAsync(leaf.Buildings[0], 1, 5);
        await h.Vm.SetElementZoomRangeAsync(leaf.Pins[0], 1, 5);
        await h.Vm.SetElementZoomRangeAsync(leaf.Labels[0], 1, 5);
        Assert.Equal(1, leaf.Splines[0].MinZoom);

        h.Vm.ToggleIsolateElement(leaf.Splines[0]);
        Assert.True(leaf.Splines[0].IsIsolated);
        h.Vm.ToggleIsolateElement(leaf.Splines[0]);
    }

    [AvaloniaFact]
    public void SelectImageFromView_SelectsOwnerNode()
    {
        var h = Loaded();
        h.Vm.PushActiveLayerRequested = _ => { };
        h.Vm.SelectImageFromView("img1");
        Assert.Equal("L1", h.Vm.SelectedNode!.NodeId);
        h.Vm.SelectImageFromView("nonexistent"); // owner null -> no-op
    }

    // ── Profiles / persistence / image add ──────────────────────────
    [AvaloniaFact]
    public async Task ManageProfiles_AppliesResult()
    {
        var h = Loaded();
        h.Vm.PushMapJsonRequested = _ => { };
        h.Vm.ManageProfilesRequested = list => Task.FromResult<List<MapProfile>?>(
            new List<MapProfile> { new() { Id = "cp2", Kind = "river", Name = "New" } });
        await h.Vm.ManageProfilesCommand.ExecuteAsync(null);
        Assert.Contains(h.Vm.CustomProfileChoices, c => c.Name == "New");

        h.Vm.ManageProfilesRequested = _ => Task.FromResult<List<MapProfile>?>(null); // cancelled
        await h.Vm.ManageProfilesCommand.ExecuteAsync(null);
    }

    [AvaloniaFact]
    public async Task AddImage_PicksAndForwards()
    {
        var h = Loaded();
        (string p, double w, double ht)? added = null;
        h.Vm.AddImageRequested = (p, w, ht) => added = (p, w, ht);
        h.Vm.PickImageRequested = () => Task.FromResult<(string, double, double)?>(("maps/new.png", 100, 80));
        await h.Vm.AddImageCommand.ExecuteAsync(null);
        Assert.Equal("maps/new.png", added!.Value.p);

        h.Vm.PickImageRequested = () => Task.FromResult<(string, double, double)?>(null); // cancelled
        await h.Vm.AddImageCommand.ExecuteAsync(null);
    }

    [AvaloniaFact]
    public async Task PersistFromView_RoundTrip()
    {
        var h = Loaded();
        var json = System.Text.Json.JsonSerializer.Serialize(SampleMap());
        h.Vm.RequestMapJsonFromViewAsync = () => Task.FromResult<string?>(json);
        await h.Vm.PersistFromViewAsync();
        await h.Map.Received().SaveMapAsync(Arg.Any<MapData>());

        h.Vm.RequestMapJsonFromViewAsync = () => Task.FromResult<string?>(""); // empty -> no-op
        await h.Vm.PersistFromViewAsync();
        h.Vm.RequestMapJsonFromViewAsync = () => Task.FromResult<string?>("{bad json"); // catch
        await h.Vm.PersistFromViewAsync();
    }

    [AvaloniaFact]
    public async Task UpdateInitialView_Saves()
    {
        var h = Loaded();
        await h.Vm.UpdateInitialViewAsync(5, 6, 2);
        Assert.Equal(5, h.Vm.ActiveMap!.InitialView.CenterX);
        await h.Map.Received().SaveMapAsync(h.Vm.ActiveMap);
    }

    // ── Sub view-models ─────────────────────────────────────────────
    [AvaloniaFact]
    public void SelectImageFromView_ExpandsCollapsedAncestors()
    {
        var h = Loaded();
        h.Vm.PushActiveLayerRequested = _ => { };
        h.Vm.PushMapJsonRequested = _ => { };
        var group = h.Vm.VisibleRows.First(r => r.NodeId == "G1");
        h.Vm.ToggleExpandCommand.Execute(group); // collapse G1
        Assert.False(group.IsExpanded);
        h.Vm.SelectImageFromView("img1"); // must expand ancestors to reveal L1
        Assert.True(group.IsExpanded);
        Assert.Equal("L1", h.Vm.SelectedNode!.NodeId);
    }

    [AvaloniaFact]
    public void EmptyDisplayGetters()
    {
        var h = Loaded();
        // marking/knot displays return placeholder text when their values are empty
        h.Vm.SetSelectedSpline("sp1", "road", "residential", 0, "", 1.0, "", "", "", "", "", false, 0);
        Assert.Equal("(preset default)", h.Vm.SelectedSplineMarkingDisplay);
        Assert.Equal("(base type)", h.Vm.SelectedKnotTypeDisplay);
        Assert.Equal("(spline default)", h.Vm.SelectedKnotMarkingDisplay);
    }

    [AvaloniaFact]
    public async Task IsDescendant_DeepRejectsMove()
    {
        var h = Build();
        var grandchild = new MapLayerNode { Id = "GC", Name = "GC" };
        var child = new MapLayerNode { Id = "L1", Name = "L1", Children = { grandchild } };
        var root = new MapLayerNode { Id = "G1", Name = "G1", Expanded = true, Children = { child } };
        var other = new MapLayerNode { Id = "OTHER", Name = "Other" };
        var map = new MapData { Id = "m1", Name = "M", Layers = { root, other } };
        h.Maps["m1"] = map;
        h.Book.Maps.Add(new MapReference { Id = "m1", Name = "M" });
        h.Vm.RefreshMaps();
        h.Vm.SelectedMap = h.Vm.Maps[0];
        h.Vm.PushMapJsonRequested = _ => { };
        // Move G1 into its grandchild GC -> IsDescendant recurses into children -> reject
        await h.Vm.MoveNodeAsync("G1", "GC", NodeDropPosition.Inside);
        // Valid move: G1 (multi-level subtree) onto unrelated OTHER -> IsDescendant iterates fully, returns false
        await h.Vm.MoveNodeAsync("G1", "OTHER", NodeDropPosition.Before);
        Assert.NotNull(h.Vm.ActiveMap);
    }

    [AvaloniaFact]
    public async Task SetElementZoomRange_UnknownKind_NoOp()
    {
        var h = Loaded();
        var bogus = new LayerElementRow("bogus", "x", "X", null, null);
        await h.Vm.SetElementZoomRangeAsync(bogus, 1, 5); // ApplyElementZoom returns false
        Assert.Null(bogus.MinZoom);
    }

    [AvaloniaFact]
    public async Task PersistFromView_NodeSetChanged_Rebuilds()
    {
        var h = Loaded();
        h.Vm.PushMapJsonRequested = _ => { };
        // JSON with a different node set (extra root layer) -> SetEquals false -> RebuildTree
        var changed = SampleMap();
        changed.Layers.Add(new MapLayerNode { Id = "EXTRA", Name = "Extra" });
        var json = System.Text.Json.JsonSerializer.Serialize(changed);
        h.Vm.RequestMapJsonFromViewAsync = () => Task.FromResult<string?>(json);
        await h.Vm.PersistFromViewAsync();
        Assert.Contains(h.Vm.VisibleRows, r => r.NodeId == "EXTRA");
    }

    [AvaloniaFact]
    public async Task PersistFromView_SameNodes_ImageAndElementSetsChanged()
    {
        var h = Loaded();
        h.Vm.PushMapJsonRequested = _ => { };
        // Same node ids (G1, L1) but L1's image + spline + pin sets differ -> in-place rebuild of those lists
        var changed = SampleMap();
        var leaf = changed.Layers[0].Children[0];
        leaf.Images.Clear();
        leaf.Images.Add(new MapImage { Id = "imgX", Path = "maps/x.png" });
        leaf.Splines.Clear();
        leaf.Splines.Add(new MapSpline { Id = "spX", Kind = "river", Preset = "canal" });
        changed.Pins.Clear();
        changed.Pins.Add(new MapPin { Id = "pinX", LayerId = "L1", Label = "New" });
        var json = System.Text.Json.JsonSerializer.Serialize(changed);
        h.Vm.RequestMapJsonFromViewAsync = () => Task.FromResult<string?>(json);
        await h.Vm.PersistFromViewAsync();
        var row = h.Vm.VisibleRows.First(r => r.NodeId == "L1");
        Assert.Contains(row.Images, i => i.ImageId == "imgX");
        Assert.Contains(row.Splines, s => s.Id == "spX");
    }

    [AvaloniaFact]
    public void SubViewModels_Construct()
    {
        var node = new MapLayerNode { Id = "n", Name = "N", Opacity = 0.7, Hidden = true, MinZoom = 2 };
        var row = new LayerNodeRow(node, null, 1);
        Assert.Equal("N", row.Name);
        Assert.True(row.Hidden);
        Assert.Equal(0.7, row.Opacity);
        Assert.False(row.HasChildren);
        Assert.Equal(16, row.IndentThickness.Left);

        var img = new ImageRow(new MapImage { Id = "i", Path = "a/b/pic.png", MinZoom = 1 });
        Assert.Equal("pic.png", img.DisplayName);
        Assert.Equal("i", img.ImageId);

        var el = new LayerElementRow("spline", "s", "Road", 1, 9);
        Assert.Equal("spline", el.Kind);
        Assert.Equal("Road", el.DisplayName);

        var choice = new MapProfileChoice("", "custom:1", "Name");
        Assert.Equal("road", choice.Kind); // empty -> road
        Assert.Equal("custom:1", choice.PresetKey);

        var font = new LabelFontChoice("Serif", "serif");
        Assert.Equal("Serif", font.Name);
    }
}
