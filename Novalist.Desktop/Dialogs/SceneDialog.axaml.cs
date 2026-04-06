using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Novalist.Desktop.Localization;

namespace Novalist.Desktop.Dialogs;

public partial class SceneDialog : UserControl
{
    private readonly List<SceneChapterOption> _chapters;

    public SceneDialogResult? Result { get; private set; }
    public TaskCompletionSource DialogClosed { get; } = new();

    public SceneDialog()
    {
        InitializeComponent();
        _chapters = [];
    }

    public SceneDialog(
        IReadOnlyList<SceneChapterOption> chapters,
        string title = "",
        string initialName = "",
        string initialDate = "",
        string? initialChapterGuid = null) : this()
    {
        _chapters = chapters.ToList();
        ChapterBox.ItemsSource = _chapters;
        ChapterBox.SelectedItem = _chapters.FirstOrDefault(chapter => chapter.Guid == initialChapterGuid) ?? _chapters.FirstOrDefault();
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
        var chapter = ChapterBox.SelectedItem as SceneChapterOption;
        if (string.IsNullOrWhiteSpace(title) || chapter == null) return;

        var date = DatePicker.SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
        Result = new SceneDialogResult(title, chapter.Guid, date);
        DialogClosed.TrySetResult();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        DialogClosed.TrySetResult();
    }
}

public sealed record SceneDialogResult(string Title, string ChapterGuid, string Date);

public sealed record SceneChapterOption(string Guid, string Title)
{
    public override string ToString() => Title;
}