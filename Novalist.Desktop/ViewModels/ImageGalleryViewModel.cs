using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Core.Services;
using Novalist.Desktop.Localization;

namespace Novalist.Desktop.ViewModels;

public partial class ImageGalleryViewModel : ObservableObject
{
    private readonly IEntityService _entityService;

    [ObservableProperty]
    private string _filterQuery = string.Empty;

    [ObservableProperty]
    private bool _isGridView = true;

    [ObservableProperty]
    private string _countText = string.Empty;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private string _emptyText = string.Empty;

    [ObservableProperty]
    private bool _isPreviewOpen;

    [ObservableProperty]
    private string _previewImagePath = string.Empty;

    [ObservableProperty]
    private string _previewImageName = string.Empty;

    public ObservableCollection<ImageGalleryItem> Images { get; } = [];

    private ImageGalleryItem[] _allImages = [];

    public Func<string, Task>? CopyToClipboard { get; set; }
    public Action<string>? RevealInExplorer { get; set; }
    public Action<string>? OpenExternally { get; set; }

    public ImageGalleryViewModel(IEntityService entityService)
    {
        _entityService = entityService;
    }

    partial void OnFilterQueryChanged(string value)
    {
        ApplyFilter();
    }

    public void Refresh() => _ = RefreshAsync();

    public async Task RefreshAsync()
    {
        // File scan off UI thread
        var items = await Task.Run(() =>
        {
            var paths = _entityService.GetProjectImages();
            return paths.Select(p =>
            {
                var fullPath = _entityService.GetImageFullPath(p);
                var fileName = Path.GetFileNameWithoutExtension(p);
                return new ImageGalleryItem
                {
                    RelativePath = p,
                    Name = fileName,
                    FullPath = fullPath,
                    CopyPathCommand = new RelayCommand(() =>
                    {
                        if (CopyToClipboard != null) _ = CopyToClipboard.Invoke(p);
                    }),
                    OpenInExplorerCommand = new RelayCommand(() => RevealInExplorer?.Invoke(fullPath)),
                    OpenExternallyCommand = new RelayCommand(() => OpenExternally?.Invoke(fullPath)),
                    CopyAsMarkdownCommand = new RelayCommand(() =>
                    {
                        if (CopyToClipboard != null) _ = CopyToClipboard.Invoke($"![{fileName}]({p.Replace('\\', '/')})");
                    }),
                };
            }).ToArray();
        }).ConfigureAwait(true);

        _allImages = items;
        ApplyFilter();

        // Lazy-decode thumbnails off UI thread, marshal back per-item
        _ = Task.Run(() =>
        {
            foreach (var item in items)
            {
                try
                {
                    if (!File.Exists(item.FullPath)) continue;
                    using var stream = File.OpenRead(item.FullPath);
                    var bmp = Bitmap.DecodeToWidth(stream, 200);
                    Dispatcher.UIThread.Post(() => item.Thumbnail = bmp);
                }
                catch { /* ignore decode failure */ }
            }
        });
    }

    [RelayCommand]
    private void SetGridView()
    {
        IsGridView = true;
    }

    [RelayCommand]
    private void SetListView()
    {
        IsGridView = false;
    }

    [RelayCommand]
    private void ClosePreview()
    {
        IsPreviewOpen = false;
    }

    private void ApplyFilter()
    {
        Images.Clear();

        var query = FilterQuery.Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? _allImages
            : _allImages.Where(img =>
                img.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                img.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase))
              .ToArray();

        foreach (var img in filtered)
            Images.Add(img);

        CountText = Loc.T("imageGallery.count", filtered.Length, _allImages.Length);
        IsEmpty = filtered.Length == 0;
        EmptyText = string.IsNullOrEmpty(query)
            ? Loc.T("imageGallery.noImages")
            : Loc.T("imageGallery.noResults");
    }
}

public partial class ImageGalleryItem : ObservableObject
{
    public string RelativePath { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public RelayCommand? CopyPathCommand { get; init; }
    public RelayCommand? OpenInExplorerCommand { get; init; }
    public RelayCommand? OpenExternallyCommand { get; init; }
    public RelayCommand? CopyAsMarkdownCommand { get; init; }

    [ObservableProperty]
    private Bitmap? _thumbnail;
}
