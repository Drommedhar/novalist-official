using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Dialogs;
using Novalist.Desktop.Localization;

namespace Novalist.Desktop.ViewModels;

public partial class ExplorerViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private readonly HashSet<string> _selectedChapterGuids = [];
    private readonly HashSet<string> _selectedSceneIds = [];
    private string? _lastSelectedChapterGuid;
    private string? _lastSelectedSceneId;

    [ObservableProperty]
    private string _activeTab = "Chapters";

    [ObservableProperty]
    private ChapterTreeItemViewModel? _selectedChapter;

    [ObservableProperty]
    private SceneTreeItemViewModel? _selectedScene;

    /// <summary>
    /// Called by the view to show an input dialog. Set by MainWindow.
    /// </summary>
    public Func<string, string, string, Task<string?>>? ShowInputDialog { get; set; }
    public Func<string, string, string, Task<string?>>? ShowOptionalInputDialog { get; set; }
    public Func<string, string, string, Task<string?>>? ShowDatePickerDialog { get; set; }
    public Func<string, string, IReadOnlyList<string>, Task<string?>>? ShowAutoCompleteInputDialog { get; set; }
    public Func<Task<ChapterDialogResult?>>? ShowChapterDialog { get; set; }
    public Func<ChapterTreeItemViewModel?, Task<SceneDialogResult?>>? ShowSceneDialog { get; set; }

    /// <summary>
    /// Fired when a scene is selected and should be opened in the editor.
    /// </summary>
    public event Action<ChapterData, SceneData>? SceneOpenRequested;

    /// <summary>
    /// Fired when chapter or scene changes affect project-level statistics.
    /// </summary>
    public event Action? ProjectChanged;

    public ObservableCollection<object> ExplorerItems { get; } = new();

    public ObservableCollection<SmartListItemViewModel> SmartLists { get; } = new();

    private ISmartListService? _smartListService;

    public Func<SmartList?, Task<SmartList?>>? ShowSmartListEditor { get; set; }
    public Func<string, string, Task<bool>>? ShowConfirmDialog { get; set; }

    public ExplorerViewModel(IProjectService projectService)
    {
        _projectService = projectService;
    }

    public void AttachSmartListService(ISmartListService service)
    {
        _smartListService = service;
        RefreshSmartLists();
    }

    public void RefreshSmartLists()
    {
        SmartLists.Clear();
        if (_smartListService == null) return;
        foreach (var list in _smartListService.GetAll())
            SmartLists.Add(new SmartListItemViewModel(list, _smartListService, OnSmartListSceneOpenRequested));
    }

    private void OnSmartListSceneOpenRequested(ChapterData chapter, SceneData scene)
        => SceneOpenRequested?.Invoke(chapter, scene);

    [RelayCommand]
    private async Task CreateSmartListAsync()
    {
        if (ShowSmartListEditor == null || _smartListService == null) return;
        var draft = await ShowSmartListEditor.Invoke(null);
        if (draft == null) return;
        await _smartListService.SaveAsync(draft);
        RefreshSmartLists();
    }

    [RelayCommand]
    private async Task EditSmartListAsync(SmartListItemViewModel item)
    {
        if (ShowSmartListEditor == null || _smartListService == null || item == null) return;
        var updated = await ShowSmartListEditor.Invoke(item.Source);
        if (updated == null) return;
        await _smartListService.SaveAsync(updated);
        RefreshSmartLists();
    }

    [RelayCommand]
    private async Task DeleteSmartListAsync(SmartListItemViewModel item)
    {
        if (_smartListService == null || item == null) return;
        if (ShowConfirmDialog != null)
        {
            var ok = await ShowConfirmDialog.Invoke(
                Loc.T("smartList.confirmDeleteTitle"),
                string.Format(Loc.T("smartList.confirmDelete"), item.Source.Name));
            if (!ok) return;
        }
        await _smartListService.DeleteAsync(item.Source.Id);
        RefreshSmartLists();
    }

    /// <summary>
    /// Returns all ChapterTreeItemViewModels from the explorer items (skipping act headers).
    /// </summary>
    private IEnumerable<ChapterTreeItemViewModel> AllChapters
        => ExplorerItems.OfType<ChapterTreeItemViewModel>();

    public void Refresh()
    {
        Refresh(_selectedChapterGuids, _selectedSceneIds);
    }

    public void Refresh(IEnumerable<string>? selectedChapterGuids, IEnumerable<string>? selectedSceneIds)
    {
        ExplorerItems.Clear();

        var selectedChapterSet = selectedChapterGuids != null
            ? new HashSet<string>(selectedChapterGuids)
            : [];
        var selectedSceneSet = selectedSceneIds != null
            ? new HashSet<string>(selectedSceneIds)
            : [];

        SelectedChapter = null;
        SelectedScene = null;

        var chapters = _projectService.GetChaptersOrdered();

        // Group by act: chapters with an act are grouped under act headers,
        // chapters without an act appear at the top level.
        string? currentAct = null;
        foreach (var chapter in chapters)
        {
            var act = string.IsNullOrWhiteSpace(chapter.Act) ? null : chapter.Act;

            // Insert act header when the act changes
            if (act != currentAct)
            {
                if (act != null)
                    ExplorerItems.Add(new ActHeaderViewModel(act));
                currentAct = act;
            }

            var chapterVm = new ChapterTreeItemViewModel(chapter);
            chapterVm.IsSelected = selectedChapterSet.Contains(chapter.Guid);
            var scenes = _projectService.GetScenesForChapter(chapter.Guid);
            foreach (var scene in scenes)
            {
                if (ShowFavoritesOnly && !scene.IsFavorite && !chapter.IsFavorite)
                    continue;
                var sceneVm = new SceneTreeItemViewModel(scene, chapter)
                {
                    IsSelected = selectedSceneSet.Contains(scene.Id)
                };
                chapterVm.Scenes.Add(sceneVm);
                if (sceneVm.IsSelected)
                {
                    chapterVm.IsExpanded = true;
                    SelectedScene ??= sceneVm;
                }
            }

            if (ShowFavoritesOnly && !chapter.IsFavorite && chapterVm.Scenes.Count == 0)
                continue;

            if (chapterVm.IsSelected)
            {
                SelectedChapter ??= chapterVm;
                chapterVm.IsExpanded = true;
            }

            ExplorerItems.Add(chapterVm);
        }
    }

    [RelayCommand]
    private void SetTab(string tab)
    {
        ActiveTab = tab;
    }

    [RelayCommand]
    private void SelectChapter(ChapterTreeItemViewModel chapter)
    {
        // Deselect previous scene
        if (SelectedScene != null) { SelectedScene.IsSelected = false; SelectedScene = null; }

        if (SelectedChapter == chapter)
        {
            // Re-clicking same chapter toggles expand
            chapter.IsExpanded = !chapter.IsExpanded;
            return;
        }

        // Deselect previous chapter
        if (SelectedChapter != null) SelectedChapter.IsSelected = false;

        SelectedChapter = chapter;
        chapter.IsSelected = true;
        chapter.IsExpanded = true;
    }

    [RelayCommand]
    private void SelectScene(SceneTreeItemViewModel scene)
    {
        // Deselect previous
        if (SelectedChapter != null) { SelectedChapter.IsSelected = false; }
        if (SelectedScene != null) SelectedScene.IsSelected = false;

        SelectedScene = scene;
        scene.IsSelected = true;

        // Also track the parent chapter
        var parentChapter = AllChapters.FirstOrDefault(c => c.Chapter.Guid == scene.Scene.ChapterGuid);
        SelectedChapter = parentChapter;
        if (parentChapter != null)
        {
            parentChapter.IsSelected = true;
        }

        // Notify that this scene should be opened in the editor
        SceneOpenRequested?.Invoke(
            parentChapter?.Chapter ?? new ChapterData { Guid = scene.Scene.ChapterGuid },
            scene.Scene);
    }

    [RelayCommand]
    private async Task CreateChapter()
    {
        var result = await (ShowChapterDialog?.Invoke() ?? Task.FromResult<ChapterDialogResult?>(null));
        if (result == null || string.IsNullOrWhiteSpace(result.Title)) return;

        await _projectService.CreateChapterAsync(result.Title, result.Date);
        ClearSelections();
        Refresh();
        ProjectChanged?.Invoke();

        var chapterVm = AllChapters.LastOrDefault();
        if (chapterVm != null)
            HandleChapterSelection(chapterVm, ctrl: false, shift: false);
    }

    [RelayCommand]
    private async Task CreateScene(ChapterTreeItemViewModel? preferredChapter = null)
    {
        if (!AllChapters.Any()) return;

        preferredChapter ??= SelectedChapter ?? AllChapters.LastOrDefault();
        var result = await (ShowSceneDialog?.Invoke(preferredChapter) ?? Task.FromResult<SceneDialogResult?>(null));
        if (result == null || string.IsNullOrWhiteSpace(result.Title)) return;

        await _projectService.CreateSceneAsync(result.ChapterGuid, result.Title, result.Date);
        ClearSelections();
        Refresh();
        ProjectChanged?.Invoke();

        var chapterVm = AllChapters.FirstOrDefault(chapter => chapter.Chapter.Guid == result.ChapterGuid);
        var sceneVm = chapterVm?.Scenes.LastOrDefault();
        if (sceneVm != null)
            HandleSceneSelection(sceneVm, ctrl: false, shift: false, openScene: true);
    }

    [RelayCommand]
    private async Task RenameChapter(ChapterTreeItemViewModel? chapterVm)
    {
        if (chapterVm == null) chapterVm = SelectedChapter;
        if (chapterVm == null) return;

        var newName = await RequestInput(Loc.T("explorer.renameChapter"), Loc.T("explorer.renameChapterPrompt"), chapterVm.Chapter.Title);
        if (string.IsNullOrWhiteSpace(newName) || newName == chapterVm.Chapter.Title) return;

        await _projectService.RenameChapterAsync(chapterVm.Chapter.Guid, newName);
        chapterVm.RefreshDisplay();
        ProjectChanged?.Invoke();
    }

    [RelayCommand]
    private async Task RenameScene(SceneTreeItemViewModel? sceneVm)
    {
        if (sceneVm == null) sceneVm = SelectedScene;
        if (sceneVm == null) return;

        var newName = await RequestInput(Loc.T("explorer.renameScene"), Loc.T("explorer.renameScenePrompt"), sceneVm.Scene.Title);
        if (string.IsNullOrWhiteSpace(newName) || newName == sceneVm.Scene.Title) return;

        await _projectService.RenameSceneAsync(sceneVm.Scene.ChapterGuid, sceneVm.Scene.Id, newName);
        sceneVm.RefreshDisplay();
        ProjectChanged?.Invoke();
    }

    [RelayCommand]
    private async Task DeleteChapter()
    {
        var chaptersToDelete = GetSelectedChapters();
        if (chaptersToDelete.Count == 0 && SelectedChapter != null)
            chaptersToDelete.Add(SelectedChapter);

        foreach (var chapter in chaptersToDelete)
            await _projectService.DeleteChapterAsync(chapter.Chapter.Guid);

        ClearSelections();
        Refresh();
        ProjectChanged?.Invoke();
    }

    [RelayCommand]
    private async Task DeleteScene()
    {
        var scenesToDelete = GetSelectedScenes();
        if (scenesToDelete.Count == 0 && SelectedScene != null)
            scenesToDelete.Add(SelectedScene);

        foreach (var scene in scenesToDelete)
            await _projectService.DeleteSceneAsync(scene.Scene.ChapterGuid, scene.Scene.Id);

        ClearSelections();
        Refresh();
        ProjectChanged?.Invoke();
    }

    public Func<string, StoryDateRange?, Task<DateRangeDialogResult>>? ShowDateRangeDialog { get; set; }

    [RelayCommand]
    private async Task SetChapterDate(ChapterTreeItemViewModel? chapterVm)
    {
        chapterVm ??= SelectedChapter;
        if (chapterVm == null) return;
        if (ShowDateRangeDialog == null) return;

        var initial = chapterVm.Chapter.DateRange ?? (string.IsNullOrWhiteSpace(chapterVm.Chapter.Date)
            ? null
            : new StoryDateRange { Start = chapterVm.Chapter.Date });

        var r = await ShowDateRangeDialog.Invoke(Loc.T("explorer.chapterDate"), initial);
        if (r.Cancelled) return;

        var range = r.Range;
        await _projectService.SetChapterDateRangeAsync(chapterVm.Chapter.Guid, range);
        chapterVm.Chapter.DateRange = range?.HasValue == true ? range : null;
        if (range?.HasValue == true && !string.IsNullOrWhiteSpace(range.Start))
            chapterVm.Chapter.Date = range.Start;
        else
            chapterVm.Chapter.Date = string.Empty;
        chapterVm.RefreshDisplay();
        ProjectChanged?.Invoke();
    }

    [RelayCommand]
    private async Task CycleChapterStatus(ChapterTreeItemViewModel? chapterVm)
    {
        if (chapterVm == null) return;
        var values = Enum.GetValues<ChapterStatus>();
        var currentIndex = Array.IndexOf(values, chapterVm.Chapter.Status);
        chapterVm.Chapter.Status = values[(currentIndex + 1) % values.Length];
        await _projectService.SaveScenesAsync();
        chapterVm.RefreshDisplay();
        ProjectChanged?.Invoke();
    }

    [RelayCommand]
    private async Task SetSceneDate(SceneTreeItemViewModel? sceneVm)
    {
        sceneVm ??= SelectedScene;
        if (sceneVm == null) return;
        if (ShowDateRangeDialog == null) return;

        var initial = sceneVm.Scene.DateRange ?? (string.IsNullOrWhiteSpace(sceneVm.Scene.Date)
            ? null
            : new StoryDateRange { Start = sceneVm.Scene.Date });

        var r = await ShowDateRangeDialog.Invoke(Loc.T("explorer.sceneDate"), initial);
        if (r.Cancelled) return;

        var range = r.Range;
        await _projectService.SetSceneDateRangeAsync(sceneVm.Scene.ChapterGuid, sceneVm.Scene.Id, range);
        sceneVm.Scene.DateRange = range?.HasValue == true ? range : null;
        if (range?.HasValue == true && !string.IsNullOrWhiteSpace(range.Start))
            sceneVm.Scene.Date = range.Start;
        else
            sceneVm.Scene.Date = string.Empty;
        sceneVm.RefreshDisplay();
        ProjectChanged?.Invoke();
    }

    public sealed record DateRangeDialogResult(bool Cancelled, StoryDateRange? Range);

    [RelayCommand]
    private async Task ToggleChapterFavorite(ChapterTreeItemViewModel? chapterVm)
    {
        chapterVm ??= SelectedChapter;
        if (chapterVm == null) return;
        var newValue = !chapterVm.Chapter.IsFavorite;
        await _projectService.SetChapterFavoriteAsync(chapterVm.Chapter.Guid, newValue);
        chapterVm.Chapter.IsFavorite = newValue;
        chapterVm.RefreshDisplay();
    }

    [RelayCommand]
    private async Task ToggleSceneFavorite(SceneTreeItemViewModel? sceneVm)
    {
        sceneVm ??= SelectedScene;
        if (sceneVm == null) return;
        var newValue = !sceneVm.Scene.IsFavorite;
        await _projectService.SetSceneFavoriteAsync(sceneVm.Scene.ChapterGuid, sceneVm.Scene.Id, newValue);
        sceneVm.Scene.IsFavorite = newValue;
        sceneVm.RefreshDisplay();
    }

    public async Task SetSceneLabelColorAsync(SceneTreeItemViewModel? scene, string color)
    {
        if (scene == null) return;
        await _projectService.SetSceneLabelColorAsync(scene.Scene.ChapterGuid, scene.Scene.Id, color);
        scene.Scene.LabelColor = string.IsNullOrWhiteSpace(color) ? null : color.Trim();
        scene.RefreshDisplay();
        System.Diagnostics.Debug.WriteLine($"[LabelColor] saved sceneId={scene.Scene.Id} → {scene.Scene.LabelColor}");
    }

    [RelayCommand]
    private void ToggleFavoritesOnlyFilter() => ShowFavoritesOnly = !ShowFavoritesOnly;

    [ObservableProperty]
    private bool _showFavoritesOnly;

    partial void OnShowFavoritesOnlyChanged(bool value) => Refresh();

    // ── Act commands ────────────────────────────────────────────────

    [RelayCommand]
    private async Task CreateAct()
    {
        var name = await RequestInput(Loc.T("explorer.createAct"), Loc.T("explorer.actNamePrompt"), "");
        if (string.IsNullOrWhiteSpace(name)) return;

        // Check for duplicate act names
        var existing = _projectService.GetChaptersOrdered()
            .Select(c => c.Act)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (existing.Contains(name, StringComparer.OrdinalIgnoreCase)) return;

        // Create a placeholder — the act exists when at least one chapter references it.
        // We'll assign the last selected chapter (if any) or just create an empty act header.
        // For now, just refresh to show the header once a chapter is assigned.
        // If no chapters exist with this act yet, we need to store it.
        // The simplest approach: assign the selected chapter to this act.
        if (SelectedChapter != null)
        {
            SelectedChapter.Chapter.Act = name;
            await _projectService.SaveProjectAsync();
            Refresh();
            ProjectChanged?.Invoke();
        }
    }

    [RelayCommand]
    private async Task RenameAct(ActHeaderViewModel? actVm)
    {
        if (actVm == null) return;

        var newName = await RequestInput(Loc.T("explorer.renameAct"), Loc.T("explorer.actNamePrompt"), actVm.ActName);
        if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, actVm.ActName, StringComparison.OrdinalIgnoreCase)) return;

        // Rename across all chapters
        foreach (var chapter in _projectService.GetChaptersOrdered())
        {
            if (string.Equals(chapter.Act, actVm.ActName, StringComparison.OrdinalIgnoreCase))
                chapter.Act = newName;
        }

        await _projectService.SaveProjectAsync();
        Refresh();
        ProjectChanged?.Invoke();
    }

    [RelayCommand]
    private async Task DeleteAct(ActHeaderViewModel? actVm)
    {
        if (actVm == null) return;

        // Remove act assignment from all chapters in this act
        foreach (var chapter in _projectService.GetChaptersOrdered())
        {
            if (string.Equals(chapter.Act, actVm.ActName, StringComparison.OrdinalIgnoreCase))
                chapter.Act = string.Empty;
        }

        await _projectService.SaveProjectAsync();
        Refresh();
        ProjectChanged?.Invoke();
    }

    [RelayCommand]
    private async Task SetChapterAct(ChapterTreeItemViewModel? chapterVm)
    {
        chapterVm ??= SelectedChapter;
        if (chapterVm == null) return;

        var actName = await RequestAutoCompleteInput(
            Loc.T("explorer.actNamePrompt"), chapterVm.Chapter.Act, GetExistingActNames());
        if (actName == null) return;

        chapterVm.Chapter.Act = actName;
        await _projectService.SaveProjectAsync();
        Refresh();
        ProjectChanged?.Invoke();
    }

    [RelayCommand]
    private async Task RemoveChapterFromAct(ChapterTreeItemViewModel? chapterVm)
    {
        chapterVm ??= SelectedChapter;
        if (chapterVm == null || string.IsNullOrWhiteSpace(chapterVm.Chapter.Act)) return;

        chapterVm.Chapter.Act = string.Empty;
        await _projectService.SaveProjectAsync();
        Refresh();
        ProjectChanged?.Invoke();
    }

    public void HandleChapterSelection(ChapterTreeItemViewModel chapter, bool ctrl, bool shift)
    {
        var chapterOrder = AllChapters.ToList();

        if (shift && _lastSelectedChapterGuid != null)
        {
            ClearSceneSelections();
            var start = chapterOrder.FindIndex(item => item.Chapter.Guid == _lastSelectedChapterGuid);
            var end = chapterOrder.FindIndex(item => item.Chapter.Guid == chapter.Chapter.Guid);
            if (start == -1 || end == -1)
            {
                SelectSingleChapter(chapter);
                return;
            }

            ClearChapterSelections();
            for (var index = Math.Min(start, end); index <= Math.Max(start, end); index++)
                SelectChapterInternal(chapterOrder[index]);

            SelectedChapter = chapter;
            chapter.IsExpanded = true;
            return;
        }

        if (ctrl)
        {
            ClearSceneSelections();
            if (_selectedChapterGuids.Contains(chapter.Chapter.Guid))
            {
                DeselectChapterInternal(chapter);
                if (SelectedChapter == chapter)
                    SelectedChapter = GetSelectedChapters().LastOrDefault();
            }
            else
            {
                SelectChapterInternal(chapter);
                SelectedChapter = chapter;
                _lastSelectedChapterGuid = chapter.Chapter.Guid;
                chapter.IsExpanded = true;
            }
            return;
        }

        if (SelectedChapter == chapter && _selectedChapterGuids.Count == 1 && _selectedSceneIds.Count == 0)
        {
            chapter.IsExpanded = !chapter.IsExpanded;
            return;
        }

        SelectSingleChapter(chapter);
    }

    public void HandleSceneSelection(SceneTreeItemViewModel scene, bool ctrl, bool shift, bool openScene)
    {
        var visualScenes = AllChapters.SelectMany(chapter => chapter.Scenes).ToList();

        if (shift && _lastSelectedSceneId != null)
        {
            ClearChapterSelections();
            var start = visualScenes.FindIndex(item => item.Scene.Id == _lastSelectedSceneId);
            var end = visualScenes.FindIndex(item => item.Scene.Id == scene.Scene.Id);
            if (start == -1 || end == -1)
            {
                SelectSingleScene(scene, openScene);
                return;
            }

            ClearSceneSelections();
            for (var index = Math.Min(start, end); index <= Math.Max(start, end); index++)
                SelectSceneInternal(visualScenes[index]);

            SelectedScene = scene;
            SelectedChapter = AllChapters.FirstOrDefault(chapter => chapter.Chapter.Guid == scene.Scene.ChapterGuid);
            if (SelectedChapter != null)
                SelectedChapter.IsExpanded = true;
            return;
        }

        if (ctrl)
        {
            ClearChapterSelections();
            if (_selectedSceneIds.Contains(scene.Scene.Id))
            {
                DeselectSceneInternal(scene);
                if (SelectedScene == scene)
                    SelectedScene = GetSelectedScenes().LastOrDefault();
            }
            else
            {
                SelectSceneInternal(scene);
                SelectedScene = scene;
                SelectedChapter = AllChapters.FirstOrDefault(chapter => chapter.Chapter.Guid == scene.Scene.ChapterGuid);
                if (SelectedChapter != null)
                    SelectedChapter.IsExpanded = true;
                _lastSelectedSceneId = scene.Scene.Id;
            }
            return;
        }

        SelectSingleScene(scene, openScene);
    }

    public IReadOnlyList<ChapterTreeItemViewModel> PrepareChapterDrag(ChapterTreeItemViewModel sourceChapter)
    {
        if (!_selectedChapterGuids.Contains(sourceChapter.Chapter.Guid))
            SelectSingleChapter(sourceChapter);

        return GetSelectedChapters();
    }

    public IReadOnlyList<SceneTreeItemViewModel> PrepareSceneDrag(SceneTreeItemViewModel sourceScene)
    {
        if (!_selectedSceneIds.Contains(sourceScene.Scene.Id))
            SelectSingleScene(sourceScene, openScene: false);

        return GetSelectedScenes();
    }

    public async Task MoveChaptersBeforeAsync(IReadOnlyList<string> chapterGuids, string targetChapterGuid)
    {
        var ordered = AllChapters.Select(chapter => chapter.Chapter.Guid).Where(guid => !chapterGuids.Contains(guid)).ToList();
        var targetIndex = ordered.IndexOf(targetChapterGuid);
        if (targetIndex < 0) return;

        await _projectService.MoveChaptersAsync(chapterGuids, targetIndex);
        Refresh(chapterGuids, []);
        ProjectChanged?.Invoke();
    }

    public async Task MoveScenesBeforeAsync(IReadOnlyList<string> sceneIds, string targetSceneId, string targetChapterGuid)
    {
        var ordered = _projectService.GetScenesForChapter(targetChapterGuid)
            .Where(scene => !sceneIds.Contains(scene.Id))
            .Select(scene => scene.Id)
            .ToList();
        var targetIndex = ordered.IndexOf(targetSceneId);
        if (targetIndex < 0) return;

        await _projectService.MoveScenesAsync(sceneIds, targetChapterGuid, targetIndex);
        Refresh([], sceneIds);
        ProjectChanged?.Invoke();
    }

    public async Task MoveScenesToChapterAsync(IReadOnlyList<string> sceneIds, string targetChapterGuid)
    {
        var ordered = _projectService.GetScenesForChapter(targetChapterGuid)
            .Where(scene => !sceneIds.Contains(scene.Id))
            .ToList();

        await _projectService.MoveScenesAsync(sceneIds, targetChapterGuid, ordered.Count);
        Refresh([], sceneIds);
        ProjectChanged?.Invoke();
    }

    private async Task<string?> RequestInput(string title, string prompt, string defaultValue)
    {
        if (ShowInputDialog != null)
            return await ShowInputDialog(title, prompt, defaultValue);
        return null;
    }

    private async Task<string?> RequestOptionalInput(string title, string prompt, string defaultValue)
    {
        if (ShowOptionalInputDialog != null)
            return await ShowOptionalInputDialog(title, prompt, defaultValue);
        return null;
    }

    private async Task<string?> RequestDateInput(string title, string prompt, string currentDate)
    {
        if (ShowDatePickerDialog != null)
            return await ShowDatePickerDialog(title, prompt, currentDate);
        return null;
    }

    private async Task<string?> RequestAutoCompleteInput(string prompt, string defaultValue, IReadOnlyList<string> suggestions)
    {
        if (ShowAutoCompleteInputDialog != null)
            return await ShowAutoCompleteInputDialog(prompt, defaultValue, suggestions);
        return null;
    }

    private IReadOnlyList<string> GetExistingActNames()
    {
        return _projectService.GetChaptersOrdered()
            .Select(c => c.Act)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void SelectSingleChapter(ChapterTreeItemViewModel chapter)
    {
        ClearSceneSelections();
        ClearChapterSelections();
        SelectChapterInternal(chapter);
        SelectedChapter = chapter;
        SelectedScene = null;
        _lastSelectedChapterGuid = chapter.Chapter.Guid;
        chapter.IsExpanded = true;
    }

    /// <summary>
    /// Navigate to the next (direction = +1) or previous (direction = -1) scene.
    /// </summary>
    public void NavigateScene(int direction)
    {
        var allScenes = AllChapters.SelectMany(chapter => chapter.Scenes).ToList();
        if (allScenes.Count == 0) return;

        var currentIndex = SelectedScene != null
            ? allScenes.FindIndex(s => s.Scene.Id == SelectedScene.Scene.Id)
            : -1;

        var nextIndex = currentIndex + direction;
        if (nextIndex < 0 || nextIndex >= allScenes.Count) return;

        SelectSingleScene(allScenes[nextIndex], openScene: true);
    }

    private void SelectSingleScene(SceneTreeItemViewModel scene, bool openScene)
    {
        ClearChapterSelections();
        ClearSceneSelections();
        SelectSceneInternal(scene);
        SelectedScene = scene;
        _lastSelectedSceneId = scene.Scene.Id;

        var parentChapter = AllChapters.FirstOrDefault(chapter => chapter.Chapter.Guid == scene.Scene.ChapterGuid);
        SelectedChapter = parentChapter;
        if (parentChapter != null)
            parentChapter.IsExpanded = true;

        if (openScene)
            SceneOpenRequested?.Invoke(parentChapter?.Chapter ?? new ChapterData { Guid = scene.Scene.ChapterGuid }, scene.Scene);
    }

    private void SelectChapterInternal(ChapterTreeItemViewModel chapter)
    {
        _selectedChapterGuids.Add(chapter.Chapter.Guid);
        chapter.IsSelected = true;
    }

    private void DeselectChapterInternal(ChapterTreeItemViewModel chapter)
    {
        _selectedChapterGuids.Remove(chapter.Chapter.Guid);
        chapter.IsSelected = false;
    }

    private void SelectSceneInternal(SceneTreeItemViewModel scene)
    {
        _selectedSceneIds.Add(scene.Scene.Id);
        scene.IsSelected = true;
    }

    private void DeselectSceneInternal(SceneTreeItemViewModel scene)
    {
        _selectedSceneIds.Remove(scene.Scene.Id);
        scene.IsSelected = false;
    }

    private void ClearChapterSelections()
    {
        foreach (var chapter in AllChapters)
            chapter.IsSelected = false;
        _selectedChapterGuids.Clear();
        SelectedChapter = null;
    }

    private void ClearSceneSelections()
    {
        foreach (var scene in AllChapters.SelectMany(chapter => chapter.Scenes))
            scene.IsSelected = false;
        _selectedSceneIds.Clear();
        SelectedScene = null;
    }

    private void ClearSelections()
    {
        ClearChapterSelections();
        ClearSceneSelections();
    }

    private List<ChapterTreeItemViewModel> GetSelectedChapters()
        => AllChapters.Where(chapter => _selectedChapterGuids.Contains(chapter.Chapter.Guid)).ToList();

    private List<SceneTreeItemViewModel> GetSelectedScenes()
        => AllChapters.SelectMany(chapter => chapter.Scenes)
            .Where(scene => _selectedSceneIds.Contains(scene.Scene.Id))
            .ToList();
}

