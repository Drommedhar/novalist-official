using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.Localization;

namespace Novalist.Desktop.ViewModels;

public partial class TimelineViewModel : ObservableObject
{
    private readonly IProjectService _projectService;

    // ── Source colors ───────────────────────────────────────────────
    public static readonly Dictionary<string, Color> SourceColors = new()
    {
        ["act"] = Color.Parse("#9b59b6"),
        ["chapter"] = Color.Parse("#3498db"),
        ["scene"] = Color.Parse("#27ae60"),
        ["manual"] = Color.Parse("#e67e22"),
    };

    // ── Observable state ────────────────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<TimelineEventItem> _events = [];

    [ObservableProperty]
    private string _viewMode = "vertical";

    [ObservableProperty]
    private string _zoomLevel = "month";

    public bool IsVertical => ViewMode == "vertical";
    public bool IsHorizontal => ViewMode == "horizontal";

    [ObservableProperty]
    private string? _filterCharacter;

    [ObservableProperty]
    private string? _filterLocation;

    [ObservableProperty]
    private string? _filterSource;

    [ObservableProperty]
    private SourceFilterItem? _selectedSourceFilter;

    [ObservableProperty]
    private ObservableCollection<string> _availableCharacters = [];

    [ObservableProperty]
    private ObservableCollection<string> _availableLocations = [];

    [ObservableProperty]
    private ObservableCollection<SourceFilterItem> _availableSources = [];

    [ObservableProperty]
    private bool _hasEvents;

    [ObservableProperty]
    private ObservableCollection<TimelineGroup> _groups = [];

    // ── Form state ──────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isFormOpen;

    [ObservableProperty]
    private string _formTitle = string.Empty;

    [ObservableProperty]
    private string _formDate = string.Empty;

    [ObservableProperty]
    private string _formDescription = string.Empty;

    [ObservableProperty]
    private string _formCategoryId = "plot";

    [ObservableProperty]
    private CategoryItem? _selectedCategory;

    [ObservableProperty]
    private ObservableCollection<CategoryItem> _availableCategories = [];

    [ObservableProperty]
    private string _formLinkedChapterGuid = string.Empty;

    [ObservableProperty]
    private DateTime? _anchorDate;

    [ObservableProperty]
    private string _highlightedGroupKey = string.Empty;

    [ObservableProperty]
    private bool _isJumpFlyoutOpen;

    [ObservableProperty]
    private string _jumpDateInput = string.Empty;

    public event Action<string>? ScrollToGroupRequested;

    [ObservableProperty]
    private ObservableCollection<ChapterData> _availableChapters = [];

    private string? _editingEventId;

    public TimelineViewModel(IProjectService projectService)
    {
        _projectService = projectService;
    }

    public void Refresh() => _ = RefreshAsync();

    public async Task RefreshAsync()
    {
        var timeline = _projectService.ProjectSettings.Timeline;
        ViewMode = timeline.ViewMode;
        ZoomLevel = timeline.ZoomLevel;

        AvailableSources = new ObservableCollection<SourceFilterItem>([
            new() { Value = "act", Label = Loc.T("timeline.actEvent") },
            new() { Value = "chapter", Label = Loc.T("timeline.chapterEvent") },
            new() { Value = "scene", Label = Loc.T("timeline.sceneEvent") },
            new() { Value = "manual", Label = Loc.T("timeline.manualEvent") },
        ]);

        // Heavy build off UI thread
        var built = await Task.Run(BuildSnapshot).ConfigureAwait(true);
        Events = new ObservableCollection<TimelineEventItem>(built.FilteredEvents);
        HasEvents = Events.Count > 0;
        AvailableCharacters = new ObservableCollection<string>(built.Characters);
        AvailableLocations = new ObservableCollection<string>(built.Locations);
        Groups = new ObservableCollection<TimelineGroup>(built.Groups);
    }

    private void BuildAndRender()
    {
        var snap = BuildSnapshot();
        Events = new ObservableCollection<TimelineEventItem>(snap.FilteredEvents);
        HasEvents = Events.Count > 0;
        AvailableCharacters = new ObservableCollection<string>(snap.Characters);
        AvailableLocations = new ObservableCollection<string>(snap.Locations);
        Groups = new ObservableCollection<TimelineGroup>(snap.Groups);
    }

    private record TimelineSnapshot(
        List<TimelineEventItem> FilteredEvents,
        SortedSet<string> Characters,
        SortedSet<string> Locations,
        List<TimelineGroup> Groups);

