using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Novalist.Core.Models;

namespace Novalist.Desktop.Dialogs;

public partial class StoryDateRangeDialog : UserControl
{
    public StoryDateRange? Result { get; private set; }
    public bool Cleared { get; private set; }
    public TaskCompletionSource DialogClosed { get; } = new();

    public StoryDateRangeDialog()
    {
        InitializeComponent();
    }

    public StoryDateRangeDialog(string prompt, StoryDateRange? initial) : this()
    {
        PromptText.Text = prompt;
        if (initial != null)
        {
            if (TryParseDate(initial.Start, out var sd)) StartDate.SelectedDate = sd;
            if (TryParseDate(initial.End, out var ed)) EndDate.SelectedDate = ed;
            if (TryParseTime(initial.StartTime, out var st)) StartTime.SelectedTime = st;
            if (TryParseTime(initial.EndTime, out var et)) EndTime.SelectedTime = et;
            NoteBox.Text = initial.Note;
        }
    }

    private static bool TryParseDate(string raw, out DateTime dt)
    {
        dt = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt)
            || DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out dt);
    }

    private static bool TryParseTime(string raw, out TimeSpan ts)
    {
        ts = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        return TimeSpan.TryParseExact(raw.Trim(), new[] { @"hh\:mm", @"h\:mm", @"hh\:mm\:ss" }, CultureInfo.InvariantCulture, out ts)
            || TimeSpan.TryParse(raw.Trim(), out ts);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => StartDate.Focus(),
            Avalonia.Threading.DispatcherPriority.Input);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) DialogClosed.TrySetResult();
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        Result = new StoryDateRange
        {
            Start = StartDate.SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            End = EndDate.SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            StartTime = StartTime.SelectedTime is { } sst ? $"{sst.Hours:D2}:{sst.Minutes:D2}" : string.Empty,
            EndTime = EndTime.SelectedTime is { } est ? $"{est.Hours:D2}:{est.Minutes:D2}" : string.Empty,
            Note = NoteBox.Text?.Trim() ?? string.Empty,
        };
        DialogClosed.TrySetResult();
    }

    private void OnClear(object? sender, RoutedEventArgs e)
    {
        Cleared = true;
        Result = null;
        DialogClosed.TrySetResult();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Cleared = false;
        DialogClosed.TrySetResult();
    }
}
