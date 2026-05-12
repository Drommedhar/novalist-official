using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Novalist.Desktop.Localization;

namespace Novalist.Desktop.Dialogs;

public partial class AddImageSourceDialog : UserControl
{
    public AddImageSourceChoice? Result { get; private set; }
    public TaskCompletionSource DialogClosed { get; } = new();

    public AddImageSourceDialog()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => LibraryButton.Focus(),
            Avalonia.Threading.DispatcherPriority.Input);
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

    private void OnSelectFromLibrary(object? sender, RoutedEventArgs e)
    {
        Result = AddImageSourceChoice.Library;
        DialogClosed.TrySetResult();
    }

    private void OnImportFile(object? sender, RoutedEventArgs e)
    {
        Result = AddImageSourceChoice.Import;
        DialogClosed.TrySetResult();
    }

    private void OnFromClipboard(object? sender, RoutedEventArgs e)
    {
        Result = AddImageSourceChoice.Clipboard;
        DialogClosed.TrySetResult();
    }

    private void OnFromUrl(object? sender, RoutedEventArgs e)
    {
        Result = AddImageSourceChoice.Url;
        DialogClosed.TrySetResult();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        DialogClosed.TrySetResult();
    }
}