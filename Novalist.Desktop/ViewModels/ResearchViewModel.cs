using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Core.Models;
using Novalist.Core.Services;

namespace Novalist.Desktop.ViewModels;

public partial class ResearchViewModel : ObservableObject
{
    private readonly IResearchService _service;

    [ObservableProperty]
    private ObservableCollection<ResearchItem> _items = [];

    [ObservableProperty]
    private ResearchItem? _selectedItem;

    [ObservableProperty]
    private bool _hasSelection;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    private ResearchItem[] _allItems = [];

    public Func<Task<string?>>? PickFileToImport { get; set; }
    public Func<string, string, Task<bool>>? ShowConfirmDialog { get; set; }

    public Array AvailableTypes { get; } = Enum.GetValues<ResearchItemType>();

    public ResearchViewModel(IResearchService service)
    {
        _service = service;
    }

    public void Refresh()
    {
        _allItems = _service.GetAll().ToArray();
        ApplyFilter();
        if (SelectedItem != null)
            SelectedItem = Items.FirstOrDefault(i => i.Id == SelectedItem.Id);
        HasSelection = SelectedItem != null;
    }

    partial void OnSelectedItemChanged(ResearchItem? value)
    {
        HasSelection = value != null;
        OnPropertyChanged(nameof(SelectedIsImage));
        OnPropertyChanged(nameof(SelectedIsFile));
        OnPropertyChanged(nameof(SelectedAbsolutePath));
        OnPropertyChanged(nameof(SelectedFileSize));
        OnPropertyChanged(nameof(SelectedModified));
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var q = (SearchQuery ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(q))
        {
            Items = new ObservableCollection<ResearchItem>(_allItems);
            return;
        }

        var filtered = _allItems.Where(i =>
            (i.Title?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (i.Content?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
            i.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)));
        Items = new ObservableCollection<ResearchItem>(filtered);
    }

    [RelayCommand]
    private async Task AddNoteAsync()
    {
        var item = new ResearchItem
        {
            Title = "New note",
            Type = ResearchItemType.Note
        };
        await _service.SaveAsync(item);
        Refresh();
        SelectedItem = Items.FirstOrDefault(i => i.Id == item.Id);
    }

    [RelayCommand]
    private async Task AddLinkAsync()
    {
        var item = new ResearchItem
        {
            Title = "New link",
            Type = ResearchItemType.Link,
            Content = "https://"
        };
        await _service.SaveAsync(item);
        Refresh();
        SelectedItem = Items.FirstOrDefault(i => i.Id == item.Id);
    }

    [RelayCommand]
    private async Task ImportFileAsync()
    {
        if (PickFileToImport == null) return;
        var path = await PickFileToImport.Invoke();
        if (string.IsNullOrEmpty(path)) return;

        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        var type = ext switch
        {
            ".pdf" => ResearchItemType.Pdf,
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" => ResearchItemType.Image,
            _ => ResearchItemType.File
        };

        var rel = await _service.ImportFileAsync(path);
        var item = new ResearchItem
        {
            Title = System.IO.Path.GetFileNameWithoutExtension(path),
            Type = type,
            Content = rel
        };
        await _service.SaveAsync(item);
        Refresh();
        SelectedItem = Items.FirstOrDefault(i => i.Id == item.Id);
    }

    [RelayCommand]
    private async Task SaveSelectedAsync()
    {
        if (SelectedItem == null) return;
        await _service.SaveAsync(SelectedItem);
        Refresh();
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedItem == null) return;
        if (ShowConfirmDialog != null)
        {
            var ok = await ShowConfirmDialog.Invoke(
                Localization.Loc.T("research.confirmDeleteTitle"),
                string.Format(Localization.Loc.T("research.confirmDelete"), SelectedItem.Title));
            if (!ok) return;
        }
        await _service.DeleteAsync(SelectedItem.Id);
        SelectedItem = null;
        Refresh();
    }

    [ObservableProperty]
    private string _newTagText = string.Empty;

    public Action<string>? RevealInExplorer { get; set; }

    public bool SelectedIsImage =>
        SelectedItem?.Type == ResearchItemType.Image && !string.IsNullOrWhiteSpace(SelectedItem.Content);

    public bool SelectedIsFile =>
        SelectedItem != null && SelectedItem.Type is ResearchItemType.File or ResearchItemType.Pdf or ResearchItemType.Image;

    public string? SelectedAbsolutePath =>
        SelectedItem != null && !string.IsNullOrWhiteSpace(SelectedItem.Content) && SelectedIsFile
            ? _service.GetAbsolutePath(SelectedItem.Content)
            : null;

    public string SelectedFileSize
    {
        get
        {
            var p = SelectedAbsolutePath;
            if (string.IsNullOrEmpty(p) || !System.IO.File.Exists(p)) return string.Empty;
            try
            {
                var bytes = new System.IO.FileInfo(p).Length;
                if (bytes < 1024) return $"{bytes} B";
                if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
                return $"{bytes / 1024.0 / 1024.0:F2} MB";
            }
            catch { return string.Empty; }
        }
    }

    public string SelectedModified
    {
        get
        {
            var p = SelectedAbsolutePath;
            if (string.IsNullOrEmpty(p) || !System.IO.File.Exists(p)) return string.Empty;
            try { return new System.IO.FileInfo(p).LastWriteTime.ToString("yyyy-MM-dd HH:mm"); }
            catch { return string.Empty; }
        }
    }

    [RelayCommand]
    private void RevealSelected()
    {
        var p = SelectedAbsolutePath;
        if (!string.IsNullOrEmpty(p)) RevealInExplorer?.Invoke(p);
    }

    [RelayCommand]
    private async Task AddTagAsync()
    {
        if (SelectedItem == null) return;
        var tag = (NewTagText ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(tag)) return;
        if (!SelectedItem.Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedItem.Tags.Add(tag);
            await _service.SaveAsync(SelectedItem);
            Refresh();
        }
        NewTagText = string.Empty;
    }

    [RelayCommand]
    private async Task RemoveTagAsync(string? tag)
    {
        if (SelectedItem == null || string.IsNullOrEmpty(tag)) return;
        SelectedItem.Tags.RemoveAll(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
        await _service.SaveAsync(SelectedItem);
        Refresh();
    }

    [RelayCommand]
    private void OpenExternal()
    {
        if (SelectedItem == null) return;
        try
        {
            string target;
            if (SelectedItem.Type == ResearchItemType.Link)
                target = SelectedItem.Content;
            else
                target = _service.GetAbsolutePath(SelectedItem.Content);
            if (string.IsNullOrEmpty(target)) return;
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch { /* swallow — user feedback via toast layer */ }
    }
}
