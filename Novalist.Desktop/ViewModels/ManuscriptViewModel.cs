using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using Novalist.Desktop.Localization;

namespace Novalist.Desktop.ViewModels;

public partial class ManuscriptViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private readonly Dictionary<string, ManuscriptSceneItem> _sceneIndex = new();
    private System.Threading.CancellationTokenSource? _autoSaveCts;

    [ObservableProperty]
    private ObservableCollection<ManuscriptSection> _sections = [];

    [ObservableProperty]
    private int _totalWords;

    [ObservableProperty]
    private int _totalScenes;

    [ObservableProperty]
    private bool _hasContent;

    [ObservableProperty]
    private string _filterStatus = "All";

    public string TotalWordsDisplay => TextStatistics.FormatCompactCount(TotalWords);
    public string ReadingTimeDisplay => LocFormatters.ReadingTime(TextStatistics.EstimateReadingTime(TotalWords));

    public event Action<ChapterData, SceneData>? SceneOpenRequested;
    public event Action<ChapterData, SceneData, string>? SceneFocusChanged;
    public event Action? ContentRefreshRequested;
    public event Action? SceneSaved;

    public ManuscriptViewModel(IProjectService projectService)
    {
        _projectService = projectService;
    }

    partial void OnFilterStatusChanged(string value)
    {
        Refresh();
        ContentRefreshRequested?.Invoke();
    }

    [RelayCommand]
    private void SetFilter(string status)
    {
        FilterStatus = status;
    }

    public void NotifyContentRefresh()
    {
        ContentRefreshRequested?.Invoke();
    }

    public void RequestOpenScene(string chapterGuid, string sceneId)
    {
        var item = FindScene(sceneId);
        if (item != null)
            SceneOpenRequested?.Invoke(item.Chapter, item.Scene);
    }

    public void OnSceneFocused(string chapterGuid, string sceneId)
    {
        var item = FindScene(sceneId);
        if (item == null) return;
        var plainText = System.Text.RegularExpressions.Regex.Replace(item.HtmlContent, "<[^>]+>", string.Empty);
        SceneFocusChanged?.Invoke(item.Chapter, item.Scene, plainText);
    }

    public async Task CycleStatusByGuidAsync(string chapterGuid)
    {
        var section = Sections.FirstOrDefault(s => s.Chapter.Guid == chapterGuid);
        if (section == null) return;
        var values = Enum.GetValues<ChapterStatus>();
        var currentIndex = Array.IndexOf(values, section.Chapter.Status);
        section.Chapter.Status = values[(currentIndex + 1) % values.Length];
        await _projectService.SaveScenesAsync();
        Refresh();
        ContentRefreshRequested?.Invoke();
    }

    // ── WebView Content Changed ─────────────────────────────────────

    public void OnWebViewSceneChanged(string sceneId, string chapterGuid, string html, int wordCount)
    {
        var item = FindScene(sceneId);
        if (item == null) return;

        item.HtmlContent = html;
        item.Scene.WordCount = wordCount;
        item.IsDirty = true;

        // Recalculate totals
        TotalWords = _sceneIndex.Values.Sum(s => s.Scene.WordCount);
        OnPropertyChanged(nameof(TotalWordsDisplay));
        OnPropertyChanged(nameof(ReadingTimeDisplay));

        ScheduleAutoSave(item);
    }

    private void ScheduleAutoSave(ManuscriptSceneItem item)
    {
        _autoSaveCts?.Cancel();
        _autoSaveCts?.Dispose();
        _autoSaveCts = new System.Threading.CancellationTokenSource();
        var token = _autoSaveCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(800, token);
                if (!token.IsCancellationRequested)
                    await SaveSceneContentAsync(item);
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    public async Task SaveAllDirtyAsync()
    {
        foreach (var item in _sceneIndex.Values)
            if (item.IsDirty)
                await SaveSceneContentAsync(item);
    }

    private async Task SaveSceneContentAsync(ManuscriptSceneItem item)
    {
        if (!item.IsDirty) return;
        await _projectService.WriteSceneContentAsync(item.Chapter, item.Scene, item.HtmlContent);
        await _projectService.SaveScenesAsync();
        item.IsDirty = false;
        SceneSaved?.Invoke();
    }

    // ── Serialize for WebView ───────────────────────────────────────

    public string GetManuscriptJson()
    {
        var sections = Sections.Select(s => new
        {
            chapterGuid = s.Chapter.Guid,
            chapterTitle = s.ChapterTitle,
            status = s.Status,
            act = s.Act ?? string.Empty,
            scenes = s.Scenes.Select(sc => new
            {
                sceneId = sc.Scene.Id,
                title = sc.Title,
                html = sc.HtmlContent,
                wordCount = sc.Scene.WordCount
            })
        });
        return JsonSerializer.Serialize(sections);
    }

    // ── Refresh ─────────────────────────────────────────────────────

    public void Refresh()
    {
        if (!_projectService.IsProjectLoaded)
        {
            Sections = [];
            _sceneIndex.Clear();
            HasContent = false;
            return;
        }

        var chapters = _projectService.GetChaptersOrdered();
        var sections = new ObservableCollection<ManuscriptSection>();
        _sceneIndex.Clear();
        var totalWords = 0;
        var totalScenes = 0;

        foreach (var chapter in chapters)
        {
            if (FilterStatus != "All" &&
                !string.Equals(chapter.Status.ToString(), FilterStatus, StringComparison.OrdinalIgnoreCase))
                continue;

            var scenes = _projectService.GetScenesForChapter(chapter.Guid);
            var sceneItems = new List<ManuscriptSceneItem>();

            foreach (var scene in scenes)
            {
                var html = ReadSceneContent(chapter, scene);
                var wordCount = scene.WordCount;
                totalWords += wordCount;
                totalScenes++;

                var item = new ManuscriptSceneItem(chapter, scene, scene.Title, html, wordCount);
                sceneItems.Add(item);
                _sceneIndex[scene.Id] = item;
            }

            if (sceneItems.Count > 0)
            {
                sections.Add(new ManuscriptSection(
                    chapter,
                    chapter.Status.ToString(),
                    string.IsNullOrWhiteSpace(chapter.Act) ? null : chapter.Act,
                    sceneItems));
            }
        }

        Sections = sections;
        TotalWords = totalWords;
        TotalScenes = totalScenes;
        HasContent = sections.Count > 0;
        OnPropertyChanged(nameof(TotalWordsDisplay));
        OnPropertyChanged(nameof(ReadingTimeDisplay));
    }

    private string ReadSceneContent(ChapterData chapter, SceneData scene)
    {
        if (_projectService.ProjectRoot == null)
            return string.Empty;

        var scenePath = _projectService.GetSceneFilePath(chapter, scene);
        return File.Exists(scenePath) ? File.ReadAllText(scenePath) : string.Empty;
    }

    private ManuscriptSceneItem? FindScene(string sceneId)
    {
        _sceneIndex.TryGetValue(sceneId, out var item);
        return item;
    }
}

public sealed class ManuscriptSection
{
    public ChapterData Chapter { get; }
    public string ChapterTitle { get; }
    public string Status { get; }
    public string? Act { get; }
    public List<ManuscriptSceneItem> Scenes { get; }

    public ManuscriptSection(ChapterData chapter, string status, string? act, List<ManuscriptSceneItem> scenes)
    {
        Chapter = chapter;
        ChapterTitle = chapter.Title;
        Status = status;
        Act = act;
        Scenes = scenes;
    }
}

public sealed class ManuscriptSceneItem
{
    public ChapterData Chapter { get; }
    public SceneData Scene { get; }
    public string Title { get; }
    public string HtmlContent { get; set; }
    public bool IsDirty { get; set; }

    public ManuscriptSceneItem(ChapterData chapter, SceneData scene, string title, string htmlContent, int wordCount)
    {
        Chapter = chapter;
        Scene = scene;
        Title = title;
        HtmlContent = htmlContent;
    }
}
