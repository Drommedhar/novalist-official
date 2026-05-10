using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Novalist.Core.Models;
using Novalist.Desktop.ViewModels;

namespace Novalist.Desktop.Dialogs;

public partial class FindReplaceDialog : UserControl
{
    public TaskCompletionSource DialogClosed { get; } = new();

    public FindReplaceDialog()
    {
        InitializeComponent();
    }

    public FindReplaceDialog(FindReplaceViewModel vm) : this()
    {
        DataContext = vm;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
            DialogClosed.TrySetResult();
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        DialogClosed.TrySetResult();
    }

    private void OnResultDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is FindReplaceViewModel vm
            && ResultsList.SelectedItem is FindMatch match)
        {
            vm.JumpToCommand.Execute(match);
        }
    }
}
