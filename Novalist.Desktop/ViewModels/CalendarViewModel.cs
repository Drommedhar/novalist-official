using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;

namespace Novalist.Desktop.ViewModels;

/// <summary>
/// Week calendar showing scenes as events. Scene start/end dates + optional
/// times resolved through <see cref="StoryDateResolver"/>; events without a
/// time render as a full-day band; events with time slot into the hour grid.
/// </summary>
public partial class CalendarViewModel : ObservableObject
{
    private readonly IProjectService _projectService;

    private bool _anchorInitialized;

    [ObservableProperty]
    private DateTime _weekAnchor = DateTime.Today;

    [ObservableProperty]
    private string _rangeLabel = string.Empty;

    [ObservableProperty]
    private string _viewMode = "Week"; // Week | Month | Year

    public bool IsWeekView => ViewMode == "Week";
    public bool IsMonthView => ViewMode == "Month";
    public bool IsYearView => ViewMode == "Year";

    partial void OnViewModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsWeekView));
        OnPropertyChanged(nameof(IsMonthView));
        OnPropertyChanged(nameof(IsYearView));
        Refresh();
    }

    public ObservableCollection<CalendarDayColumn> Days { get; } = new();
    public ObservableCollection<CalendarMonthDay> MonthDays { get; } = new();
    public ObservableCollection<CalendarYearMonth> YearMonths { get; } = new();

    public int[] HoursOfDay { get; } = Enumerable.Range(0, 24).ToArray();

    public event Action<string, string>? SceneOpenRequested; // (chapterGuid, sceneId)

    public CalendarViewModel(IProjectService projectService)
    {
        _projectService = projectService;
    }

    [RelayCommand]
    private void PreviousWeek()
    {
        WeekAnchor = ViewMode switch
        {
            "Month" => WeekAnchor.AddMonths(-1),
            "Year" => WeekAnchor.AddYears(-1),
            _ => WeekAnchor.AddDays(-7),
        };
        Refresh();
        SaveAnchor();
    }

    [RelayCommand]
    private void NextWeek()
    {
        WeekAnchor = ViewMode switch
        {
            "Month" => WeekAnchor.AddMonths(1),
            "Year" => WeekAnchor.AddYears(1),
            _ => WeekAnchor.AddDays(7),
        };
        Refresh();
        SaveAnchor();
    }

    [RelayCommand]
    private void Today()
    {
        WeekAnchor = DateTime.Today;
        Refresh();
        SaveAnchor();
    }

    [RelayCommand] private void SetWeekView() => ViewMode = "Week";
    [RelayCommand] private void SetMonthView() => ViewMode = "Month";
    [RelayCommand] private void SetYearView() => ViewMode = "Year";

    [RelayCommand]
    private void JumpToDate(DateTime date)
    {
        WeekAnchor = date;
        ViewMode = ViewMode == "Year" ? "Month" : "Week";
    }

    private void SaveAnchor()
    {
        if (!_projectService.IsProjectLoaded) return;
        _projectService.ProjectSettings.CalendarAnchor =
            WeekAnchor.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _ = _projectService.SaveProjectSettingsAsync();
    }

    /// <summary>Resolve initial anchor: persisted setting; else earliest scene
    /// date via Scene → Chapter → Act fallback; else today.</summary>
    private DateTime ResolveInitialAnchor()
    {
        var saved = _projectService.ProjectSettings?.CalendarAnchor;
        if (!string.IsNullOrWhiteSpace(saved)
            && DateTime.TryParse(saved, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
            return dt;

        if (_projectService.IsProjectLoaded)
        {
            var acts = _projectService.ActiveBook?.Acts;
            var earliest = DateTime.MaxValue;
            var found = false;
            foreach (var chapter in _projectService.GetChaptersOrdered())
            {
                foreach (var scene in _projectService.GetScenesForChapter(chapter.Guid))
                {
                    var range = StoryDateResolver.Resolve(scene, chapter, acts);
                    if (range?.Start is { } s && TryParseDate(s, out var d))
                    {
                        if (d < earliest) { earliest = d; found = true; }
                    }
                }
            }
            if (found) return earliest;
        }

        return DateTime.Today;
    }

    public void Refresh()
    {
        Days.Clear();
        MonthDays.Clear();
        YearMonths.Clear();
        if (!_projectService.IsProjectLoaded)
        {
            RangeLabel = string.Empty;
            return;
        }

        if (!_anchorInitialized)
        {
            WeekAnchor = ResolveInitialAnchor();
            _anchorInitialized = true;
        }

        if (IsMonthView) { RefreshMonth(); return; }
        if (IsYearView)  { RefreshYear();  return; }

        var weekStart = StartOfWeek(WeekAnchor, DayOfWeek.Monday);
        RangeLabel = $"{weekStart:MMM d}  –  {weekStart.AddDays(6):MMM d, yyyy}";

        var bucket = CollectEvents(weekStart, weekStart.AddDays(6));
        for (int i = 0; i < 7; i++)
        {
            var day = weekStart.AddDays(i);
            var events = bucket.TryGetValue(day, out var list) ? list : new();
            Days.Add(new CalendarDayColumn(day, events, sceneEvent =>
                SceneOpenRequested?.Invoke(sceneEvent.ChapterGuid, sceneEvent.SceneId)));
        }
    }

    private void RefreshMonth()
    {
        var first = new DateTime(WeekAnchor.Year, WeekAnchor.Month, 1);
        RangeLabel = first.ToString("MMMM yyyy", CultureInfo.CurrentCulture);

        // Bucket events by date for the whole month (and a few days on each
        // side so the 6-row grid is populated).
        var gridStart = StartOfWeek(first, DayOfWeek.Monday);
        var gridEnd = gridStart.AddDays(42); // 6 weeks
        var bucket = CollectEvents(gridStart, gridEnd.AddDays(-1));

        for (int i = 0; i < 42; i++)
        {
            var day = gridStart.AddDays(i);
            var events = bucket.TryGetValue(day, out var list) ? list : new();
            MonthDays.Add(new CalendarMonthDay(day, day.Month == first.Month, events,
                ev => SceneOpenRequested?.Invoke(ev.ChapterGuid, ev.SceneId),
                jumpDate => { WeekAnchor = jumpDate; ViewMode = "Week"; }));
        }
    }

    private void RefreshYear()
    {
        RangeLabel = WeekAnchor.Year.ToString();
        var jan = new DateTime(WeekAnchor.Year, 1, 1);
        var dec = new DateTime(WeekAnchor.Year, 12, 31);
        var bucket = CollectEvents(jan, dec);

        for (int m = 1; m <= 12; m++)
        {
            var monthStart = new DateTime(WeekAnchor.Year, m, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var monthEvents = new List<CalendarSceneEvent>();
            var seen = new HashSet<string>(); // dedup multi-day scenes per month
            for (var d = monthStart; d <= monthEnd; d = d.AddDays(1))
            {
                if (!bucket.TryGetValue(d, out var list)) continue;
                foreach (var ev in list)
                    if (seen.Add(ev.SceneId)) monthEvents.Add(ev);
            }

            YearMonths.Add(new CalendarYearMonth(monthStart, monthEvents,
                ev => SceneOpenRequested?.Invoke(ev.ChapterGuid, ev.SceneId),
                () => { WeekAnchor = monthStart; ViewMode = "Month"; }));
        }
    }

    private Dictionary<DateTime, List<CalendarSceneEvent>> CollectEvents(DateTime fromDate, DateTime toDate)
    {
        var bucket = new Dictionary<DateTime, List<CalendarSceneEvent>>();
        var acts = _projectService.ActiveBook?.Acts;
        foreach (var chapter in _projectService.GetChaptersOrdered())
        {
            foreach (var scene in _projectService.GetScenesForChapter(chapter.Guid))
            {
                var range = StoryDateResolver.Resolve(scene, chapter, acts);
                if (range == null || string.IsNullOrWhiteSpace(range.Start)) continue;
                if (!TryParseDate(range.Start, out var startDate)) continue;
                var endDate = TryParseDate(range.End, out var ed) ? ed : startDate;
                var startTime = ParseTime(range.StartTime);
                var endTime = ParseTime(range.EndTime);

                for (var d = startDate.Date; d <= endDate.Date; d = d.AddDays(1))
                {
                    if (d < fromDate.Date || d > toDate.Date) continue;
                    if (!bucket.TryGetValue(d, out var list))
                    {
                        list = [];
                        bucket[d] = list;
                    }
                    list.Add(new CalendarSceneEvent
                    {
                        ChapterGuid = chapter.Guid,
                        SceneId = scene.Id,
                        Title = scene.Title,
                        ChapterTitle = chapter.Title,
                        StartHour = startTime?.Hours ?? -1,
                        StartMinute = startTime?.Minutes ?? 0,
                        EndHour = endTime?.Hours ?? -1,
                        EndMinute = endTime?.Minutes ?? 0,
                        AllDay = startTime == null,
                        Synopsis = scene.Synopsis ?? string.Empty,
                        Note = range.Note ?? string.Empty,
                    });
                }
            }
        }
        return bucket;
    }

    private static DateTime StartOfWeek(DateTime dt, DayOfWeek start)
    {
        var diff = (7 + (dt.DayOfWeek - start)) % 7;
        return dt.AddDays(-1 * diff).Date;
    }

    private static bool TryParseDate(string raw, out DateTime dt)
    {
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeLocal, out dt)
            || DateTime.TryParse(raw, CultureInfo.CurrentCulture,
                   DateTimeStyles.AssumeLocal, out dt);
    }

    private static TimeSpan? ParseTime(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (TimeSpan.TryParseExact(raw.Trim(), new[] { @"hh\:mm", @"h\:mm", @"hh\:mm\:ss" }, CultureInfo.InvariantCulture, out var ts))
            return ts;
        if (TimeSpan.TryParse(raw.Trim(), out ts)) return ts;
        return null;
    }
}

public sealed class CalendarMonthDay
{
    public CalendarMonthDay(DateTime day, bool inMonth, List<CalendarSceneEvent> events,
        Action<CalendarSceneEvent> onOpenEvent, Action<DateTime> onJump)
    {
        Day = day;
        InMonth = inMonth;
        DayNumber = day.Day.ToString();
        IsToday = day.Date == DateTime.Today;
        VisibleEvents = events.Take(3).ToList();
        foreach (var e in VisibleEvents) e.OnOpen = onOpenEvent;
        OverflowCount = Math.Max(0, events.Count - VisibleEvents.Count);
        HasOverflow = OverflowCount > 0;
        OverflowLabel = OverflowCount > 0 ? $"+{OverflowCount} more" : string.Empty;
        _onJump = onJump;
        JumpCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => _onJump(Day));
    }

    private readonly Action<DateTime> _onJump;
    public DateTime Day { get; }
    public bool InMonth { get; }
    public bool IsToday { get; }
    public string DayNumber { get; }
    public List<CalendarSceneEvent> VisibleEvents { get; }
    public int OverflowCount { get; }
    public bool HasOverflow { get; }
    public string OverflowLabel { get; }
    public System.Windows.Input.ICommand JumpCommand { get; }
}

