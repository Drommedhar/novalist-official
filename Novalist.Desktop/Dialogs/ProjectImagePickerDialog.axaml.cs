using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Novalist.Desktop.Localization;

namespace Novalist.Desktop.Dialogs;

public partial class ProjectImagePickerDialog : UserControl
{
    private readonly List<ProjectImageOption> _allImages = [];
    private readonly ObservableCollection<ProjectImageOption> _visibleImages = [];

    public string? Result { get; private set; }
    public TaskCompletionSource DialogClosed { get; } = new();

    public ProjectImagePickerDialog()
    {
        InitializeComponent();
        ImageList.ItemsSource = _visibleImages;
    }

    public ProjectImagePickerDialog(IEnumerable<string> imagePaths, string? currentPath) : this()
    {
        _allImages = imagePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => new ProjectImageOption(
                Path.GetFileNameWithoutExtension(path),
                path,
                string.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        SearchBox.TextChanged += OnSearchChanged;
        RenderImages(SearchBox.Text);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => SearchBox.Focus(),
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

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        RenderImages(SearchBox.Text);
    }

    private void OnImageClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ProjectImageOption option })
        {
            Result = option.Path;
            DialogClosed.TrySetResult();
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        DialogClosed.TrySetResult();
    }

    private void RenderImages(string? query)
    {
        _visibleImages.Clear();

        var normalizedQuery = query?.Trim() ?? string.Empty;
        foreach (var image in _allImages)
        {
            if (!string.IsNullOrWhiteSpace(normalizedQuery)
                && !image.Name.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                && !image.Path.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _visibleImages.Add(image);
        }

        CountText.Text = _visibleImages.Count == 1
            ? Loc.T("dialog.imageCountSingle")
            : Loc.T("dialog.imageCount", _visibleImages.Count);
        ImageList.IsVisible = _visibleImages.Count > 0;
        EmptyText.IsVisible = _visibleImages.Count == 0;
    }
}