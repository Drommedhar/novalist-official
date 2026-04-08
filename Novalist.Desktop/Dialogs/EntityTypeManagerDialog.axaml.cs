using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Novalist.Desktop.ViewModels;

namespace Novalist.Desktop.Dialogs;

public partial class EntityTypeManagerDialog : UserControl
{
    public bool Saved { get; private set; }
    public TaskCompletionSource DialogClosed { get; } = new();

    public EntityTypeManagerDialog()
    {
        InitializeComponent();
    }

    public EntityTypeManagerDialog(EntityTypeManagerViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Saved = false;
            DialogClosed.TrySetResult();
        }
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        Saved = true;
        DialogClosed.TrySetResult();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Saved = false;
        DialogClosed.TrySetResult();
    }
}
