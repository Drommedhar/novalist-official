using System.Globalization;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

public class CalendarViewModelTests
{
    private static SceneData Scene(string id, string title, string date, string start = "", string end = "", string endTime = "", string note = "", string synopsis = "")
        => new()
        {
            Id = id,
            Title = title,
            DateRange = new StoryDateRange { Start = date, End = end, StartTime = start, EndTime = endTime, Note = note },
            Synopsis = synopsis,
        };

    private static (CalendarViewModel Vm, IProjectService Proj) Build(
        string? anchor = "2026-06-15", bool loaded = true,
        (ChapterData Chapter, SceneData[] Scenes)[]? chapters = null)
    {
        var proj = Substitute.For<IProjectService>();
        var settings = new ProjectSettings { CalendarAnchor = anchor };
        proj.ProjectSettings.Returns(settings);
        proj.IsProjectLoaded.Returns(loaded);
        proj.SaveProjectSettingsAsync().Returns(Task.CompletedTask);
        proj.ActiveBook.Returns(new BookData());
        var chapterList = (chapters ?? []).Select(c => c.Chapter).ToList();
        proj.GetChaptersOrdered().Returns(chapterList);
        foreach (var (ch, scenes) in chapters ?? [])
            proj.GetScenesForChapter(ch.Guid).Returns(scenes.ToList());
        return (new CalendarViewModel(proj), proj);
    }

    private static ChapterData Chap(string guid = "c1", string title = "Ch", string act = "", string date = "")
        => new() { Guid = guid, Title = title, Act = act, Date = date, Order = 1 };

    [Fact]
    public void NotLoaded_Refresh_Empty()
    {
        var (vm, _) = Build(loaded: false);
        vm.Refresh();
        Assert.Empty(vm.Days);
        Assert.Empty(vm.MonthDays);
        Assert.Empty(vm.YearMonths);
        Assert.Equal(string.Empty, vm.RangeLabel);
    }

    [Fact]
    public void Week_Refresh_BuildsSevenDays_WithEvents()
    {
        var ch = Chap();
        var scenes = new[]
        {
            Scene("s1", "Morning", "2026-06-15", start: "09:00", endTime: "10:30", note: "n", synopsis: "syn"),
            Scene("s2", "AllDay", "2026-06-16"),
        };
        var (vm, _) = Build(chapters: [(ch, scenes)]);
        vm.Refresh();

        Assert.Equal(7, vm.Days.Count);
        Assert.False(string.IsNullOrEmpty(vm.RangeLabel));
        var allEvents = vm.Days.SelectMany(d => d.AllDayEvents).Concat(vm.Days.SelectMany(d => d.TimedEvents.Select(t => t.Event))).ToList();
        Assert.Contains(allEvents, e => e.Title == "Morning" && !e.AllDay);
        Assert.Contains(allEvents, e => e.Title == "AllDay" && e.AllDay);
        Assert.True(vm.IsWeekView);
    }

    [Fact]
    public void Navigation_Week_Month_Year()
    {
        var (vm, proj) = Build();
        vm.Refresh();
        var start = vm.WeekAnchor;

        vm.PreviousWeekCommand.Execute(null);
        Assert.Equal(start.AddDays(-7), vm.WeekAnchor);
        vm.NextWeekCommand.Execute(null);
        Assert.Equal(start, vm.WeekAnchor);

        vm.SetMonthViewCommand.Execute(null);
        Assert.True(vm.IsMonthView);
        var m = vm.WeekAnchor;
        vm.NextWeekCommand.Execute(null);
        Assert.Equal(m.AddMonths(1), vm.WeekAnchor);
        vm.PreviousWeekCommand.Execute(null);
        Assert.Equal(m, vm.WeekAnchor);

        vm.SetYearViewCommand.Execute(null);
        Assert.True(vm.IsYearView);
        var y = vm.WeekAnchor;
        vm.NextWeekCommand.Execute(null);
        Assert.Equal(y.AddYears(1), vm.WeekAnchor);

        vm.TodayCommand.Execute(null);
        Assert.Equal(DateTime.Today, vm.WeekAnchor);

        vm.SetWeekViewCommand.Execute(null);
        Assert.True(vm.IsWeekView);
    }

    [Fact]
    public async Task SaveAnchor_PersistsOnlyWhenLoaded()
    {
        var (vm, proj) = Build();
        vm.Refresh();
        vm.TodayCommand.Execute(null); // triggers SaveAnchor
        await Task.Delay(10);
        await proj.Received().SaveProjectSettingsAsync();
        Assert.Equal(DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), proj.ProjectSettings.CalendarAnchor);