    private TimelineSnapshot BuildSnapshot()
    {
        var allEvents = BuildEvents();
        var filtered = ApplyFilters(allEvents).ToList();

        var chars = new SortedSet<string>();
        var locs = new SortedSet<string>();
        foreach (var e in allEvents)
        {
            foreach (var c in e.Characters) chars.Add(c);
            foreach (var l in e.Locations) locs.Add(l);
        }

        var groupMap = new Dictionary<string, List<TimelineEventItem>>();
        foreach (var evt in filtered)
        {
            var key = GroupKey(evt.SortDate, ZoomLevel);
            if (!groupMap.TryGetValue(key, out var list))
            {
                list = [];
                groupMap[key] = list;
            }
            list.Add(evt);
        }

        var groupsList = groupMap
            .Select(kv => new TimelineGroup { Key = kv.Key, Label = GroupLabel(kv.Key, ZoomLevel), Events = new ObservableCollection<TimelineEventItem>(kv.Value) })
            .ToList();

        return new TimelineSnapshot(filtered, chars, locs, groupsList);
    }

    private List<TimelineEventItem> BuildEvents()
    {
        var events = new List<TimelineEventItem>();
        var chapters = _projectService.GetChaptersOrdered();
        var timeline = _projectService.ProjectSettings.Timeline;
        var seenActs = new HashSet<string>();

        foreach (var ch in chapters)
        {
            // Act marker
            if (!string.IsNullOrEmpty(ch.Act) && seenActs.Add(ch.Act))
            {
                events.Add(new TimelineEventItem
                {
                    Id = $"act-{ch.Act}",
                    Title = ch.Act,
                    DateStr = ch.Date,
                    SortDate = ParseDate(ch.Date),
                    Source = "act",
                    SourceLabel = Loc.T("timeline.actEvent"),
                    SourceColor = SourceColors["act"],
                    ChapterOrder = ch.Order - 0.5,
                });
            }

            // Chapter event
            if (!string.IsNullOrEmpty(ch.Date))
            {
                events.Add(new TimelineEventItem
                {
                    Id = $"ch-{ch.Guid}",
                    Title = ch.Title,
                    DateStr = ch.Date,
                    SortDate = ParseDate(ch.Date),
                    Source = "chapter",
                    SourceLabel = Loc.T("timeline.chapterEvent"),
                    SourceColor = SourceColors["chapter"],
                    ChapterGuid = ch.Guid,
                    ChapterOrder = ch.Order,
                });
            }

            // Scene events
            var scenes = _projectService.GetScenesForChapter(ch.Guid);
            foreach (var scene in scenes)
            {
                var sceneDate = !string.IsNullOrWhiteSpace(scene.Date) ? scene.Date : ch.Date;
                if (!string.IsNullOrEmpty(sceneDate))
                {
                    events.Add(new TimelineEventItem
                    {
                        Id = $"sc-{ch.Guid}-{scene.Id}",
                        Title = $"{ch.Title}: {scene.Title}",
                        DateStr = sceneDate,
                        SortDate = ParseDate(sceneDate),
                        Source = "scene",
                        SourceLabel = Loc.T("timeline.sceneEvent"),
                        SourceColor = SourceColors["scene"],
                        ChapterGuid = ch.Guid,
                        SceneId = scene.Id,
                        ChapterOrder = ch.Order,
                    });
                }
            }
        }

        // Manual events
        foreach (var me in timeline.ManualEvents)
        {
            events.Add(new TimelineEventItem
            {
                Id = $"manual-{me.Id}",
                Title = me.Title,
                DateStr = me.Date,
                SortDate = ParseDate(me.Date),
                Description = me.Description,
                Source = "manual",
                SourceLabel = Loc.T("timeline.manualEvent"),
                SourceColor = SourceColors["manual"],
                CategoryId = me.CategoryId,
                ChapterGuid = me.LinkedChapterGuid,
                SceneId = me.LinkedSceneId,
                ChapterOrder = me.Order,
                Characters = new ObservableCollection<string>(me.Characters),
                Locations = new ObservableCollection<string>(me.Locations),
                IsManual = true,
            });
        }

        // Sort
        events.Sort((a, b) =>
        {
            if (a.SortDate.HasValue && b.SortDate.HasValue)
            {
                var diff = a.SortDate.Value.CompareTo(b.SortDate.Value);
                if (diff != 0) return diff;
                return a.ChapterOrder.CompareTo(b.ChapterOrder);
            }
            if (a.SortDate.HasValue) return -1;
            if (b.SortDate.HasValue) return 1;
            return a.ChapterOrder.CompareTo(b.ChapterOrder);
        });

        return events;
    }

