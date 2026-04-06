using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Novalist.Desktop.Dialogs;

public partial class AutoCompleteInputDialog : UserControl
{
    public string? Result { get; private set; }
    public TaskCompletionSource DialogClosed { get; } = new();

    public AutoCompleteInputDialog()
    {
        InitializeComponent();
    }

    public AutoCompleteInputDialog(string prompt, string defaultValue, IReadOnlyList<string> suggestions) : this()
    {
        PromptText.Text = prompt;
        InputBox.ItemsSource = suggestions;
        InputBox.Text = defaultValue;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            InputBox.Focus();
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
        if (!string.IsNullOrEmpty(text))
        {
            Result = text;
            DialogClosed.TrySetResult();
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        DialogClosed.TrySetResult();
    }
}
