using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Core.Models;
using Novalist.Core.Services;

namespace Novalist.Desktop.ViewModels;

public partial class PlotGridViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private readonly IPlotlineService _plotlineService;

    [ObservableProperty]
    private ObservableCollection<PlotGridSceneColumn> _columns = [];

    [ObservableProperty]
    private ObservableCollection<PlotGridRow> _rows = [];

    [ObservableProperty]
    private bool _hasContent;

    public Func<string, string, string?, Task<string?>>? ShowInputDialog { get; set; }
    public Func<string, string, Task<bool>>? ShowConfirmDialog { get; set; }

    public PlotGridViewModel(IProjectService projectService, IPlotlineService plotlineService)
    {
        _projectService = projectService;
        _plotlineService = plotlineService;
    }

    public void Refresh()
    {
        var chapters = _projectService.GetChaptersOrdered();
        var columns = new List<PlotGridSceneColumn>();
        foreach (var chapter in chapters)
        {
            var scenes = _projectService.GetScenesForChapter(chapter.Guid);
            foreach (var scene in scenes)
            {
                columns.Add(new PlotGridSceneColumn
                {
                    Chapter = chapter,
                    Scene = scene,
                    ChapterTitle = chapter.Title,
                    SceneTitle = scene.Title
                });
            }
        }

        var plotlines = _plotlineService.GetPlotlines();
        var rows = new List<PlotGridRow>();
        foreach (var pl in plotlines)
        {
            var cells = new ObservableCollection<PlotGridCell>();
            foreach (var col in columns)
            {
                cells.Add(new PlotGridCell(pl, col.Chapter, col.Scene,
                    _plotlineService.IsSceneInPlotline(col.Scene, pl.Id),
                    OnCellToggleAsync));
            }
            rows.Add(new PlotGridRow(pl, cells, _plotlineService, OnRowDirty));
        }

        Columns = new ObservableCollection<PlotGridSceneColumn>(columns);
        Rows = new ObservableCollection<PlotGridRow>(rows);
        HasContent = rows.Count > 0;
    }

    private async Task OnCellToggleAsync(PlotGridCell cell)
    {
        await _plotlineService.ToggleSceneAsync(cell.Chapter.Guid, cell.Scene.Id, cell.Plotline.Id);
    }

    private void OnRowDirty()
    {
        Refresh();
    }

    [RelayCommand]
    private async Task AddPlotlineAsync()
    {
        if (ShowInputDialog == null) return;
        var name = await ShowInputDialog.Invoke(
            Localization.Loc.T("plotGrid.addTitle"),
            Localization.Loc.T("plotGrid.addPrompt"),
            null);
        if (string.IsNullOrWhiteSpace(name)) return;
        await _plotlineService.CreateAsync(name.Trim());
        Refresh();
    }

    [RelayCommand]
    private async Task RenamePlotlineAsync(PlotGridRow row)
    {
        if (ShowInputDialog == null || row == null) return;
        var name = await ShowInputDialog.Invoke(
            Localization.Loc.T("plotGrid.renameTitle"),
            Localization.Loc.T("plotGrid.renamePrompt"),
            row.Plotline.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        row.Plotline.Name = name.Trim();
        await _plotlineService.UpdateAsync(row.Plotline);
        Refresh();
    }

    [RelayCommand]
    private async Task DeletePlotlineAsync(PlotGridRow row)
    {
        if (row == null) return;
        if (ShowConfirmDialog != null)
        {
            var ok = await ShowConfirmDialog.Invoke(
                Localization.Loc.T("plotGrid.confirmDeleteTitle"),
                string.Format(Localization.Loc.T("plotGrid.confirmDelete"), row.Plotline.Name));
            if (!ok) return;
        }
        await _plotlineService.DeleteAsync(row.Plotline.Id);
        Refresh();
    }
}

public sealed class PlotGridSceneColumn
{
    public ChapterData Chapter { get; init; } = null!;
    public SceneData Scene { get; init; } = null!;
    public string ChapterTitle { get; init; } = string.Empty;
    public string SceneTitle { get; init; } = string.Empty;
}

public partial class PlotGridRow : ObservableObject
{
    public PlotlineData Plotline { get; }
    public ObservableCollection<PlotGridCell> Cells { get; }

    private readonly IPlotlineService _service;
    private readonly Action _onDirty;

    [ObservableProperty]
    private string _name;

    public PlotGridRow(PlotlineData plotline, ObservableCollection<PlotGridCell> cells, IPlotlineService service, Action onDirty)
    {
        Plotline = plotline;
        Cells = cells;
        _service = service;
        _onDirty = onDirty;
        _name = plotline.Name;
    }

    partial void OnNameChanged(string value)
    {
        if (Plotline.Name == value) return;
        Plotline.Name = value;
        _ = _service.UpdateAsync(Plotline);
    }
}

public partial class PlotGridCell : ObservableObject
{
    public PlotlineData Plotline { get; }
    public ChapterData Chapter { get; }
    public SceneData Scene { get; }

    private readonly Func<PlotGridCell, Task> _onToggle;

    [ObservableProperty]
    private bool _isAssigned;

    public string Tooltip => $"{Chapter.Title} → {Scene.Title}";

    public PlotGridCell(PlotlineData plotline, ChapterData chapter, SceneData scene, bool assigned, Func<PlotGridCell, Task> onToggle)
    {
        Plotline = plotline;
        Chapter = chapter;
        Scene = scene;
        _isAssigned = assigned;
        _onToggle = onToggle;
    }

    partial void OnIsAssignedChanged(bool value)
    {
        _ = _onToggle(this);
    }
}
