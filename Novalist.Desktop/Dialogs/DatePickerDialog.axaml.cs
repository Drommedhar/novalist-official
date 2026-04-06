using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Novalist.Desktop.Dialogs;

public partial class DatePickerDialog : UserControl
{
    public string? Result { get; private set; }
    public TaskCompletionSource DialogClosed { get; } = new();

    public DatePickerDialog()
    {
        InitializeComponent();
    }

    public DatePickerDialog(string title, string prompt, string currentDate = "") : this()
    {
        PromptText.Text = prompt;
        if (DateTime.TryParse(currentDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            DatePicker.SelectedDate = dt;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => DatePicker.Focus(),
            Avalonia.Threading.DispatcherPriority.Input);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
            DialogClosed.TrySetResult();
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        Result = DatePicker.SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
        DialogClosed.TrySetResult();
    }

    private void OnClear(object? sender, RoutedEventArgs e)
    {
        Result = string.Empty;
        DialogClosed.TrySetResult();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        DialogClosed.TrySetResult();
    }
}