public sealed class CalendarYearMonth
{
    public CalendarYearMonth(DateTime firstOfMonth, List<CalendarSceneEvent> events,
        Action<CalendarSceneEvent> onOpenEvent, Action onJump)
    {
        Month = firstOfMonth;
        Label = firstOfMonth.ToString("MMMM", System.Globalization.CultureInfo.CurrentCulture);
        Events = events;
        foreach (var e in Events) e.OnOpen = onOpenEvent;
        EventCount = events.Count;
        EventCountLabel = events.Count == 0 ? "—" : $"{events.Count} scene" + (events.Count == 1 ? "" : "s");
        JumpCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(onJump);
    }

    public DateTime Month { get; }
    public string Label { get; }
    public int EventCount { get; }
    public string EventCountLabel { get; }
    public List<CalendarSceneEvent> Events { get; }
    public System.Windows.Input.ICommand JumpCommand { get; }
}

public sealed class CalendarDayColumn
{
    public CalendarDayColumn(DateTime day, List<CalendarSceneEvent> events, Action<CalendarSceneEvent> onOpen)
    {
        Day = day;
        DayLabel = day.ToString("ddd d", CultureInfo.CurrentCulture);
        IsToday = day.Date == DateTime.Today;
        AllDayEvents = events.Where(e => e.AllDay).ToList();
        TimedEvents = events.Where(e => !e.AllDay).Select(e => new CalendarTimedEvent(e, onOpen)).ToList();
        foreach (var ae in AllDayEvents) ae.OnOpen = onOpen;
        LayoutOverlaps();
    }

