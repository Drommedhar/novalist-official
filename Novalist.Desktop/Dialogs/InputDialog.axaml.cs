using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Novalist.Desktop.Dialogs;

public partial class InputDialog : UserControl
{
    private readonly bool _allowEmpty;

    public string? Result { get; private set; }
    public TaskCompletionSource DialogClosed { get; } = new();

    public InputDialog()
    {
        InitializeComponent();
    }

    public InputDialog(string title, string prompt, string defaultValue = "", bool allowEmpty = false) : this()
    {
        _allowEmpty = allowEmpty;
        PromptText.Text = prompt;
        InputBox.Text = defaultValue;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        }, Avalonia.Threading.DispatcherPriority.Input);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Result = null;
            DialogClosed.TrySetResult();
        }
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var text = InputBox.Text?.Trim();
        if (_allowEmpty || !string.IsNullOrEmpty(text))
        {
            Result = text ?? string.Empty;
            DialogClosed.TrySetResult();
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        DialogClosed.TrySetResult();
    }
}
