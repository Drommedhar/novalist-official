using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Sdk.Models;
using Novalist.Sdk.Services;

namespace Novalist.Extensions.AiAssistant.ViewModels;

public partial class StoryAnalysisViewModel : ObservableObject
{
    private readonly IHostServices _host;
    private readonly AiAssistantExtension _extension;
    private readonly IExtensionLocalization _loc;

    [ObservableProperty]
    private bool _isAnalysing;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private int _progressCurrent;

    [ObservableProperty]
    private int _progressTotal;

    [ObservableProperty]
    private string _streamingLog = string.Empty;

    [ObservableProperty]
    private string _streamingThinking = string.Empty;

    [ObservableProperty]
    private string _filterType = "all";

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private AnalysisChapterOption? _selectedChapter;

    [ObservableProperty]
    private string? _selectedSceneFilter;

    [ObservableProperty]
    private string? _selectedSceneOption;

    [ObservableProperty]
    private bool _hasMultipleScenes;

    public ObservableCollection<AnalysisChapterOption> AvailableChapters { get; } = [];
    public ObservableCollection<string> AvailableScenes { get; } = [];
    public ObservableCollection<string> SceneFilterOptions { get; } = [];
    public ObservableCollection<AnalysisFindingItem> AllFindings { get; } = [];
    public ObservableCollection<AnalysisFindingItem> FilteredFindings { get; } = [];

    private CancellationTokenSource? _cts;
    private readonly object _streamLock = new();
    private readonly StringBuilder _pendingLog = new();
    private readonly StringBuilder _pendingThinking = new();
    private bool _flushScheduled;

    public StoryAnalysisViewModel(IHostServices host, AiAssistantExtension extension)
    {
        _host = host;
        _extension = extension;
        _loc = host.GetLocalization(extension.Id);
    }

    partial void OnSelectedSceneOptionChanged(string? value)
    {
        SelectedSceneFilter = value != null && AvailableScenes.Contains(value) ? value : null;
        ApplyFilter();
    }

    public void RefreshChapters()
    {
        AvailableChapters.Clear();
        var chapters = _host.ProjectService.GetChaptersOrdered();
        foreach (var ch in chapters)
            AvailableChapters.Add(new AnalysisChapterOption(ch.Guid, ch.Title));
        if (AvailableChapters.Count > 0 && SelectedChapter == null)
            SelectedChapter = AvailableChapters[0];
    }

