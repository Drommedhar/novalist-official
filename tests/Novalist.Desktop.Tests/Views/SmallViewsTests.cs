using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Novalist.Desktop.Localization;
using Novalist.Desktop.Services;
using Novalist.Desktop.Tests.TestHelpers;
using Novalist.Desktop.ViewModels;
using Novalist.Desktop.Views;
using Novalist.Sdk.Models;
using Xunit;

namespace Novalist.Desktop.Tests.Views;

[Collection("Avalonia")]
public class SmallViewsTests
{
    private static void InitLoc() => Loc.Instance.Initialize(
        Path.Combine(AppContext.BaseDirectory, "Assets", "Locales"), "en");

    private static RoutedEventArgs R() => new();

    // ── WelcomeView ─────────────────────────────────────────────────
    [AvaloniaFact]
    public void Welcome_RemoveRecent_AllBranches()
    {
        using var dir = new TempDir();
        var vm = new WelcomeViewModel(
            new[] { new RecentProject { Name = "A", Path = dir.Path, LastOpened = DateTime.UtcNow } },
            new List<ProjectTemplate>());
        var view = new WelcomeView { DataContext = vm };
        var card = vm.RecentProjects[0];

        DialogHost.Invoke(view, "OnRemoveRecentClick", new Button(), R());        // not MenuItem
        DialogHost.Invoke(view, "OnRemoveRecentClick", new MenuItem(), R());       // Tag not card
        DialogHost.Invoke(view, "OnRemoveRecentClick", new MenuItem { Tag = card }, R()); // wrong DC? still vm
        Assert.Empty(vm.RecentProjects); // removed

        var noDc = new WelcomeView();
        DialogHost.Invoke(noDc, "OnRemoveRecentClick", new MenuItem { Tag = card }, R()); // DataContext not vm
    }

    // ── PlotGridView ────────────────────────────────────────────────
    [AvaloniaFact]
    public void PlotGrid_RenameDelete_AllBranches()
    {
        var plot = Substitute.For<IPlotlineService>();
        var vm = new PlotGridViewModel(Substitute.For<IProjectService>(), plot)
        {
            ShowInputDialog = (_, _, _) => System.Threading.Tasks.Task.FromResult<string?>(null), // cancel rename
            ShowConfirmDialog = (_, _) => System.Threading.Tasks.Task.FromResult(false),           // decline delete
        };
        var row = new PlotGridRow(new PlotlineData { Name = "P" }, new ObservableCollection<PlotGridCell>(), plot, () => { });
        var view = new PlotGridView { DataContext = vm };

        DialogHost.Invoke(view, "OnRenamePlotlineClick", new MenuItem { Tag = row }, R());
        DialogHost.Invoke(view, "OnDeletePlotlineClick", new MenuItem { Tag = row }, R());
        DialogHost.Invoke(view, "OnRenamePlotlineClick", new Button(), R());  // not MenuItem
        DialogHost.Invoke(view, "OnDeletePlotlineClick", new Button(), R());

        var noDc = new PlotGridView();
        DialogHost.Invoke(noDc, "OnRenamePlotlineClick", new MenuItem { Tag = row }, R()); // Vm null
        DialogHost.Invoke(noDc, "OnDeletePlotlineClick", new MenuItem { Tag = row }, R());
    }

    // ── ImageGalleryView ────────────────────────────────────────────
    [AvaloniaFact]
    public void ImageGallery_Tap_PreviewHandlers()
    {
        var vm = new ImageGalleryViewModel(Substitute.For<IEntityService>());
        var view = new ImageGalleryView { DataContext = vm };
        var item = new ImageGalleryItem { RelativePath = "img/a.png", Name = "a" };

        DialogHost.Invoke(view, "OnCardTapped", new Border { DataContext = item }, (TappedEventArgs?)null);
        Assert.True(vm.IsPreviewOpen);
        Assert.Equal("img/a.png", vm.PreviewImagePath);

        DialogHost.Invoke(view, "OnCardTapped", new Border(), (TappedEventArgs?)null); // no item -> no-op

        DialogHost.Invoke(view, "OnPreviewOverlayPressed", view, DialogHost.UninitializedArgs<PointerPressedEventArgs>());
        Assert.False(vm.IsPreviewOpen);
        DialogHost.Invoke(view, "OnPreviewContentPressed", view, DialogHost.UninitializedArgs<PointerPressedEventArgs>());

        var noDc = new ImageGalleryView();
        DialogHost.Invoke(noDc, "OnPreviewOverlayPressed", noDc, DialogHost.UninitializedArgs<PointerPressedEventArgs>()); // DC not vm
    }

