using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Localization;
using Novalist.Desktop.Tests.TestHelpers;
using Novalist.Desktop.ViewModels;
using Novalist.Desktop.Views;
using Xunit;

namespace Novalist.Desktop.Tests.Views;

[Collection("Avalonia")]
public class ExplorerViewTests
{
    static ExplorerViewTests()
        => Loc.Instance.Initialize(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Locales"), "en");

    private static ExplorerViewModel BuildVm()
    {
        var proj = Substitute.For<IProjectService>();
        proj.GetChaptersOrdered().Returns(new System.Collections.Generic.List<ChapterData>());
        proj.GetScenesForChapter(Arg.Any<string>()).Returns(new System.Collections.Generic.List<SceneData>());
        return new ExplorerViewModel(proj);
    }

    private static RoutedEventArgs R() => new();

    // MenuItem whose parent chain reaches a ContextMenu carrying the tag.
    private static MenuItem MenuFor(object? tag, object? menuItemTag = null)
    {
        var cm = new ContextMenu { Tag = tag };
        var mi = new MenuItem { Tag = menuItemTag };
        cm.Items.Add(mi);
        return mi;
    }

    private static ChapterTreeItemViewModel Chapter() => new(new ChapterData { Title = "Ch", Guid = "g1" });
    private static SceneTreeItemViewModel Scene() => new(new SceneData { Title = "Sc", Id = "s1" }, new ChapterData { Title = "Ch", Guid = "g1" });

    [AvaloniaFact]
    public void ChapterContextHandlers()
    {
        var vm = BuildVm();
        var view = new ExplorerView { DataContext = vm };

        foreach (var h in new[] { "OnAddSceneClick", "OnSetChapterDateClick", "OnToggleChapterFavoriteClick",
            "OnRenameChapterClick", "OnDeleteChapterClick", "OnSetChapterActClick", "OnRemoveChapterFromActClick" })
        {
            DialogHost.Invoke(view, h, MenuFor(Chapter()), R());
            DialogHost.Invoke(view, h, MenuFor(null), R());     // null tag
            DialogHost.Invoke(new ExplorerView(), h, MenuFor(Chapter()), R()); // no vm
        }

        // bare MenuItem with no ContextMenu ancestor -> GetContextMenuTag walks to null
        DialogHost.Invoke(view, "OnAddSceneClick", new MenuItem(), R());
    }

    [AvaloniaFact]
    public void SceneContextHandlers()
    {
        var vm = BuildVm();
        var view = new ExplorerView { DataContext = vm };

        foreach (var h in new[] { "OnRenameSceneClick", "OnDeleteSceneClick", "OnArchiveSceneClick",
            "OnOpenSceneInSplitClick", "OnSetSceneDateClick", "OnToggleSceneFavoriteClick" })
        {
            DialogHost.Invoke(view, h, MenuFor(Scene()), R());
            DialogHost.Invoke(view, h, MenuFor(null), R());
            DialogHost.Invoke(new ExplorerView(), h, MenuFor(Scene()), R());
        }
    }

    [AvaloniaFact]
    public void Archived_And_SmartList_And_Act_Handlers()
    {
        var vm = BuildVm();
        var view = new ExplorerView { DataContext = vm };
        var arch = new ArchivedSceneItemViewModel(new SceneData { Title = "A", Id = "a1" }, "Ch");
        var act = new ActHeaderViewModel("Act I");
        var smart = new SmartListItemViewModel(new SmartList { Id = "1", Name = "L" }, Substitute.For<ISmartListService>(), null);

        DialogHost.Invoke(view, "OnRestoreArchivedSceneClick", MenuFor(arch), R());
        DialogHost.Invoke(view, "OnRestoreArchivedSceneClick", MenuFor(null), R());
        DialogHost.Invoke(view, "OnDeleteArchivedSceneClick", MenuFor(arch), R());
        DialogHost.Invoke(view, "OnDeleteArchivedSceneClick", MenuFor(null), R());
        DialogHost.Invoke(view, "OnArchivedScenePreviewMenuClick", MenuFor(arch), R());
        DialogHost.Invoke(view, "OnArchivedScenePreviewMenuClick", MenuFor(null), R());

        // Direct-tag tapped handler
        DialogHost.Invoke(view, "OnArchivedScenePreviewClick", new Border { Tag = arch }, (TappedEventArgs?)null);
        DialogHost.Invoke(view, "OnArchivedScenePreviewClick", new Border(), (TappedEventArgs?)null);
        DialogHost.Invoke(new ExplorerView(), "OnArchivedScenePreviewClick", new Border { Tag = arch }, (TappedEventArgs?)null);

        DialogHost.Invoke(view, "OnEditSmartListClick", MenuFor(smart), R());
        DialogHost.Invoke(view, "OnEditSmartListClick", MenuFor(null), R());
        DialogHost.Invoke(view, "OnDeleteSmartListClick", MenuFor(smart), R());
        DialogHost.Invoke(view, "OnDeleteSmartListClick", MenuFor(null), R());

        DialogHost.Invoke(view, "OnRenameActClick", MenuFor(act), R());
        DialogHost.Invoke(view, "OnDeleteActClick", MenuFor(act), R());

        // no-vm guards for the archived/smartlist handlers
        var bare = new ExplorerView();
        DialogHost.Invoke(bare, "OnRestoreArchivedSceneClick", MenuFor(arch), R());
        DialogHost.Invoke(bare, "OnDeleteArchivedSceneClick", MenuFor(arch), R());
        DialogHost.Invoke(bare, "OnArchivedScenePreviewMenuClick", MenuFor(arch), R());
        DialogHost.Invoke(bare, "OnEditSmartListClick", MenuFor(smart), R());
        DialogHost.Invoke(bare, "OnDeleteSmartListClick", MenuFor(smart), R());
    }

    [AvaloniaFact]
    public void SetSceneColor_Click()
    {
        var vm = BuildVm();
        var view = new ExplorerView { DataContext = vm };
        DialogHost.Invoke(view, "OnSetSceneColorClick", MenuFor(Scene(), menuItemTag: "#ff0000"), R());
        DialogHost.Invoke(view, "OnSetSceneColorClick", MenuFor(null), R()); // null scene -> return
        DialogHost.Invoke(new ExplorerView(), "OnSetSceneColorClick", MenuFor(Scene()), R()); // no vm
    }

    [AvaloniaFact]
    public void StatusPointer_DragLeave_Threshold_ContextMenuOpening()
    {
        var vm = BuildVm();
        var view = new ExplorerView { DataContext = vm };

        // OnStatusPointerPressed: Border ancestor carries the chapter
        var outer = new Border { Tag = Chapter() };
        var inner = new Border();
        outer.Child = inner;
        DialogHost.Invoke(view, "OnStatusPointerPressed", inner, DialogHost.UninitializedArgs<PointerPressedEventArgs>());
        // no ancestor tag -> return
        DialogHost.Invoke(view, "OnStatusPointerPressed", new Border(), DialogHost.UninitializedArgs<PointerPressedEventArgs>());
        // no vm / non-control sender
        DialogHost.Invoke(new ExplorerView(), "OnStatusPointerPressed", inner, DialogHost.UninitializedArgs<PointerPressedEventArgs>());

        // OnDragLeave
        var b = new Border();
        b.Classes.Add("dropTarget");
        DialogHost.Invoke(view, "OnDragLeave", b, (object?)null);
        Assert.DoesNotContain("dropTarget", b.Classes);
        DialogHost.Invoke(view, "OnDragLeave", new TextBox(), (object?)null);

        // HasExceededDragThreshold
        var thr = typeof(ExplorerView).GetMethod("HasExceededDragThreshold",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Assert.True((bool)thr.Invoke(view, new object[] { new Point(10, 10) })!);
        Assert.False((bool)thr.Invoke(view, new object[] { new Point(2, 2) })!);

        // Context-menu opening handlers (extension injection is excluded; ExtensionManager null -> no-op)
        var cm = new ContextMenu();
        DialogHost.Invoke(view, "OnChapterContextMenuOpening", cm, new System.ComponentModel.CancelEventArgs());
        DialogHost.Invoke(view, "OnSceneContextMenuOpening", cm, new System.ComponentModel.CancelEventArgs());
        DialogHost.Invoke(view, "OnChapterContextMenuOpening", new TextBox(), new System.ComponentModel.CancelEventArgs()); // not a ContextMenu
        DialogHost.Invoke(view, "OnSceneContextMenuOpening", new TextBox(), new System.ComponentModel.CancelEventArgs());
    }
}