    [RelayCommand]
    private async Task AnalyseCurrentChapterAsync()
    {
        if (SelectedChapter == null) return;

        var chapters = _host.ProjectService.GetChaptersOrdered();
        var chapter = chapters.FirstOrDefault(c => c.Guid == SelectedChapter.Guid);
        if (chapter == null) return;

        IsAnalysing = true;
        AllFindings.Clear();
        FilteredFindings.Clear();
        AvailableScenes.Clear();
        SceneFilterOptions.Clear();
        HasMultipleScenes = false;
        SelectedSceneFilter = null;
        SelectedSceneOption = null;
        HasResults = false;
        StreamingLog = string.Empty;
        StreamingThinking = string.Empty;
        _cts = new CancellationTokenSource();

        try
        {
            var settings = _extension.Settings;
            var chatVm = new AiChatViewModel(_host, _extension);
            var entities = await chatVm.CollectEntitySummariesAsync().ConfigureAwait(false);

            var checks = new EnabledChecks
            {
                References = settings.CheckReferences,
                Inconsistencies = settings.CheckInconsistencies,
                Suggestions = settings.CheckSuggestions,
                SceneStats = settings.CheckSceneStats,
            };

            // Get text for all scenes in the selected chapter
            var scenes = _host.ProjectService.GetScenesForChapter(chapter.Guid);
            var sceneTexts = new List<(Sdk.Services.SceneInfo Scene, string Text)>();
            foreach (var s in scenes)
            {
                var text = await _host.ProjectService.ReadSceneContentAsync(chapter.Guid, s.Id).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(text))
                    sceneTexts.Add((s, text));
            }

            _host.PostToUI(() =>
            {
                ProgressTotal = sceneTexts.Count;
                ProgressCurrent = 0;
            });

            foreach (var (s, text) in sceneTexts)
            {
                _host.PostToUI(() =>
                {
                    StreamingLog = string.Empty;
                    StreamingThinking = string.Empty;
                    ProgressText = _loc.T("ai.analysingScene", s.Title);
                });

                lock (_streamLock)
                {
                    _pendingLog.Clear();
                    _pendingThinking.Clear();
                }

                var context = new ChapterContext
                {
                    ChapterName = chapter.Title,
                    SceneName = s.Title,
                    Date = chapter.Date,
                };

                var result = await _extension.AiService.AnalyseChapterWholeAsync(
                    text, entities,
                    null, context, checks,
                    OnStreamingChunk,
                    OnThinkingChunk,
                    settings.DisableRegexReferences,
                    _cts.Token).ConfigureAwait(false);

                // Flush any remaining buffered streaming output
                _host.PostToUI(FlushStreamingBuffers);

                foreach (var f in result.Findings)
                {
                    var item = new AnalysisFindingItem(f, chapter.Title, s.Title);
                    _host.PostToUI(() => AllFindings.Add(item));
                }

                _host.PostToUI(() => ProgressCurrent++);
            }

            _host.PostToUI(() =>
            {
                var sceneNames = AllFindings.Select(f => f.SceneName).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
                foreach (var name in sceneNames)
                    AvailableScenes.Add(name);

                SceneFilterOptions.Clear();
                SceneFilterOptions.Add(_loc.T("ai.allScenes"));
                foreach (var name in sceneNames)
                    SceneFilterOptions.Add(name);
                HasMultipleScenes = sceneNames.Count > 1;
                SelectedSceneOption = SceneFilterOptions[0];

                ApplyFilter();
                HasResults = AllFindings.Count > 0;
                ProgressText = _loc.T("ai.analysisComplete", AllFindings.Count);
            });
        }
        catch (OperationCanceledException)
        {
            _host.PostToUI(() => ProgressText = _loc.T("ai.analysisCancelled"));
        }
        catch (Exception ex)
        {
            _host.PostToUI(() => ProgressText = $"Error: {ex.Message}");
        }
        finally
        {
            _host.PostToUI(() => IsAnalysing = false);
            _cts = null;
        }
    }

    [RelayCommand]
    private async Task AnalyseWholeStoryAsync()
    {
        IsAnalysing = true;
        AllFindings.Clear();
        FilteredFindings.Clear();
        AvailableScenes.Clear();
        SceneFilterOptions.Clear();
        HasMultipleScenes = false;
        SelectedSceneFilter = null;
        SelectedSceneOption = null;
        HasResults = false;
        StreamingLog = string.Empty;
        StreamingThinking = string.Empty;
        _cts = new CancellationTokenSource();

        try
        {
            var chatVm = new AiChatViewModel(_host, _extension);
            var entities = await chatVm.CollectEntitySummariesAsync().ConfigureAwait(false);
            var chapters = _host.ProjectService.GetChaptersOrdered();
            var chapterTexts = new List<ChapterTextEntry>();

            foreach (var ch in chapters)
            {
                var scenes = _host.ProjectService.GetScenesForChapter(ch.Guid);
                var sb = new StringBuilder();
                foreach (var s in scenes)
                {
                    var text = await _host.ProjectService.ReadSceneContentAsync(ch.Guid, s.Id).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        if (sb.Length > 0) sb.AppendLine();
                        sb.Append(text);
                    }
                }
                if (sb.Length > 0)
                    chapterTexts.Add(new ChapterTextEntry { Name = ch.Title, Text = sb.ToString() });
            }

            _host.PostToUI(() =>
            {
                ProgressText = _loc.T("ai.analysingWholeStory");
                ProgressTotal = 1;
                ProgressCurrent = 0;
            });

            var result = await _extension.AiService.AnalyseWholeStoryAsync(
                chapterTexts, entities, [],
                OnStreamingChunk,
                OnThinkingChunk,
                _cts.Token).ConfigureAwait(false);

            // Flush any remaining buffered streaming output
            _host.PostToUI(FlushStreamingBuffers);

            foreach (var f in result.Findings)
                _host.PostToUI(() => AllFindings.Add(new AnalysisFindingItem(f, "", "")));

            _host.PostToUI(() =>
            {
                ProgressCurrent = 1;
                ApplyFilter();
                HasResults = AllFindings.Count > 0;
                ProgressText = _loc.T("ai.analysisComplete", AllFindings.Count);
            });
        }
        catch (OperationCanceledException)
        {
            _host.PostToUI(() => ProgressText = _loc.T("ai.analysisCancelled"));
        }
        catch (Exception ex)
        {
            _host.PostToUI(() => ProgressText = $"Error: {ex.Message}");
        }
        finally
        {
            _host.PostToUI(() => IsAnalysing = false);
            _cts = null;
        }
    }

    [RelayCommand]
    private void CancelAnalysis()
    {
        _cts?.Cancel();
        _extension.AiService.Cancel();
    }

    [RelayCommand]
    private void SetFilter(string type)
    {
        FilterType = type;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredFindings.Clear();
        foreach (var f in AllFindings)
        {
            if (FilterType != "all" && f.Type != FilterType)
                continue;
            if (SelectedSceneFilter != null && f.SceneName != SelectedSceneFilter)
                continue;
            FilteredFindings.Add(f);
        }
    }

    private void OnStreamingChunk(string chunk)
    {
        lock (_streamLock)
        {
            _pendingLog.Append(chunk);
            ScheduleFlush();
        }
    }

    private void OnThinkingChunk(string chunk)
    {
        lock (_streamLock)
        {
            _pendingThinking.Append(chunk);
            ScheduleFlush();
        }
    }

    private void ScheduleFlush()
    {
        if (_flushScheduled) return;
        _flushScheduled = true;
        _host.PostToUI(FlushStreamingBuffers);
    }

    private void FlushStreamingBuffers()
    {
        string logBatch;
        string thinkingBatch;
        lock (_streamLock)
        {
            logBatch = _pendingLog.ToString();
            thinkingBatch = _pendingThinking.ToString();
            _pendingLog.Clear();
            _pendingThinking.Clear();
            _flushScheduled = false;
        }

        if (logBatch.Length > 0)
            StreamingLog += logBatch;
        if (thinkingBatch.Length > 0)
            StreamingThinking += thinkingBatch;
    }
}