    private List<TimelineEventItem> ApplyFilters(List<TimelineEventItem> events)
    {
        var result = events.AsEnumerable();
        if (!string.IsNullOrEmpty(FilterCharacter))
            result = result.Where(e => e.Characters.Contains(FilterCharacter));
        if (!string.IsNullOrEmpty(FilterLocation))
            result = result.Where(e => e.Locations.Contains(FilterLocation));
        if (!string.IsNullOrEmpty(FilterSource))
            result = result.Where(e => e.Source == FilterSource);
        return result.ToList();
    }

    // ── Commands ────────────────────────────────────────────────────

    /// <summary>Provided by the host to show a Save-file picker. Returns the
    /// chosen path or null when the user cancels.</summary>
    public Func<string, string, Task<string?>>? ShowSaveFileDialog { get; set; }

    [RelayCommand]
    private async Task ExportOutlineAsync()
    {
        if (ShowSaveFileDialog == null) return;
        var path = await ShowSaveFileDialog.Invoke("story_outline.md", "Markdown");
        if (string.IsNullOrEmpty(path)) return;

        var export = new ExportService(_projectService);
        await export.ExportTimelineOutlineAsync(path);
    }

    [RelayCommand]
    private async Task ToggleViewModeAsync()
    {
        ViewMode = ViewMode == "vertical" ? "horizontal" : "vertical";
        _projectService.ProjectSettings.Timeline.ViewMode = ViewMode;
        await _projectService.SaveProjectSettingsAsync();
        BuildAndRender();
    }

    [RelayCommand]
    private async Task CycleZoomAsync()
    {
        var levels = new[] { "year", "month", "day" };
        var idx = Array.IndexOf(levels, ZoomLevel);
        ZoomLevel = levels[(idx + 1) % levels.Length];
        _projectService.ProjectSettings.Timeline.ZoomLevel = ZoomLevel;
        await _projectService.SaveProjectSettingsAsync();
        BuildAndRender();
    }

    [RelayCommand]
    private void ApplyFilter()
    {
        BuildAndRender();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        FilterCharacter = null;
        FilterLocation = null;
        SelectedSourceFilter = null;
        FilterSource = null;
        BuildAndRender();
    }

    [RelayCommand]
    private void ShowAddForm()
    {
        _editingEventId = null;
        FormTitle = string.Empty;
        FormDate = string.Empty;
        FormDescription = string.Empty;
        FormCategoryId = "plot";
        FormLinkedChapterGuid = string.Empty;
        AvailableChapters = new ObservableCollection<ChapterData>(_projectService.GetChaptersOrdered());
        PopulateCategories();
        SelectedCategory = AvailableCategories.FirstOrDefault(c => c.Value == "plot");
        IsFormOpen = true;
    }

    [RelayCommand]
    private void EditEvent(string eventId)
    {
        var realId = eventId.StartsWith("manual-", StringComparison.Ordinal)
            ? eventId["manual-".Length..]
            : eventId;
        var timeline = _projectService.ProjectSettings.Timeline;
        var existing = timeline.ManualEvents.FirstOrDefault(e => e.Id == realId);
        if (existing == null) return;

        _editingEventId = existing.Id;
        FormTitle = existing.Title;
        FormDate = existing.Date;
        FormDescription = existing.Description;
        FormCategoryId = existing.CategoryId;
        FormLinkedChapterGuid = existing.LinkedChapterGuid;
        AvailableChapters = new ObservableCollection<ChapterData>(_projectService.GetChaptersOrdered());
        PopulateCategories();
        SelectedCategory = AvailableCategories.FirstOrDefault(c => c.Value == existing.CategoryId);
        IsFormOpen = true;
    }

    [RelayCommand]
    private async Task DeleteEventAsync(string eventId)
    {
        var realId = eventId.StartsWith("manual-", StringComparison.Ordinal)
            ? eventId["manual-".Length..]
            : eventId;
        var timeline = _projectService.ProjectSettings.Timeline;
        var idx = timeline.ManualEvents.FindIndex(e => e.Id == realId);
        if (idx < 0) return;
        timeline.ManualEvents.RemoveAt(idx);
        await _projectService.SaveProjectSettingsAsync();
        BuildAndRender();
    }