public partial class ChapterTreeItemViewModel : ObservableObject
{
    public ChapterData Chapter { get; }

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private string _gitStatusLabel = string.Empty;

    [ObservableProperty]
    private bool _hasGitChanges;

    public bool HasDate => !string.IsNullOrWhiteSpace(Chapter.Date);

    public bool HasAct => !string.IsNullOrWhiteSpace(Chapter.Act);

    public string DateDisplay => Chapter.Date;

    public bool IsFavorite => Chapter.IsFavorite;

    public string StatusDisplay => Chapter.Status switch
    {
        ChapterStatus.Outline => "○",
        ChapterStatus.FirstDraft => "◔",
        ChapterStatus.Revised => "◑",
        ChapterStatus.Edited => "◕",
        ChapterStatus.Final => "●",
        _ => "○"
    };

    public ObservableCollection<SceneTreeItemViewModel> Scenes { get; } = new();

    public ChapterTreeItemViewModel(ChapterData chapter)
    {
        Chapter = chapter;
        _displayName = $"{chapter.Order}. {chapter.Title}";
    }

    public void RefreshDisplay()
    {
        DisplayName = $"{Chapter.Order}. {Chapter.Title}";
        OnPropertyChanged(nameof(StatusDisplay));
        OnPropertyChanged(nameof(HasDate));
        OnPropertyChanged(nameof(DateDisplay));
        OnPropertyChanged(nameof(IsFavorite));
    }