public sealed record AnalysisChapterOption(string Guid, string Title)
{
    public override string ToString() => Title;
}

public sealed class AnalysisFindingItem
{
    public AnalysisFindingItem(AiFinding finding, string chapterName, string sceneName)
    {
        Type = finding.Type;
        Title = finding.Title;
        Description = finding.Description;
        Excerpt = finding.Excerpt;
        EntityName = finding.EntityName;
        EntityType = finding.EntityType;
        ChapterName = chapterName;
        SceneName = sceneName;

        TypeIcon = Type switch
        {
            "reference" => "🔗",
            "inconsistency" => "⚠",
            "suggestion" => "💡",
            "scene_stats" => "📊",
            _ => "•",
        };

        ScenePov = finding.ScenePov;
        SceneEmotion = finding.SceneEmotion;
        SceneIntensity = finding.SceneIntensity;
        SceneConflict = finding.SceneConflict;
    }

    public string Type { get; }
    public string Title { get; }
    public string Description { get; }
    public string Excerpt { get; }
    public string EntityName { get; }
    public string EntityType { get; }
    public string ChapterName { get; }
    public string SceneName { get; }
    public string TypeIcon { get; }
    public bool HasExcerpt => !string.IsNullOrEmpty(Excerpt);
    public bool HasEntity => !string.IsNullOrEmpty(EntityName);
    public bool HasScene => !string.IsNullOrEmpty(SceneName);

    // Scene stats
    public string? ScenePov { get; }
    public string? SceneEmotion { get; }
    public int? SceneIntensity { get; }
    public string? SceneConflict { get; }
    public bool IsSceneStats => Type == "scene_stats";
}
