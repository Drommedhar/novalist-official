using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Novalist.Desktop.Localization;

namespace Novalist.Desktop.Dialogs;

public partial class ChapterDialog : UserControl
{
    public ChapterDialogResult? Result { get; private set; }
    public TaskCompletionSource DialogClosed { get; } = new();

    public ChapterDialog()
    {
        InitializeComponent();
    }

    public ChapterDialog(string title = "", string initialName = "", string initialDate = "") : this()
    {
        TitleBox.Text = initialName;
        if (DateTime.TryParse(initialDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            DatePicker.SelectedDate = dt;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            TitleBox.Focus();
            TitleBox.SelectAll();
        }, Avalonia.Threading.DispatcherPriority.Input);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
            DialogClosed.TrySetResult();
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var title = TitleBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(title)) return;

        var date = DatePicker.SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
        Result = new ChapterDialogResult(title, date);
        DialogClosed.TrySetResult();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        DialogClosed.TrySetResult();
    }
}

public sealed record ChapterDialogResult(string Title, string Date);