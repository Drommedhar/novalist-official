using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Localization;

namespace Novalist.Desktop.Dialogs;

public partial class SnapshotsDialog : UserControl
{
    public TaskCompletionSource DialogClosed { get; } = new();

    private readonly ISnapshotService? _snapshotService;
    private readonly ChapterData? _chapter;
    private readonly SceneData? _scene;
    private readonly Action? _onRestored;
    private readonly Func<SceneSnapshot, Task>? _onCompare;

    private List<SnapshotRow> _rows = new();

    public SnapshotsDialog()
    {
        InitializeComponent();
    }

    public SnapshotsDialog(
        ISnapshotService snapshotService,
        ChapterData chapter,
        SceneData scene,
        Action? onRestored,
        Func<SceneSnapshot, Task>? onCompare) : this()
    {
        _snapshotService = snapshotService;
        _chapter = chapter;
        _scene = scene;
        _onRestored = onRestored;
        _onCompare = onCompare;

        SceneTitleText.Text = $"{chapter.Title} → {scene.Title}";
        _ = ReloadAsync();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
            DialogClosed.TrySetResult();
    }

    private async Task ReloadAsync()
    {
        if (_snapshotService == null || _scene == null)
            return;

        var items = await _snapshotService.ListAsync(_scene);
        _rows = items.Select(s => new SnapshotRow(s)).ToList();
        SnapshotList.ItemsSource = _rows;
        EmptyText.IsVisible = _rows.Count == 0;
    }

    private async void OnTakeSnapshot(object? sender, RoutedEventArgs e)
    {
        if (_snapshotService == null || _scene == null || _chapter == null)
            return;

        var label = LabelBox.Text ?? string.Empty;
        await _snapshotService.TakeAsync(_chapter, _scene, label);
        LabelBox.Text = string.Empty;
        await ReloadAsync();
    }

    private void ClearAllConfirms()
    {
        foreach (var row in _rows)
        {
            row.IsConfirmingRestore = false;
            row.IsConfirmingDelete = false;
        }
    }

    private static SnapshotRow? RowFrom(object? sender)
        => (sender as Button)?.Tag as SnapshotRow;

    private void OnRestoreItem(object? sender, RoutedEventArgs e)
    {
        var row = RowFrom(sender);
        if (row == null) return;
        ClearAllConfirms();
        row.IsConfirmingRestore = true;
    }

    private void OnCancelRestore(object? sender, RoutedEventArgs e)
    {
        var row = RowFrom(sender);
        if (row != null) row.IsConfirmingRestore = false;
    }

    private async void OnConfirmRestore(object? sender, RoutedEventArgs e)
    {
        var row = RowFrom(sender);
        if (row == null || _snapshotService == null || _scene == null || _chapter == null)
            return;

        row.IsConfirmingRestore = false;
        await _snapshotService.RestoreAsync(_chapter, _scene, row.Snapshot.Id);
        _onRestored?.Invoke();
        await ReloadAsync();
    }

    private void OnDeleteItem(object? sender, RoutedEventArgs e)
    {
        var row = RowFrom(sender);
        if (row == null) return;
        ClearAllConfirms();
        row.IsConfirmingDelete = true;
    }

    private void OnCancelDelete(object? sender, RoutedEventArgs e)
    {
        var row = RowFrom(sender);
        if (row != null) row.IsConfirmingDelete = false;
    }

    private async void OnConfirmDelete(object? sender, RoutedEventArgs e)
    {
        var row = RowFrom(sender);
        if (row == null || _snapshotService == null || _scene == null)
            return;

        row.IsConfirmingDelete = false;
        await _snapshotService.DeleteAsync(_scene, row.Snapshot.Id);
        await ReloadAsync();
    }

    private async void OnCompareItem(object? sender, RoutedEventArgs e)
    {
        var row = RowFrom(sender);
        if (row == null || _onCompare == null)
            return;

        await _onCompare.Invoke(row.Snapshot);
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        DialogClosed.TrySetResult();
    }

    public sealed class SnapshotRow : INotifyPropertyChanged
    {
        public SceneSnapshot Snapshot { get; }
        public string DisplayLabel { get; }
        public string DisplaySubtitle { get; }

        private bool _isConfirmingRestore;
        public bool IsConfirmingRestore
        {
            get => _isConfirmingRestore;
            set { if (_isConfirmingRestore != value) { _isConfirmingRestore = value; Raise(); } }
        }

        private bool _isConfirmingDelete;
        public bool IsConfirmingDelete
        {
            get => _isConfirmingDelete;
            set { if (_isConfirmingDelete != value) { _isConfirmingDelete = value; Raise(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Raise([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name!));

        public SnapshotRow(SceneSnapshot snapshot)
        {
            Snapshot = snapshot;
            DisplayLabel = string.IsNullOrWhiteSpace(snapshot.Label)
                ? Loc.T("snapshots.unlabeled")
                : snapshot.Label;
            DisplaySubtitle = $"{snapshot.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} · {snapshot.WordCount} {Loc.T("snapshots.words")}";
        }
    }
}