    [RelayCommand]
    private async Task SaveFormAsync()
    {
        if (string.IsNullOrWhiteSpace(FormTitle)) return;

        var timeline = _projectService.ProjectSettings.Timeline;
        var manualEvent = new TimelineManualEvent
        {
            Id = _editingEventId ?? $"evt-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid().ToString()[..7]}",
            Title = FormTitle.Trim(),
            Date = FormDate.Trim(),
            Description = FormDescription.Trim(),
            CategoryId = FormCategoryId,
            LinkedChapterGuid = FormLinkedChapterGuid,
            Order = _editingEventId != null
                ? timeline.ManualEvents.FirstOrDefault(e => e.Id == _editingEventId)?.Order ?? timeline.ManualEvents.Count
                : timeline.ManualEvents.Count,
        };

        if (_editingEventId != null)
        {
            var idx = timeline.ManualEvents.FindIndex(e => e.Id == _editingEventId);
            if (idx >= 0)
                timeline.ManualEvents[idx] = manualEvent;
        }
        else
        {
            timeline.ManualEvents.Add(manualEvent);
        }

        IsFormOpen = false;
        await _projectService.SaveProjectSettingsAsync();
        BuildAndRender();
    }

    [RelayCommand]
    private void CancelForm()
    {
        IsFormOpen = false;
    }

    [RelayCommand]
    private void ScrollToToday() => SetAnchorAndScroll(DateTime.Today);

    [RelayCommand]
    private void PanPrevious() => Pan(-1);

    [RelayCommand]
    private void PanNext() => Pan(1);

    [RelayCommand]
    private void OpenJumpFlyout()
    {
        JumpDateInput = (AnchorDate ?? DateTime.Today).ToString("yyyy-MM-dd");
        IsJumpFlyoutOpen = true;
    }

    [RelayCommand]
    private void ConfirmJump()
    {
        if (DateTime.TryParseExact(JumpDateInput?.Trim() ?? string.Empty, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            SetAnchorAndScroll(parsed);
        }
        IsJumpFlyoutOpen = false;
    }

    [RelayCommand]
    private void CancelJump() => IsJumpFlyoutOpen = false;

    private void Pan(int direction)
    {
        var current = AnchorDate ?? DateTime.Today;
        var next = ZoomLevel switch
        {
            "year" => current.AddYears(direction),
            "day" => current.AddDays(direction),
            _ => current.AddMonths(direction),
        };
        SetAnchorAndScroll(next);
    }

    private void SetAnchorAndScroll(DateTime date)
    {
        AnchorDate = date;
        HighlightedGroupKey = GroupKey(date, ZoomLevel);
        ScrollToGroupRequested?.Invoke(HighlightedGroupKey);
    }

    public IReadOnlyList<StoryStructureTemplate> AvailableStructureTemplates { get; } = StoryStructureTemplates.All;

    [RelayCommand]
    private async Task ApplyStructureTemplateAsync(string? templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId)) return;
        var template = StoryStructureTemplates.GetById(templateId);
        if (template == null) return;

        var timeline = _projectService.ProjectSettings.Timeline;
        var nextOrder = timeline.ManualEvents.Count;
        foreach (var beat in template.Beats)
        {
            timeline.ManualEvents.Add(new TimelineManualEvent
            {
                Id = $"evt-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid().ToString()[..7]}",
                Title = beat.Title,
                Description = beat.Description,
                CategoryId = beat.CategoryId,
                Order = nextOrder++
            });
        }

