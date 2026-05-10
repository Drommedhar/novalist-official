using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Novalist.Core.Models;

namespace Novalist.Desktop.Dialogs;

public partial class SmartListEditorDialog : UserControl
{
    public SmartList? Result { get; private set; }
    public TaskCompletionSource DialogClosed { get; } = new();

    public SmartListEditorDialog()
    {
        InitializeComponent();

        // Status options: empty (any) + every ChapterStatus value.
        var items = new List<string> { string.Empty };
        foreach (var s in Enum.GetNames<ChapterStatus>()) items.Add(s);
        StatusBox.ItemsSource = items;
        StatusBox.SelectedIndex = 0;
    }

    public SmartListEditorDialog(SmartList? source) : this()
    {
        if (source != null)
        {
            NameBox.Text = source.Name;
            StatusBox.SelectedItem = source.ChapterStatus ?? string.Empty;
            PovBox.Text = source.PovContains ?? string.Empty;
            TagBox.Text = source.Tag ?? string.Empty;
            // Carry forward id so save updates instead of insert.
            _editingId = source.Id;
        }
    }

    private string? _editingId;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
            DialogClosed.TrySetResult();
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var name = (NameBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name))
        {
            NameBox.Focus();
            return;
        }

        var status = StatusBox.SelectedItem as string;
        Result = new SmartList
        {
            Id = _editingId ?? System.Guid.NewGuid().ToString(),
            Name = name,
            ChapterStatus = string.IsNullOrEmpty(status) ? null : status,
            PovContains = string.IsNullOrWhiteSpace(PovBox.Text) ? null : PovBox.Text!.Trim(),
            Tag = string.IsNullOrWhiteSpace(TagBox.Text) ? null : TagBox.Text!.Trim()
        };
        DialogClosed.TrySetResult();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        DialogClosed.TrySetResult();
    }
}
