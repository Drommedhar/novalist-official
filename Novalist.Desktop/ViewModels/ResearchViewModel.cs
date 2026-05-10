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

    public Func<Task<string?>>? PickFileToImport { get; set; }
    public Func<string, string, Task<bool>>? ShowConfirmDialog { get; set; }

    public Array AvailableTypes { get; } = Enum.GetValues<ResearchItemType>();

    public ResearchViewModel(IResearchService service)
    {
        _service = service;
    }

    public void Refresh()
    {
        Items = new ObservableCollection<ResearchItem>(_service.GetAll());
        if (SelectedItem != null)
            SelectedItem = Items.FirstOrDefault(i => i.Id == SelectedItem.Id);
        HasSelection = SelectedItem != null;
    }

    partial void OnSelectedItemChanged(ResearchItem? value)
    {
        HasSelection = value != null;
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