        await _projectService.SaveProjectSettingsAsync();
        BuildAndRender();
    }

    private void PopulateCategories()
    {
        AvailableCategories = new ObservableCollection<CategoryItem>([
            new() { Value = "plot", Label = Loc.T("timeline.catPlot") },
            new() { Value = "character", Label = Loc.T("timeline.catCharacter") },
            new() { Value = "location", Label = Loc.T("timeline.catLocation") },
            new() { Value = "world", Label = Loc.T("timeline.catWorld") },
            new() { Value = "other", Label = Loc.T("timeline.catOther") },
        ]);
    }

    /// <summary>Event requesting a scene to be opened in the editor.</summary>
    public event Action<ChapterData, SceneData>? SceneOpenRequested;

    [RelayCommand]
    private void OpenLinkedChapter(string chapterGuid)
    {
        if (string.IsNullOrEmpty(chapterGuid)) return;
        var chapter = _projectService.GetChaptersOrdered().FirstOrDefault(c => c.Guid == chapterGuid);
        if (chapter == null) return;
        var scenes = _projectService.GetScenesForChapter(chapterGuid);
        var firstScene = scenes.FirstOrDefault();
        if (firstScene != null)
            SceneOpenRequested?.Invoke(chapter, firstScene);
    }

    // ── Display helpers ─────────────────────────────────────────────

    public string ViewModeLabel => ViewMode == "vertical"
        ? Loc.T("timeline.viewVertical")
        : Loc.T("timeline.viewHorizontal");

    public string ZoomLabel => ZoomLevel switch
    {
        "year" => Loc.T("timeline.zoomYear"),
        "day" => Loc.T("timeline.zoomDay"),
        _ => Loc.T("timeline.zoomMonth"),
    };

    partial void OnViewModeChanged(string value)
    {
        OnPropertyChanged(nameof(ViewModeLabel));
        OnPropertyChanged(nameof(IsVertical));
        OnPropertyChanged(nameof(IsHorizontal));
    }
    partial void OnZoomLevelChanged(string value)
    {
        OnPropertyChanged(nameof(ZoomLabel));
        if (AnchorDate.HasValue)
            HighlightedGroupKey = GroupKey(AnchorDate.Value, value);
    }
    partial void OnFilterCharacterChanged(string? value) => BuildAndRender();
    partial void OnFilterLocationChanged(string? value) => BuildAndRender();
    partial void OnSelectedSourceFilterChanged(SourceFilterItem? value)
    {
        FilterSource = value?.Value;
    }
    partial void OnSelectedCategoryChanged(CategoryItem? value)
    {
        if (value != null)
            FormCategoryId = value.Value;
    }
    partial void OnFilterSourceChanged(string? value) => BuildAndRender();

    // ── Date parsing ────────────────────────────────────────────────

    public static DateTime? ParseDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return null;
        var s = dateStr.Trim();

        // ISO: YYYY-MM-DD
        if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
            return iso;

        // Month precision: YYYY-MM
        if (DateTime.TryParseExact(s, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var ym))
            return ym;

        // Year precision: YYYY
        if (DateTime.TryParseExact(s, "yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var y))
            return y;

        // European: DD.MM.YYYY
        if (DateTime.TryParseExact(s, "d.M.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var eu))
            return eu;

        // Fallback
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fallback))
            return fallback;

        return null;
    }

    public static string GroupKey(DateTime? date, string zoom)
    {
        if (!date.HasValue) return "no-date";
        var d = date.Value;
        return zoom switch
        {
            "year" => $"{d.Year}",
            "day" => $"{d.Year}-{d.Month:D2}-{d.Day:D2}",
            _ => $"{d.Year}-{d.Month:D2}",
        };
    }

    public static string GroupLabel(string key, string zoom)
    {
        if (key == "no-date") return "???";
        var parts = key.Split('-').Select(int.Parse).ToArray();
        var months = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        return zoom switch
        {
            "year" => $"{parts[0]}",
            "day" => $"{months[parts[1] - 1]} {parts[2]}, {parts[0]}",
            _ => $"{months[parts[1] - 1]} {parts[0]}",
        };
    }

    public static string FormatDateLabel(DateTime? date, string dateStr, string zoom)
    {
        if (!date.HasValue) return string.IsNullOrEmpty(dateStr) ? "???" : dateStr;
        var d = date.Value;
        var mo = d.ToString("MMM", CultureInfo.InvariantCulture);
        return zoom switch
        {
            "year" => $"{d.Year}",
            "day" => $"{mo} {d.Day}, {d.Year}",
            _ => $"{mo} {d.Year}",
        };
    }
}

// ── Display models ──────────────────────────────────────────────

public class TimelineEventItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string DateStr { get; set; } = string.Empty;
    public DateTime? SortDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string SourceLabel { get; set; } = string.Empty;
    public Color SourceColor { get; set; }
    public string CategoryId { get; set; } = string.Empty;
    public string ChapterGuid { get; set; } = string.Empty;
    public string SceneId { get; set; } = string.Empty;
    public double ChapterOrder { get; set; }
    public ObservableCollection<string> Characters { get; set; } = [];
    public ObservableCollection<string> Locations { get; set; } = [];
    public bool IsManual { get; set; }
    public bool HasDescription => !string.IsNullOrEmpty(Description);
    public bool HasCharacters => Characters.Count > 0;
    public bool HasLocations => Locations.Count > 0;
    public bool HasMeta => HasCharacters || HasLocations;
    public bool HasChapterLink => !string.IsNullOrEmpty(ChapterGuid);
    public bool IsAct => Source == "act";
    public bool HasNoDate => SortDate == null;

    public string FormattedDate(string zoom) => TimelineViewModel.FormatDateLabel(SortDate, DateStr, zoom);
}

public class TimelineGroup
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public ObservableCollection<TimelineEventItem> Events { get; set; } = [];
}

public class SourceFilterItem
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public override string ToString() => Label;
}

public class CategoryItem
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public override string ToString() => Label;
}
