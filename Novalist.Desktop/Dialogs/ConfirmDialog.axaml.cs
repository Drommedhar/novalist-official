using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Novalist.Desktop.Dialogs;

public partial class ConfirmDialog : UserControl
{
    public bool Confirmed { get; private set; }
    public TaskCompletionSource DialogClosed { get; } = new();

    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public ConfirmDialog(string title, string message) : this()
    {
        MessageText.Text = message;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Confirmed = false;
            DialogClosed.TrySetResult();
        }
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogClosed.TrySetResult();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogClosed.TrySetResult();
    }
}
