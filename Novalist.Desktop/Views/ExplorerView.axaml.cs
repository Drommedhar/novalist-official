using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Novalist.Desktop.ViewModels;
using Novalist.Sdk.Models;

namespace Novalist.Desktop.Views;

public partial class ExplorerView : UserControl
{
    private const string ChapterDragPrefix = "novalist-chapters:";
    private const string SceneDragPrefix = "novalist-scenes:";

    private ChapterTreeItemViewModel? _pendingChapterDrag;
    private SceneTreeItemViewModel? _pendingSceneDrag;
    private SceneTreeItemViewModel? _pendingSceneOpen;
    private Point _dragStartPoint;
    private PointerPressedEventArgs? _lastPointerPressed;

    public ExplorerView()
    {
        InitializeComponent();
    }

    private ExplorerViewModel? Vm => DataContext as ExplorerViewModel;

    // --- Pointer handlers ---

    private void OnChapterPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is null || sender is not Control control) return;

        var chapter = control.Tag as ChapterTreeItemViewModel
            ?? control.FindAncestorOfType<Border>()?.Tag as ChapterTreeItemViewModel;
        if (chapter == null) return;

        if (e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            e.Pointer.Capture(control);
            var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            Vm.HandleChapterSelection(chapter, ctrl, shift);
            _pendingChapterDrag = chapter;
            _pendingSceneDrag = null;
            _dragStartPoint = e.GetPosition(this);
            _lastPointerPressed = e;
        }
    }

    private void OnChapterPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Control control) return;
        if (e.InitialPressMouseButton != MouseButton.Left) return;

        e.Pointer.Capture(null);
        _pendingChapterDrag = null;
    }

    private void OnScenePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is null || sender is not Control control) return;

        var scene = control.Tag as SceneTreeItemViewModel
            ?? control.FindAncestorOfType<Border>()?.Tag as SceneTreeItemViewModel;
        if (scene == null) return;

        if (e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            e.Pointer.Capture(control);
            var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            Vm.HandleSceneSelection(scene, ctrl, shift, openScene: false);
            _pendingSceneDrag = scene;
            _pendingSceneOpen = !ctrl && !shift ? scene : null;
            _pendingChapterDrag = null;
            _dragStartPoint = e.GetPosition(this);
            _lastPointerPressed = e;
        }
    }

    private void OnScenePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Vm is null || sender is not Control control) return;
        if (e.InitialPressMouseButton != MouseButton.Left) return;

        e.Pointer.Capture(null);

        var scene = control.Tag as SceneTreeItemViewModel
            ?? control.FindAncestorOfType<Border>()?.Tag as SceneTreeItemViewModel;
        if (scene == null) return;

        if (_pendingSceneOpen == scene && !HasExceededDragThreshold(e.GetPosition(this)))
        {
            Vm.HandleSceneSelection(scene, ctrl: false, shift: false, openScene: true);
        }

        _pendingSceneOpen = null;
        _pendingSceneDrag = null;
    }

    private async void OnChapterPointerMoved(object? sender, PointerEventArgs e)
    {
        if (Vm is null || _pendingChapterDrag == null || _lastPointerPressed == null || sender is not Control control) return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed) return;
        if (!HasExceededDragThreshold(e.GetPosition(this))) return;

        var dragging = Vm.PrepareChapterDrag(_pendingChapterDrag).Select(chapter => chapter.Chapter.Guid).ToArray();
        e.Pointer.Capture(null);
        _pendingChapterDrag = null;
        if (dragging.Length == 0) return;

        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(ChapterDragPrefix + string.Join("|", dragging)));
        await DragDrop.DoDragDropAsync(_lastPointerPressed, data, DragDropEffects.Move);
    }

    private async void OnScenePointerMoved(object? sender, PointerEventArgs e)
    {
        if (Vm is null || _pendingSceneDrag == null || _lastPointerPressed == null || sender is not Control control) return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed) return;
        if (!HasExceededDragThreshold(e.GetPosition(this))) return;

        var dragging = Vm.PrepareSceneDrag(_pendingSceneDrag).Select(scene => scene.Scene.Id).ToArray();
        e.Pointer.Capture(null);
        _pendingSceneOpen = null;
        _pendingSceneDrag = null;
        if (dragging.Length == 0) return;

        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(SceneDragPrefix + string.Join("|", dragging)));
        await DragDrop.DoDragDropAsync(_lastPointerPressed, data, DragDropEffects.Move);
    }

    // --- Chapter context menu handlers ---

    private void OnAddSceneClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var chapter = GetContextMenuTag<ChapterTreeItemViewModel>(sender);
        Vm.CreateSceneCommand.Execute(chapter);
    }

    private void OnSetChapterDateClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var chapter = GetContextMenuTag<ChapterTreeItemViewModel>(sender);
        Vm.SetChapterDateCommand.Execute(chapter);
    }

    private void OnToggleChapterFavoriteClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var chapter = GetContextMenuTag<ChapterTreeItemViewModel>(sender);
        Vm.ToggleChapterFavoriteCommand.Execute(chapter);
    }

    private void OnRenameChapterClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var chapter = GetContextMenuTag<ChapterTreeItemViewModel>(sender);
        Vm.RenameChapterCommand.Execute(chapter);
    }

    private void OnDeleteChapterClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var chapter = GetContextMenuTag<ChapterTreeItemViewModel>(sender);
        if (chapter != null)
        {
            Vm.SelectChapterCommand.Execute(chapter);
        }
        Vm.DeleteChapterCommand.Execute(null);
    }

    // --- Scene context menu handlers ---

    private void OnRenameSceneClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var scene = GetContextMenuTag<SceneTreeItemViewModel>(sender);
        Vm.RenameSceneCommand.Execute(scene);
    }

    private void OnDeleteSceneClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var scene = GetContextMenuTag<SceneTreeItemViewModel>(sender);
        if (scene != null)
        {
            Vm.HandleSceneSelection(scene, ctrl: false, shift: false, openScene: false);
        }
        Vm.DeleteSceneCommand.Execute(null);
    }

    private void OnSetSceneDateClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var scene = GetContextMenuTag<SceneTreeItemViewModel>(sender);
        Vm.SetSceneDateCommand.Execute(scene);
    }

    private void OnToggleSceneFavoriteClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var scene = GetContextMenuTag<SceneTreeItemViewModel>(sender);
        Vm.ToggleSceneFavoriteCommand.Execute(scene);
    }

    private async void OnSetSceneColorClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var scene = GetContextMenuTag<SceneTreeItemViewModel>(sender);
        if (scene == null) return;
        var color = (sender as MenuItem)?.Tag as string ?? string.Empty;
        System.Diagnostics.Debug.WriteLine($"[LabelColor] scene='{scene.Scene.Title}' color='{color}'");
        await Vm.SetSceneLabelColorAsync(scene, color);
    }

    private void OnEditSmartListClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var item = GetContextMenuTag<SmartListItemViewModel>(sender);
        if (item != null)
            Vm.EditSmartListCommand.Execute(item);
    }

    private void OnDeleteSmartListClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var item = GetContextMenuTag<SmartListItemViewModel>(sender);
        if (item != null)
            Vm.DeleteSmartListCommand.Execute(item);
    }

    private async void OnTakeSnapshotClick(object? sender, RoutedEventArgs e)
    {
        var scene = GetContextMenuTag<SceneTreeItemViewModel>(sender);
        if (scene == null) return;
        await App.SnapshotService.TakeAsync(scene.ParentChapter, scene.Scene, string.Empty);
        Toast.Show?.Invoke(Localization.Loc.T("snapshots.taken"), ToastSeverity.Info);
    }

    private void OnOpenSnapshotsClick(object? sender, RoutedEventArgs e)
    {
        var scene = GetContextMenuTag<SceneTreeItemViewModel>(sender);
        if (scene == null) return;
        if (TopLevel.GetTopLevel(this) is MainWindow mw && mw.DataContext is MainWindowViewModel vm && vm.ShowSnapshotsDialog != null)
        {
            _ = vm.ShowSnapshotsDialog.Invoke(scene.ParentChapter, scene.Scene);
        }
    }

    // --- Act context menu handlers ---

    private void OnRenameActClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var act = GetContextMenuTag<ActHeaderViewModel>(sender);
        Vm.RenameActCommand.Execute(act);
    }

    private void OnDeleteActClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var act = GetContextMenuTag<ActHeaderViewModel>(sender);
        Vm.DeleteActCommand.Execute(act);
    }

    private void OnSetChapterActClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var chapter = GetContextMenuTag<ChapterTreeItemViewModel>(sender);
        Vm.SetChapterActCommand.Execute(chapter);
    }

    private void OnRemoveChapterFromActClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var chapter = GetContextMenuTag<ChapterTreeItemViewModel>(sender);
        Vm.RemoveChapterFromActCommand.Execute(chapter);
    }

    private void OnStatusPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is null || sender is not Control control) return;
        var chapter = control.FindAncestorOfType<Border>()?.Tag as ChapterTreeItemViewModel;
        if (chapter == null) return;
        e.Handled = true;
        Vm.CycleChapterStatusCommand.Execute(chapter);
    }

    // --- Drag and drop handlers ---

    private async void OnChapterDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Border border) return;
        if (await HasDragTextWithPrefixAsync(e, ChapterDragPrefix) || await HasDragTextWithPrefixAsync(e, SceneDragPrefix))
        {
            e.DragEffects = DragDropEffects.Move;
            border.Classes.Add("dropTarget");
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private async void OnSceneDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Border border) return;
        if (await HasDragTextWithPrefixAsync(e, SceneDragPrefix))
        {
            e.DragEffects = DragDropEffects.Move;
            border.Classes.Add("dropTarget");
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Border border)
            border.Classes.Remove("dropTarget");
    }

    private async void OnChapterDrop(object? sender, DragEventArgs e)
    {
        if (Vm is null || sender is not Border border || border.Tag is not ChapterTreeItemViewModel targetChapter) return;

        border.Classes.Remove("dropTarget");

        var chapters = await GetDragPayloadAsync(e, ChapterDragPrefix);
        if (chapters.Length > 0)
        {
            await Vm.MoveChaptersBeforeAsync(chapters, targetChapter.Chapter.Guid);
            return;
        }

        var scenes = await GetDragPayloadAsync(e, SceneDragPrefix);
        if (scenes.Length > 0)
            await Vm.MoveScenesToChapterAsync(scenes, targetChapter.Chapter.Guid);
    }

    private async void OnSceneDrop(object? sender, DragEventArgs e)
    {
        if (Vm is null || sender is not Border border || border.Tag is not SceneTreeItemViewModel targetScene) return;

        border.Classes.Remove("dropTarget");
        var scenes = await GetDragPayloadAsync(e, SceneDragPrefix);
        if (scenes.Length > 0)
            await Vm.MoveScenesBeforeAsync(scenes, targetScene.Scene.Id, targetScene.ParentChapter.Guid);
    }

    // --- Helpers ---

    private bool HasExceededDragThreshold(Point currentPoint)
    {
        return Math.Abs(currentPoint.X - _dragStartPoint.X) > 4 || Math.Abs(currentPoint.Y - _dragStartPoint.Y) > 4;
    }

    private static async Task<bool> HasDragTextWithPrefixAsync(DragEventArgs e, string prefix)
    {
        try
        {
            var text = e.DataTransfer.TryGetText();
            return text != null && text.StartsWith(prefix, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string[]> GetDragPayloadAsync(DragEventArgs e, string prefix)
    {
        try
        {
            var text = e.DataTransfer.TryGetText();
            if (text == null || !text.StartsWith(prefix, StringComparison.Ordinal))
                return [];

            return text[prefix.Length..]
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        catch
        {
            return [];
        }
    }

    private static T? GetContextMenuTag<T>(object? sender) where T : class
    {
        // Walk up parent chain; submenus nest MenuItem → MenuItem → ContextMenu.
        var current = sender as Control;
        while (current != null)
        {
            if (current is ContextMenu cm)
                return cm.Tag as T;
            current = current.Parent as Control;
        }
        return null;
    }

    // --- Extension context menu injection ---

    private void OnChapterContextMenuOpening(object? sender, CancelEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[ExtCtxMenu] Chapter context menu opening");
        if (sender is ContextMenu menu)
            InjectExtensionMenuItems(menu, "Chapter");
    }

    private void OnSceneContextMenuOpening(object? sender, CancelEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[ExtCtxMenu] Scene context menu opening");
        if (sender is ContextMenu menu)
            InjectExtensionMenuItems(menu, "Scene");
    }

    private void InjectExtensionMenuItems(ContextMenu menu, string context)
    {
        // Remove previously injected extension items
        for (var i = menu.Items.Count - 1; i >= 0; i--)
        {
            if (menu.Items[i] is MenuItem mi && mi.Tag is ContextMenuItem)
                menu.Items.RemoveAt(i);
            else if (menu.Items[i] is Separator sep && sep.Tag is string s && s == "ext")
                menu.Items.RemoveAt(i);
        }

        var extItems = App.ExtensionManager?.ContextMenuItems;
        System.Diagnostics.Debug.WriteLine($"[ExtCtxMenu] ExtensionManager null? {App.ExtensionManager is null}, items count: {extItems?.Count ?? -1}");
        if (extItems is null or { Count: 0 }) return;

        var matching = extItems
            .Where(ci => string.Equals(ci.Context, context, StringComparison.OrdinalIgnoreCase))
            .Where(ci => ci.IsVisible?.Invoke(menu.Tag) ?? true)
            .ToList();

        System.Diagnostics.Debug.WriteLine($"[ExtCtxMenu] Matching items for '{context}': {matching.Count}");

        if (matching.Count == 0) return;

        menu.Items.Add(new Separator { Tag = "ext" });

        foreach (var ci in matching)
        {
            var contextData = ToSdkContext(menu.Tag, context);
            System.Diagnostics.Debug.WriteLine($"[ExtCtxMenu] Adding item: '{ci.Label}', OnClick null? {ci.OnClick is null}, contextData: {contextData?.GetType().Name ?? "null"}");
            var item = ci; // capture for closure
            var mi = new MenuItem
            {
                Header = string.IsNullOrEmpty(ci.Icon) ? ci.Label : $"{ci.Icon}  {ci.Label}",
                Tag = ci,
                Command = new SimpleCommand(() =>
                {
                    System.Diagnostics.Debug.WriteLine($"[ExtCtxMenu] Command executed for '{item.Label}'");
                    Dispatcher.UIThread.Post(() =>
                    {
                        item.OnClick?.Invoke(contextData);
                        System.Diagnostics.Debug.WriteLine($"[ExtCtxMenu] OnClick invoked for '{item.Label}'");
                    });
                })
            };
            menu.Items.Add(mi);
        }
    }

    private static object? ToSdkContext(object? tag, string context)
    {
        if (tag is SceneTreeItemViewModel s)
        {
            var chTitle = s.Scene.ChapterGuid is { Length: > 0 } cg
                ? App.ProjectService.ActiveBook?.Chapters.FirstOrDefault(c => c.Guid == cg)?.Title ?? string.Empty
                : string.Empty;
            return new Novalist.Sdk.Services.SceneInfo
            {
                Id = s.Scene.Id,
                Title = s.Scene.Title,
                ChapterGuid = s.Scene.ChapterGuid,
                ChapterTitle = chTitle,
                WordCount = s.Scene.WordCount,
            };
        }
        if (tag is ChapterTreeItemViewModel c)
        {
            return new Novalist.Sdk.Services.ChapterInfo
            {
                Guid = c.Chapter.Guid,
                Title = c.Chapter.Title,
                Order = c.Chapter.Order,
                Date = c.Chapter.Date,
            };
        }
        return tag;
    }

    private sealed class SimpleCommand(Action execute) : ICommand
    {
#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
    }
}