        var (vm2, proj2) = Build(loaded: false);
        vm2.NextWeekCommand.Execute(null); // SaveAnchor returns early (not loaded)
        await proj2.DidNotReceive().SaveProjectSettingsAsync();
    }

    [Fact]
    public void JumpToDate_SetsAnchorAndView()
    {
        var (vm, _) = Build();
        vm.Refresh();
        vm.SetYearViewCommand.Execute(null);
        vm.JumpToDateCommand.Execute(new DateTime(2027, 3, 4)); // year -> month
        Assert.Equal(new DateTime(2027, 3, 4), vm.WeekAnchor);
        Assert.True(vm.IsMonthView);

        vm.JumpToDateCommand.Execute(new DateTime(2028, 1, 2)); // month -> week
        Assert.True(vm.IsWeekView);
    }

    [Fact]
    public void Month_Refresh_42Days_OverflowAndInMonth()
    {
        var ch = Chap();
        // 4 events on the same day -> overflow (>3 visible)
        var day = "2026-06-10";
        var scenes = Enumerable.Range(0, 4).Select(i => Scene($"s{i}", $"E{i}", day)).ToArray();
        var (vm, _) = Build(anchor: "2026-06-15", chapters: [(ch, scenes)]);
        vm.SetMonthViewCommand.Execute(null); // OnViewModeChanged -> Refresh -> RefreshMonth

        Assert.Equal(42, vm.MonthDays.Count);
        Assert.Contains(vm.MonthDays, d => d.InMonth);
        Assert.Contains(vm.MonthDays, d => !d.InMonth); // grid spills into adjacent months
        var overflowDay = vm.MonthDays.First(d => d.HasOverflow);
        Assert.Equal(3, overflowDay.VisibleEvents.Count);
        Assert.True(overflowDay.OverflowCount >= 1);
        Assert.Contains("more", overflowDay.OverflowLabel);
    }

    [Fact]
    public void Year_Refresh_12Months_DedupAndLabels()
    {
        var ch = Chap();
        // Multi-day scene spanning days in June -> deduped to one entry that month.
        var scenes = new[] { Scene("s1", "Span", "2026-06-10", end: "2026-06-12") };
        var (vm, _) = Build(anchor: "2026-06-15", chapters: [(ch, scenes)]);
        vm.SetYearViewCommand.Execute(null);

        Assert.Equal(12, vm.YearMonths.Count);
        var june = vm.YearMonths.First(m => m.Month.Month == 6);
        Assert.Equal(1, june.EventCount); // deduped across the multi-day span
        Assert.Contains("scene", june.EventCountLabel);
        var empty = vm.YearMonths.First(m => m.Month.Month == 1);
        Assert.Equal("—", empty.EventCountLabel);
    }

    [Fact]
    public async Task Reschedule_GuardsAndPersists()
    {
        var (vm, proj) = Build(chapters: [(Chap(), new[] { Scene("s1", "S", "2026-06-15") })]);
        vm.Refresh();
        await vm.RescheduleSceneAsync("", "s1", DateTime.Today); // empty guid -> no-op
        await proj.DidNotReceive().SetSceneDateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());

        await vm.RescheduleSceneAsync("c1", "s1", new DateTime(2026, 6, 20));
        await proj.Received().SetSceneDateAsync("c1", "s1", "2026-06-20");
    }

    [Fact]
    public void ResolveInitialAnchor_EarliestScene_WhenNoSavedAnchor()
    {
        var ch = Chap();
        var scenes = new[]
        {
            Scene("s1", "Late", "2026-08-01"),
            Scene("s2", "Early", "2026-05-01"),
        };
        var (vm, _) = Build(anchor: null, chapters: [(ch, scenes)]);
        vm.Refresh(); // resolves anchor to earliest scene date
        Assert.Equal(2026, vm.WeekAnchor.Year);
        Assert.Equal(5, vm.WeekAnchor.Month);
    }

    [Fact]
    public void ResolveInitialAnchor_Today_WhenNoScenes()
    {
        var (vm, _) = Build(anchor: null);
        vm.Refresh();
        Assert.Equal(DateTime.Today, vm.WeekAnchor.Date);
    }

    [Fact]
    public void SceneEvent_TooltipHasNoteAndSynopsis_OpenFires()
    {
        var ch = Chap();
        var scenes = new[] { Scene("s1", "T", "2026-06-15", note: "NOTE", synopsis: "SYN") };
        var (vm, _) = Build(chapters: [(ch, scenes)]);
        (string Chap, string Scene)? opened = null;
        vm.SceneOpenRequested += (c, s) => opened = (c, s);
        vm.Refresh();

        var ev = vm.Days.SelectMany(d => d.AllDayEvents).First(e => e.Title == "T");
        Assert.True(ev.HasNote);
        Assert.Contains("NOTE", ev.Tooltip);
        Assert.Contains("SYN", ev.Tooltip);
        ev.OpenCommand.Execute(null);
        Assert.Equal(("c1", "s1"), opened);
    }

    [Fact]
    public void DayColumn_TimedOverlap_SplitsColumns()
    {
        var ch = Chap();
        var scenes = new[]
        {
            Scene("a", "A", "2026-06-15", start: "09:00", endTime: "11:00"),
            Scene("b", "B", "2026-06-15", start: "10:00", endTime: "12:00"), // overlaps A
            Scene("c", "C", "2026-06-15", start: "13:00", endTime: "14:00"), // separate group
        };
        var (vm, _) = Build(chapters: [(ch, scenes)]);
        vm.Refresh();
        var col = vm.Days.First(d => d.TimedEvents.Count > 0);
        var a = col.TimedEvents.First(t => t.Event.Title == "A");
        var b = col.TimedEvents.First(t => t.Event.Title == "B");
        // Overlapping events get side-by-side fractions (<1 width).
        Assert.True(a.WidthFraction < 1.0);
        Assert.True(b.WidthFraction < 1.0);
        Assert.NotEqual(a.LeftFraction, b.LeftFraction);
        Assert.True(a.TopPx >= 0 && a.HeightPx > 0);
    }

    [Fact]
    public void MonthDay_JumpCommand_SwitchesToWeek()
    {
        var (vm, _) = Build();
        vm.SetMonthViewCommand.Execute(null);
        var d = vm.MonthDays.First();
        d.JumpCommand.Execute(null);
        Assert.True(vm.IsWeekView);
        Assert.Equal(d.Day, vm.WeekAnchor);
    }

    [Fact]
    public void YearMonth_JumpCommand_SwitchesToMonth()
    {
        var (vm, _) = Build();
        vm.SetYearViewCommand.Execute(null);
        var ym = vm.YearMonths.First();
        ym.JumpCommand.Execute(null);
        Assert.True(vm.IsMonthView);
        Assert.Equal(ym.Month, vm.WeekAnchor);
    }

    [Theory]
    [InlineData("09:00", false)]
    [InlineData("9:00", false)]
    [InlineData("09:00:30", false)]
    [InlineData("", true)]      // no time -> all day
    [InlineData("garbage", true)] // unparseable -> all day
    public void ParseTime_Variants_DriveAllDay(string startTime, bool expectedAllDay)
    {
        var ch = Chap();
        var scenes = new[] { Scene("s1", "S", "2026-06-15", start: startTime) };
        var (vm, _) = Build(chapters: [(ch, scenes)]);
        vm.Refresh();
        var ev = vm.Days.SelectMany(d => d.AllDayEvents.Concat(d.TimedEvents.Select(t => t.Event)))
                        .First(e => e.Title == "S");
        Assert.Equal(expectedAllDay, ev.AllDay);
    }

    [Fact]
    public void InvalidSceneDate_Skipped()
    {
        var ch = Chap();
        var scenes = new[] { Scene("s1", "Bad", "not-a-date") };
        var (vm, _) = Build(chapters: [(ch, scenes)]);
        vm.Refresh();
        Assert.DoesNotContain(vm.Days.SelectMany(d => d.AllDayEvents), e => e.Title == "Bad");
    }

    [Fact]
    public void PreviousWeek_InYearMode_SubtractsYear()
    {
        var (vm, _) = Build();
        vm.SetYearViewCommand.Execute(null);
        var y = vm.WeekAnchor;
        vm.PreviousWeekCommand.Execute(null);
        Assert.Equal(y.AddYears(-1), vm.WeekAnchor);
    }

    [Fact]
    public void Month_EventOpen_FiresSceneOpen()
    {
        var ch = Chap();
        var (vm, _) = Build(anchor: "2026-06-15", chapters: [(ch, new[] { Scene("s1", "E", "2026-06-10") })]);
        (string, string)? opened = null;
        vm.SceneOpenRequested += (c, s) => opened = (c, s);
        vm.SetMonthViewCommand.Execute(null);
        var day = vm.MonthDays.First(d => d.VisibleEvents.Count > 0);
        day.VisibleEvents[0].OpenCommand.Execute(null);
        Assert.Equal(("c1", "s1"), opened);
    }

    [Fact]
    public void Year_EventOpen_FiresSceneOpen()
    {
        var ch = Chap();
        var (vm, _) = Build(anchor: "2026-06-15", chapters: [(ch, new[] { Scene("s1", "E", "2026-06-10") })]);
        (string, string)? opened = null;
        vm.SceneOpenRequested += (c, s) => opened = (c, s);
        vm.SetYearViewCommand.Execute(null);
        var month = vm.YearMonths.First(m => m.Events.Count > 0);
        month.Events[0].OpenCommand.Execute(null);
        Assert.Equal(("c1", "s1"), opened);
    }

    [Fact]
    public void DayColumn_LayoutReusesFreedColumn()
    {
        var ch = Chap();
        var scenes = new[]
        {
            Scene("a", "A", "2026-06-15", start: "09:00", endTime: "10:00"),
            Scene("b", "B", "2026-06-15", start: "09:00", endTime: "11:00"), // overlaps A -> col 1
            Scene("c", "C", "2026-06-15", start: "10:00", endTime: "11:00"), // A freed at 10:00 -> reuse col 0
        };
        var (vm, _) = Build(chapters: [(ch, scenes)]);
        vm.Refresh();
        var col = vm.Days.First(d => d.TimedEvents.Count == 3);
        var a = col.TimedEvents.First(t => t.Event.Title == "A");
        var c = col.TimedEvents.First(t => t.Event.Title == "C");
        Assert.Equal(a.LeftFraction, c.LeftFraction); // C reused A's column
    }

    [Fact]
    public void HoursOfDay_24()
    {
        var (vm, _) = Build();
        Assert.Equal(24, vm.HoursOfDay.Length);
    }
}