    public DateTime Day { get; }
    public string DayLabel { get; }
    public bool IsToday { get; }
    public List<CalendarSceneEvent> AllDayEvents { get; }
    public List<CalendarTimedEvent> TimedEvents { get; }
    public int[] HoursOfDay { get; } = Enumerable.Range(0, 24).ToArray();

    /// <summary>Splits overlapping events into side-by-side columns. Positions
    /// stored as fractions of the day-column width (0..1) and resolved to
    /// pixels in XAML via MultiplyConverter against the Canvas's ActualWidth.</summary>
    private void LayoutOverlaps()
    {
        if (TimedEvents.Count == 0) return;

        var sorted = TimedEvents
            .Select(t => new { Item = t, Start = t.TopPx, End = t.TopPx + t.HeightPx })
            .OrderBy(x => x.Start)
            .ToList();

        var colEnds = new List<double>();
        var groupAssignments = new List<(CalendarTimedEvent Item, int Col)>();
        var groupStartIdx = 0;

        void FlushGroup(int endIdx)
        {
            if (endIdx <= groupStartIdx) return;
            int cols = 0;
            for (int i = groupStartIdx; i < endIdx; i++)
                if (groupAssignments[i].Col + 1 > cols) cols = groupAssignments[i].Col + 1;
            var widthFrac = 1.0 / cols;
            for (int i = groupStartIdx; i < endIdx; i++)
            {
                var (item, col) = groupAssignments[i];
                item.LeftFraction = col * widthFrac;
                item.WidthFraction = widthFrac;
            }
            groupStartIdx = endIdx;
        }

        double currentMaxEnd = double.NegativeInfinity;
        foreach (var entry in sorted)
        {
            if (entry.Start >= currentMaxEnd)
            {
                FlushGroup(groupAssignments.Count);
                colEnds.Clear();
                currentMaxEnd = entry.End;
            }
            else
            {
                if (entry.End > currentMaxEnd) currentMaxEnd = entry.End;
            }

            int placedCol = -1;
            for (int c = 0; c < colEnds.Count; c++)
            {
                if (colEnds[c] <= entry.Start)
                {
                    placedCol = c;
                    colEnds[c] = entry.End;
                    break;
                }
            }
            if (placedCol < 0)
            {
                placedCol = colEnds.Count;
                colEnds.Add(entry.End);
            }
            groupAssignments.Add((entry.Item, placedCol));
        }
        FlushGroup(groupAssignments.Count);
    }
}

