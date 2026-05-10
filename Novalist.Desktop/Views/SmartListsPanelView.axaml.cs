using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Novalist.Desktop.ViewModels;

namespace Novalist.Desktop.Views;

public partial class SmartListsPanelView : UserControl
{
    public SmartListsPanelView()
    {
        InitializeComponent();
    }

    private ExplorerViewModel? Vm => DataContext as ExplorerViewModel;

    private static SmartListItemViewModel? FindItem(object? sender)
    {
        if (sender is MenuItem mi)
        {
            // Walk up to ContextMenu and read its Tag.
            Avalonia.StyledElement? p = mi.Parent;
            while (p is not null && p is not Avalonia.Controls.ContextMenu) p = p.Parent;
            if (p is Avalonia.Controls.ContextMenu cm && cm.Tag is SmartListItemViewModel item)
                return item;
        }
        if (sender is Control c && c.Tag is SmartListItemViewModel direct) return direct;
        return null;
    }

    private void OnSmartListHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control c && c.Tag is SmartListItemViewModel item)
            item.IsExpanded = !item.IsExpanded;
    }

    private void OnEditSmartListClick(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var item = FindItem(sender);
        if (item != null)
            Vm.EditSmartListCommand.Execute(item);
    }

    private void OnRefreshSmartListClick(object? sender, RoutedEventArgs e)
    {
        var item = FindItem(sender);
        item?.RefreshCommand.Execute(null);
    }

    private void OnDeleteSmartListClick(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var item = FindItem(sender);
        if (item != null)
            Vm.DeleteSmartListCommand.Execute(item);
    }
}