    // ── HotkeySettingsView ──────────────────────────────────────────
    [AvaloniaFact]
    public void HotkeySettings_OnKeyDown_RecordingAndPassthrough()
    {
        var svc = Substitute.For<IHotkeyService>();
        svc.GetAllDescriptors().Returns(new[] { new HotkeyDescriptor { ActionId = "a", DisplayName = "Alpha", Category = "Edit", DefaultGesture = "Ctrl+A" } });
        svc.GetGesture("a").Returns("Ctrl+A");
        var vm = new HotkeySettingsViewModel(svc);
        var view = new HotkeySettingsView { DataContext = vm };

        // not recording -> base path
        DialogHost.PressKey(view, Key.B);

        // recording -> HandleRecordingKeyDown true -> Handled
        vm.StartRecordingCommand.Execute(vm.AllItems[0]);
        Assert.True(vm.IsRecording);
        DialogHost.PressKey(view, Key.B);
        Assert.False(vm.IsRecording); // applied

        var noDc = new HotkeySettingsView();
        DialogHost.PressKey(noDc, Key.B); // vm null -> base
    }

    // ── SmartListsPanelView ─────────────────────────────────────────
    [AvaloniaFact]
    public void SmartLists_Handlers_AllBranches()
    {
        var explorer = new ExplorerViewModel(Substitute.For<IProjectService>());
        var item = new SmartListItemViewModel(new SmartList { Id = "1", Name = "L" },
            Substitute.For<ISmartListService>(), null);
        var view = new SmartListsPanelView { DataContext = explorer };

        // header toggle (sender Control with Tag)
        DialogHost.Invoke(view, "OnSmartListHeaderPressed", new Border { Tag = item },
            DialogHost.UninitializedArgs<PointerPressedEventArgs>());
        Assert.True(item.IsExpanded);

        // direct-Tag control path of FindItem
        DialogHost.Invoke(view, "OnEditSmartListClick", new Border { Tag = item }, R());
        DialogHost.Invoke(view, "OnRefreshSmartListClick", new Border { Tag = item }, R());
        DialogHost.Invoke(view, "OnDeleteSmartListClick", new Border { Tag = item }, R());

        // MenuItem -> ContextMenu(Tag) walk-up path
        var cm = new ContextMenu { Tag = item };
        var mi = new MenuItem();
        cm.Items.Add(mi);
        DialogHost.Invoke(view, "OnEditSmartListClick", mi, R());

        // MenuItem with no ContextMenu / no tag -> FindItem null
        DialogHost.Invoke(view, "OnEditSmartListClick", new MenuItem(), R());
        DialogHost.Invoke(view, "OnRefreshSmartListClick", new MenuItem(), R());

        // Vm null
        var noDc = new SmartListsPanelView();
        DialogHost.Invoke(noDc, "OnEditSmartListClick", new Border { Tag = item }, R());
        DialogHost.Invoke(noDc, "OnDeleteSmartListClick", new Border { Tag = item }, R());
    }