public partial class CalendarSceneEvent : ObservableObject
{
    public string ChapterGuid { get; init; } = string.Empty;
    public string SceneId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string ChapterTitle { get; init; } = string.Empty;
    public string Synopsis { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
    public int StartHour { get; init; }
    public int StartMinute { get; init; }
    public int EndHour { get; init; }
    public int EndMinute { get; init; }
    public bool AllDay { get; init; }

    public Action<CalendarSceneEvent>? OnOpen { get; set; }

    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    public string Tooltip
    {
        get
        {
            var sb = new System.Text.StringBuilder(Title);
            if (HasNote) sb.Append('\n').Append(Note);
            if (!string.IsNullOrWhiteSpace(Synopsis)) sb.Append('\n').Append(Synopsis);
            return sb.ToString();
        }
    }

    [RelayCommand]
    private void Open() => OnOpen?.Invoke(this);
}

public sealed class CalendarTimedEvent
{
    public CalendarTimedEvent(CalendarSceneEvent ev, Action<CalendarSceneEvent> onOpen)
    {
        Event = ev;
        Event.OnOpen = onOpen;
        // 48px per hour, anchored to top.
        TopPx = (ev.StartHour + ev.StartMinute / 60.0) * 48;
        var endHour = ev.EndHour >= 0 ? ev.EndHour + ev.EndMinute / 60.0 : (ev.StartHour + 1);
        HeightPx = Math.Max(20, (endHour - (ev.StartHour + ev.StartMinute / 60.0)) * 48);
        LeftFraction = 0.0;
        WidthFraction = 1.0;
    }

    public CalendarSceneEvent Event { get; }
    public double TopPx { get; }
    public double HeightPx { get; }
    /// <summary>Horizontal position as fraction of column width (0..1).</summary>
    public double LeftFraction { get; set; }
    /// <summary>Width as fraction of column width (0..1).</summary>
    public double WidthFraction { get; set; }
}
