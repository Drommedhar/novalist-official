using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Novalist.Core.Models;
using Novalist.Desktop.ViewModels;

namespace Novalist.Desktop.Views;

public partial class EntityPanelView : UserControl
{
    private const string CharacterDragPrefix = "novalist-characters:";
    private const string LocationDragPrefix = "novalist-locations:";

    private CharacterListItemViewModel? _pendingCharacterDrag;
    private LocationTreeItemViewModel? _pendingLocationDrag;
    private Point _dragStartPoint;
    private PointerPressedEventArgs? _lastPointerPressed;
    private Vector _savedLocationTreeScroll;

    public EntityPanelView()
    {
        InitializeComponent();
        CreateButton.Click += OnCreateClick;
        AddHandler(Button.ClickEvent, OnItemClick);

        LocationTreeView.PropertyChanged += OnLocationTreeViewPropertyChanged;
        LocationTreeView.ContainerPrepared += OnLocationTreeContainerPrepared;
        LocationTreeView.ContainerClearing += OnLocationTreeContainerClearing;
    }

    private void OnLocationTreeContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is TreeViewItem tvi && tvi.DataContext is LocationTreeItemViewModel vm)
        {
            tvi.IsExpanded = vm.IsExpanded;
            tvi.PropertyChanged += OnTreeViewItemPropertyChanged;
        }
    }

    private void OnLocationTreeContainerClearing(object? sender, ContainerClearingEventArgs e)
    {
        if (e.Container is TreeViewItem tvi)
            tvi.PropertyChanged -= OnTreeViewItemPropertyChanged;
    }

    private static void OnTreeViewItemPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TreeViewItem.IsExpandedProperty
            && sender is TreeViewItem { DataContext: LocationTreeItemViewModel vm })
        {
            vm.IsExpanded = (bool)(e.NewValue ?? true);
        }
    }

    private void OnLocationTreeViewPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ItemsControl.ItemsSourceProperty) return;

        // Save scroll offset before items change (old value still present)
        var scroll = LocationTreeView.FindDescendantOfType<ScrollViewer>();
        if (scroll != null && e.OldValue != null)
            _savedLocationTreeScroll = scroll.Offset;

        // Restore after layout
        if (scroll != null && e.NewValue != null)
        {
            var offset = _savedLocationTreeScroll;
            scroll.PropertyChanged += RestoreOnce;
            void RestoreOnce(object? s, AvaloniaPropertyChangedEventArgs ev)
            {
                if (ev.Property != ScrollViewer.ExtentProperty) return;
                scroll.PropertyChanged -= RestoreOnce;
                scroll.Offset = offset;
            };
        }
    }

    private void OnCreateClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EntityPanelViewModel vm) return;
        _ = vm.ActiveEntityType switch
        {
            EntityType.Character => vm.CreateCharacterCommand.ExecuteAsync(null),
            EntityType.Location => vm.CreateLocationCommand.ExecuteAsync(null),
            EntityType.Item => vm.CreateItemCommand.ExecuteAsync(null),
            EntityType.Lore => vm.CreateLoreCommand.ExecuteAsync(null),
            EntityType.Custom => vm.CreateCustomEntityCommand.ExecuteAsync(null),
            _ => Task.CompletedTask
        };
    }

    private void OnItemClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button btn) return;
        // Walk up to find the Button with a Tag (entity data)
        var current = btn;
        while (current != null && current.Tag == null)
        {
            current = current.FindAncestorOfType<Button>();
        }
        if (current?.Tag == null) return;

        if (current.Tag is CharacterListItemViewModel)
            return;

        if (DataContext is EntityPanelViewModel vm)
        {
            vm.OpenEntityCommand.Execute(current.Tag);
        }
    }

    private void OnCharacterPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not EntityPanelViewModel vm || sender is not Border border) return;

        var item = border.Tag as CharacterListItemViewModel;
        if (item == null) return;

        if (e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
        {
            var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            vm.HandleCharacterSelection(item, ctrl, shift, openEntity: !ctrl && !shift);
            _pendingCharacterDrag = item;
            _dragStartPoint = e.GetPosition(this);
            _lastPointerPressed = e;
        }
    }

    private async void OnCharacterPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not EntityPanelViewModel vm || _pendingCharacterDrag == null || _lastPointerPressed == null || sender is not Border border) return;
        if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed) return;
        if (!HasExceededDragThreshold(e.GetPosition(this))) return;

        var dragging = vm.PrepareCharacterDrag(_pendingCharacterDrag);
        _pendingCharacterDrag = null;
        if (dragging.Count == 0) return;

        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(CharacterDragPrefix + string.Join("|", dragging)));
        await DragDrop.DoDragDropAsync(_lastPointerPressed, data, DragDropEffects.Move);
    }

    private async void OnCharacterGroupDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Border border) return;

        if (await HasDragTextWithPrefixAsync(e, CharacterDragPrefix))
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

    private async void OnCharacterGroupDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not EntityPanelViewModel vm || sender is not Border border || border.Tag is not CharacterGroupSectionViewModel group) return;

        border.Classes.Remove("dropTarget");

        var characters = await GetDragPayloadAsync(e, CharacterDragPrefix);
        if (characters.Length > 0)
            await vm.MoveCharactersToGroupAsync(characters, group.GroupValue);
    }

    private bool HasExceededDragThreshold(Point currentPoint)
    {
        return Math.Abs(currentPoint.X - _dragStartPoint.X) > 4 || Math.Abs(currentPoint.Y - _dragStartPoint.Y) > 4;
    }

    private static Task<bool> HasDragTextWithPrefixAsync(DragEventArgs e, string prefix)
    {
        try
        {
            var text = e.DataTransfer.TryGetText();
            return Task.FromResult(text != null && text.StartsWith(prefix, StringComparison.Ordinal));
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private static Task<string[]> GetDragPayloadAsync(DragEventArgs e, string prefix)
    {
        try
        {
            var text = e.DataTransfer.TryGetText();
            if (text == null || !text.StartsWith(prefix, StringComparison.Ordinal))
                return Task.FromResult(Array.Empty<string>());

            return Task.FromResult(text[prefix.Length..]
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        catch
        {
            return Task.FromResult(Array.Empty<string>());
        }
    }

    private void OnDeleteCharacterClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EntityPanelViewModel vm && sender is MenuItem { Tag: CharacterData character })
            _ = vm.DeleteCharacterCommand.ExecuteAsync(character);
    }

    private void OnDeleteLocationClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EntityPanelViewModel vm && sender is MenuItem { Tag: LocationData location })
            _ = vm.DeleteLocationCommand.ExecuteAsync(location);
    }

    private void OnRemoveLocationParentClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EntityPanelViewModel vm && sender is MenuItem { Tag: LocationTreeItemViewModel node })
            _ = vm.SetLocationParentAsync(node.Location, string.Empty);
    }

    private void OnLocationTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not EntityPanelViewModel vm) return;
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is LocationTreeItemViewModel node)
            vm.OpenEntityCommand.Execute(node.Location);
    }

    private void OnLocationPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not EntityPanelViewModel || sender is not Border border) return;
        if (border.Tag is not LocationTreeItemViewModel node) return;

        if (e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
        {
            _pendingLocationDrag = node;
            _dragStartPoint = e.GetPosition(this);
            _lastPointerPressed = e;
        }
    }

    private async void OnLocationPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not EntityPanelViewModel || _pendingLocationDrag == null || _lastPointerPressed == null || sender is not Border border) return;
        if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed) return;
        if (!HasExceededDragThreshold(e.GetPosition(this))) return;

        var dragging = _pendingLocationDrag;
        _pendingLocationDrag = null;

        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(LocationDragPrefix + dragging.Location.Id));
        await DragDrop.DoDragDropAsync(_lastPointerPressed, data, DragDropEffects.Move);
    }

    private async void OnLocationDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Border border) return;
        if (await HasDragTextWithPrefixAsync(e, LocationDragPrefix))
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

    private async void OnLocationDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not EntityPanelViewModel vm || sender is not Border border) return;
        border.Classes.Remove("dropTarget");

        if (border.Tag is not LocationTreeItemViewModel targetNode) return;

        var payload = await GetDragPayloadAsync(e, LocationDragPrefix);
        if (payload.Length == 0) return;

        var draggedId = payload[0];
        if (draggedId == targetNode.Location.Id) return;

        var dragged = vm.Locations.FirstOrDefault(l => l.Id == draggedId);
        if (dragged == null) return;

        await vm.SetLocationParentAsync(dragged, targetNode.Location.Name);
    }

    private void OnDeleteItemClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EntityPanelViewModel vm && sender is MenuItem { Tag: ItemData item })
            _ = vm.DeleteItemCommand.ExecuteAsync(item);
    }

    private void OnDeleteLoreClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EntityPanelViewModel vm && sender is MenuItem { Tag: LoreData lore })
            _ = vm.DeleteLoreCommand.ExecuteAsync(lore);
    }

    private void OnMoveCharacterToWBClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EntityPanelViewModel vm && sender is MenuItem { Tag: CharacterData character })
            _ = vm.ToggleWorldBibleCharacterCommand.ExecuteAsync(character);
    }

    private void OnMoveLocationToWBClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EntityPanelViewModel vm && sender is MenuItem { Tag: LocationData location })
            _ = vm.ToggleWorldBibleLocationCommand.ExecuteAsync(location);
    }

    private void OnMoveItemToWBClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EntityPanelViewModel vm && sender is MenuItem { Tag: ItemData item })
            _ = vm.ToggleWorldBibleItemCommand.ExecuteAsync(item);
    }

    private void OnMoveLoreToWBClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EntityPanelViewModel vm && sender is MenuItem { Tag: LoreData lore })
            _ = vm.ToggleWorldBibleLoreCommand.ExecuteAsync(lore);
    }

    private void OnDeleteCustomEntityClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EntityPanelViewModel vm && sender is MenuItem { Tag: CustomEntityData entity })
            _ = vm.DeleteCustomEntityCommand.ExecuteAsync(entity);
    }

    private void OnMoveCustomEntityToWBClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EntityPanelViewModel vm && sender is MenuItem { Tag: CustomEntityData entity })
            _ = vm.ToggleWorldBibleCustomEntityCommand.ExecuteAsync(entity);
    }

    private void OnEditEntityTypeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EntityPanelViewModel vm && sender is MenuItem { Tag: CustomEntityTypeDefinition typeDef })
            _ = vm.EditEntityTypeCommand.ExecuteAsync(typeDef);
    }

    private void OnDeleteEntityTypeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EntityPanelViewModel vm && sender is MenuItem { Tag: CustomEntityTypeDefinition typeDef })
            _ = vm.DeleteEntityTypeCommand.ExecuteAsync(typeDef);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is EntityPanelViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            UpdateCustomEntityListBinding(vm);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EntityPanelViewModel.ActiveCustomTypeKey) && sender is EntityPanelViewModel vm)
            UpdateCustomEntityListBinding(vm);
    }

    private void UpdateCustomEntityListBinding(EntityPanelViewModel vm)
    {
        if (vm.ActiveCustomTypeKey != null)
            CustomEntityList.ItemsSource = vm.GetCustomEntities(vm.ActiveCustomTypeKey);
    }
}