    public void RefreshGitStatus()
    {
        var anyChanged = Scenes.Any(s => s.HasGitChanges);
        HasGitChanges = anyChanged;
        GitStatusLabel = anyChanged ? "●" : string.Empty;
    }
}

public partial class SceneTreeItemViewModel : ObservableObject
{
    public SceneData Scene { get; }
    public ChapterData ParentChapter { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private string _gitStatusLabel = string.Empty;

    [ObservableProperty]
    private bool _hasGitChanges;

    public bool HasDate => !string.IsNullOrWhiteSpace(Scene.Date);

    public string DateDisplay => Scene.Date;

    public bool IsFavorite => Scene.IsFavorite;
    public string LabelColor => Scene.LabelColor ?? string.Empty;
    public bool HasLabelColor => !string.IsNullOrWhiteSpace(Scene.LabelColor);

    public SceneTreeItemViewModel(SceneData scene, ChapterData parentChapter)
    {
        Scene = scene;
        ParentChapter = parentChapter;
        _displayName = scene.Title;
    }

    public void RefreshDisplay()
    {
        DisplayName = Scene.Title;
        OnPropertyChanged(nameof(HasDate));
        OnPropertyChanged(nameof(DateDisplay));
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(LabelColor));
        OnPropertyChanged(nameof(HasLabelColor));
    }
}

public partial class ActHeaderViewModel : ObservableObject
{
    [ObservableProperty]
    private string _displayName;

    public string ActName { get; }

    public ActHeaderViewModel(string actName)
    {
        ActName = actName;
        _displayName = actName.ToUpperInvariant();
    }

    public void RefreshDisplay()
    {
        DisplayName = ActName.ToUpperInvariant();
    }
}