    // ── FocusPeekCardView ───────────────────────────────────────────
    [AvaloniaFact]
    public void FocusPeek_DataContext_Pointer_ComboGuard()
    {
        var vm = new FocusPeekViewModel();
        bool exitRaised = false;
        vm.PointerExitedRequested = () => exitRaised = true; // so OnPointerExited's ?.Invoke() runs
        var view = new FocusPeekCardView();
        view.DataContext = vm; // -> OnDataContextChanged
        DialogHost.Show(view); // realize so ComboBox descendants exist

        DialogHost.Invoke(view, "OnPointerEntered", view, DialogHost.UninitializedArgs<PointerEventArgs>());
        DialogHost.Invoke(view, "OnPointerExited", view, DialogHost.UninitializedArgs<PointerEventArgs>());
        Assert.True(exitRaised);

        // Realize the image ComboBox (gated by IsOpen + HasMultipleImages), open it,
        // so the exit-guard (IsAnyComboBoxDropDownOpen) early-returns.
        vm.Images.Add(new FocusPeekImageItem { Name = "a", Path = "a.png" });
        vm.Images.Add(new FocusPeekImageItem { Name = "b", Path = "b.png" });
        vm.IsOpen = true;
        var host = new Window { Width = 400, Height = 600, Content = view };
        host.Show();
        DialogHost.RunJobs();
        var combo = view.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault();
        Assert.NotNull(combo);
        combo!.IsDropDownOpen = true;
        DialogHost.RunJobs();
        DialogHost.Invoke(view, "OnPointerExited", view, DialogHost.UninitializedArgs<PointerEventArgs>());
        DialogHost.Invoke(view, "OnDataContextChanged", view, EventArgs.Empty);
        host.Content = null;
        host.Close();
        DialogHost.RunJobs();

        var noVm = new FocusPeekCardView { DataContext = "x" };
        DialogHost.Invoke(noVm, "OnPointerEntered", noVm, DialogHost.UninitializedArgs<PointerEventArgs>());
    }

    // ── SettingsView ────────────────────────────────────────────────
    [AvaloniaFact]
    public void Settings_Close_ScrollToCategory()
    {
        InitLoc();
        var settings = Substitute.For<ISettingsService>();
        var app = new AppSettings();
        settings.Settings.Returns(app);
        settings.Effective.Returns(app);
        var proj = Substitute.For<IProjectService>();
        var vm = new SettingsViewModel(settings, proj);
        var view = new SettingsView { DataContext = vm }; // OnDataContextChanged subscribes
        // Not rendered: SettingsView's ToggleSwitch template needs the Fluent theme.
        // Named sections are populated by InitializeComponent; BringIntoView is a no-op off-tree.

        DialogHost.Invoke(view, "OnCloseClick", null, R());

        // ext_ prefix -> SectionExtensions.BringIntoView
        DialogHost.Invoke(view, "OnScrollToCategoryRequested", "ext_thing");

        // known key, force visible -> BringIntoView
        var sec = view.GetVisualNamed<Control>("SectionAppearance");
        if (sec != null) sec.IsVisible = true;
        DialogHost.Invoke(view, "OnScrollToCategoryRequested", "appearance");

        // known key, not visible -> short-circuits
        if (sec != null) sec.IsVisible = false;
        DialogHost.Invoke(view, "OnScrollToCategoryRequested", "appearance");

        // unknown key -> no-op
        DialogHost.Invoke(view, "OnScrollToCategoryRequested", "nope");

        // re-set DataContext (resubscribe path)
        view.DataContext = vm;
        // non-vm DataContext path
        view.DataContext = "x";
    }

    // ── CalendarView (drag handlers excluded; FindMonthDay testable) ─
    [AvaloniaFact]
    public void Calendar_FindMonthDay_Static()
    {
        var mi = typeof(CalendarView).GetMethod("FindMonthDay",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;

        Assert.Null(mi.Invoke(null, new object?[] { null })); // null source -> null

        var day = new CalendarMonthDay(DateTime.Today, true, new List<CalendarSceneEvent>(), _ => { }, _ => { });
        var ctrl = new Border { Tag = day };
        Assert.Same(day, mi.Invoke(null, new object?[] { ctrl })); // tagged control -> day

        Assert.Null(mi.Invoke(null, new object?[] { new Border() })); // untagged, no parent -> null

        _ = new CalendarView(); // ctor + InitializeComponent
    }
}
