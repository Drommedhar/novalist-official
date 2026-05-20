using System.Collections.ObjectModel;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

[Collection("Avalonia")]
public class TimelineViewModelTests
{
    private static (TimelineViewModel Vm, IProjectService Proj, ProjectSettings Settings) Build(
        (ChapterData Chapter, SceneData[] Scenes)[]? chapters = null,
        IEnumerable<TimelineManualEvent>? manual = null,
        string viewMode = "vertical", string zoom = "month")
    {
        var proj = Substitute.For<IProjectService>();
        var settings = new ProjectSettings();
        settings.Timeline.ViewMode = viewMode;
        settings.Timeline.ZoomLevel = zoom;
        if (manual != null) settings.Timeline.ManualEvents.AddRange(manual);
        proj.ProjectSettings.Returns(settings);
        proj.SaveProjectSettingsAsync().Returns(Task.CompletedTask);
        proj.GetChaptersOrdered().Returns((chapters ?? []).Select(c => c.Chapter).ToList());
        foreach (var (ch, scenes) in chapters ?? [])
            proj.GetScenesForChapter(ch.Guid).Returns(scenes.ToList());
        return (new TimelineViewModel(proj), proj, settings);
    }

    private static ChapterData Chap(string guid, string title, string act = "", string date = "", int order = 1)
        => new() { Guid = guid, Title = title, Act = act, Date = date, Order = order };

    private static SceneData Scene(string id, string title, string date = "")
        => new() { Id = id, Title = title, Date = date };

    private static TimelineManualEvent Manual(string id, string title, string date, string cat = "plot",
        string chapter = "", IEnumerable<string>? chars = null, IEnumerable<string>? locs = null)
        => new()
        {
            Id = id, Title = title, Date = date, CategoryId = cat, LinkedChapterGuid = chapter,
            Characters = (chars ?? []).ToList(), Locations = (locs ?? []).ToList(),
        };

    // BuildAndRender runs synchronously via any filter command.
    private static void Render(TimelineViewModel vm) => vm.ApplyFilterCommand.Execute(null);

    [AvaloniaFact]
    public void Refresh_LoadsSettings_BuildsSources() => Task.Run(async () =>
    {
        var (vm, _, _) = Build(viewMode: "horizontal", zoom: "year");
        await vm.RefreshAsync();
        Assert.Equal("horizontal", vm.ViewMode);
        Assert.Equal("year", vm.ZoomLevel);
        Assert.Equal(4, vm.AvailableSources.Count);
    }).GetAwaiter().GetResult();

    [AvaloniaFact]
    public void BuildEvents_Act_Chapter_Scene_Manual()
    {
        var ch = Chap("c1", "Chapter", act: "Act I", date: "2026-01-01", order: 1);
        var scenes = new[] { Scene("s1", "Scene", "2026-01-02") };
        var (vm, _, _) = Build(
            chapters: [(ch, scenes)],
            manual: [Manual("m1", "Battle", "2026-01-03", chars: ["Bob"], locs: ["Castle"])]);
        Render(vm);

        var all = vm.Groups.SelectMany(g => g.Events).ToList();
        Assert.Contains(all, e => e.Source == "act" && e.Title == "Act I");
        Assert.Contains(all, e => e.Source == "chapter" && e.Title == "Chapter");
        Assert.Contains(all, e => e.Source == "scene");
        Assert.Contains(all, e => e.Source == "manual" && e.IsManual);
        Assert.True(vm.HasEvents);
        Assert.Contains("Bob", vm.AvailableCharacters);
        Assert.Contains("Castle", vm.AvailableLocations);

        vm.Refresh(); // fire-and-forget wrapper over RefreshAsync
    }

    [AvaloniaFact]
    public void Scene_FallsBackToChapterDate()
    {
        var ch = Chap("c1", "Chapter", date: "2026-05-01");
        var scenes = new[] { Scene("s1", "NoDate", "") }; // no scene date -> uses chapter date
        var (vm, _, _) = Build(chapters: [(ch, scenes)]);
        Render(vm);
        Assert.Contains(vm.Groups.SelectMany(g => g.Events), e => e.Source == "scene" && e.SortDate != null);
    }

    [AvaloniaFact]
    public void Filters_Character_Location_Source()
    {
        var (vm, _, _) = Build(manual:
        [
            Manual("m1", "A", "2026-01-01", chars: ["Bob"]),
            Manual("m2", "B", "2026-01-02", locs: ["Town"]),
        ]);
        Render(vm);
        Assert.Equal(2, vm.Groups.SelectMany(g => g.Events).Count());

        vm.FilterCharacter = "Bob"; // OnFilterCharacterChanged -> render
        Assert.Single(vm.Groups.SelectMany(g => g.Events));
        vm.FilterCharacter = null;

        vm.FilterLocation = "Town";
        Assert.Single(vm.Groups.SelectMany(g => g.Events));
        vm.FilterLocation = null;

        vm.FilterSource = "manual";
        Assert.Equal(2, vm.Groups.SelectMany(g => g.Events).Count());
        vm.FilterSource = "act";
        Assert.Empty(vm.Groups.SelectMany(g => g.Events));
    }

    [AvaloniaFact]
    public async Task ToggleViewMode_And_CycleZoom_Persist()
    {
        var (vm, proj, settings) = Build();
        await vm.ToggleViewModeCommand.ExecuteAsync(null);
        Assert.Equal("horizontal", vm.ViewMode);
        Assert.True(vm.IsHorizontal);
        Assert.Equal("horizontal", settings.Timeline.ViewMode);

        Assert.Equal("month", vm.ZoomLevel);
        await vm.CycleZoomCommand.ExecuteAsync(null);
        Assert.Equal("day", vm.ZoomLevel); // month -> day
        await vm.CycleZoomCommand.ExecuteAsync(null);
        Assert.Equal("year", vm.ZoomLevel); // day -> year
        await proj.Received().SaveProjectSettingsAsync();
    }

    [AvaloniaFact]
    public void ClearFilters_ResetsAll()
    {
        var (vm, _, _) = Build();
        vm.FilterCharacter = "x";
        vm.SelectedSourceFilter = new SourceFilterItem { Value = "manual" };
        vm.ClearFiltersCommand.Execute(null);
        Assert.Null(vm.FilterCharacter);
        Assert.Null(vm.FilterLocation);
        Assert.Null(vm.FilterSource);
        Assert.Null(vm.SelectedSourceFilter);
    }

    [AvaloniaFact]
    public void ShowAddForm_InitializesForm()
    {
        var (vm, _, _) = Build(chapters: [(Chap("c1", "Ch"), [])]);
        vm.ShowAddFormCommand.Execute(null);
        Assert.True(vm.IsFormOpen);
        Assert.Equal("plot", vm.FormCategoryId);
        Assert.NotEmpty(vm.AvailableCategories);
        Assert.Single(vm.AvailableChapters);
    }

    [AvaloniaFact]
    public async Task SaveForm_AddsNew_GuardsEmptyTitle()
    {
        var (vm, proj, settings) = Build();
        vm.ShowAddFormCommand.Execute(null);
        await vm.SaveFormCommand.ExecuteAsync(null); // empty title -> guard
        Assert.Empty(settings.Timeline.ManualEvents);

        vm.FormTitle = "New Event";
        vm.FormDate = "2026-02-02";
        await vm.SaveFormCommand.ExecuteAsync(null);
        Assert.Single(settings.Timeline.ManualEvents);
        Assert.False(vm.IsFormOpen);
        await proj.Received().SaveProjectSettingsAsync();
    }

    [AvaloniaFact]
    public async Task EditEvent_LoadsForm_SaveUpdates()
    {
        var (vm, _, settings) = Build(manual: [Manual("m1", "Orig", "2026-01-01")]);
        vm.EditEventCommand.Execute("manual-m1"); // prefix stripped
        Assert.True(vm.IsFormOpen);
        Assert.Equal("Orig", vm.FormTitle);

        vm.FormTitle = "Updated";
        await vm.SaveFormCommand.ExecuteAsync(null);
        Assert.Single(settings.Timeline.ManualEvents);
        Assert.Equal("Updated", settings.Timeline.ManualEvents[0].Title);

        vm.EditEventCommand.Execute("ghost"); // not found -> no-op (form stays as-is)
    }

    [AvaloniaFact]
    public async Task DeleteEvent_RemovesOrNoOp()
    {
        var (vm, proj, settings) = Build(manual: [Manual("m1", "X", "2026-01-01")]);
        await vm.DeleteEventCommand.ExecuteAsync("ghost"); // not found
        Assert.Single(settings.Timeline.ManualEvents);
        await vm.DeleteEventCommand.ExecuteAsync("manual-m1");
        Assert.Empty(settings.Timeline.ManualEvents);
    }

    [AvaloniaFact]
    public void CancelForm_Closes()
    {
        var (vm, _, _) = Build();
        vm.ShowAddFormCommand.Execute(null);
        vm.CancelFormCommand.Execute(null);
        Assert.False(vm.IsFormOpen);
    }

    [AvaloniaFact]
    public void Jump_Pan_ScrollToday()
    {
        var (vm, _, _) = Build(zoom: "month");
        string? scrolled = null;
        vm.ScrollToGroupRequested += k => scrolled = k;

        vm.ScrollToTodayCommand.Execute(null);
        Assert.Equal(DateTime.Today, vm.AnchorDate);
        Assert.NotNull(scrolled);

        var before = vm.AnchorDate!.Value;
        vm.PanNextCommand.Execute(null);
        Assert.Equal(before.AddMonths(1), vm.AnchorDate);
        vm.PanPreviousCommand.Execute(null);
        Assert.Equal(before, vm.AnchorDate);
    }

    [AvaloniaFact]
    public void Pan_RespectsZoom()
    {
        var (vm, _, _) = Build();
        vm.ZoomLevel = "year"; // ZoomLevel is only loaded from settings via RefreshAsync; set directly
        vm.ScrollToTodayCommand.Execute(null);
        var d = vm.AnchorDate!.Value;
        vm.PanNextCommand.Execute(null);
        Assert.Equal(d.AddYears(1), vm.AnchorDate);

        var (vm2, _, _) = Build();
        vm2.ZoomLevel = "day";
        vm2.ScrollToTodayCommand.Execute(null);
        var d2 = vm2.AnchorDate!.Value;
        vm2.PanNextCommand.Execute(null);
        Assert.Equal(d2.AddDays(1), vm2.AnchorDate);
    }

    [AvaloniaFact]
    public void JumpFlyout_ConfirmValidAndInvalid()
    {
        var (vm, _, _) = Build();
        vm.OpenJumpFlyoutCommand.Execute(null);
        Assert.True(vm.IsJumpFlyoutOpen);
        Assert.False(string.IsNullOrEmpty(vm.JumpDateInput));

        vm.JumpDateInput = "2027-07-07";
        vm.ConfirmJumpCommand.Execute(null);
        Assert.Equal(new DateTime(2027, 7, 7), vm.AnchorDate);
        Assert.False(vm.IsJumpFlyoutOpen);

        vm.OpenJumpFlyoutCommand.Execute(null);
        vm.JumpDateInput = "bad";
        vm.ConfirmJumpCommand.Execute(null); // invalid -> no anchor change, flyout closes
        Assert.False(vm.IsJumpFlyoutOpen);

        vm.OpenJumpFlyoutCommand.Execute(null);
        vm.CancelJumpCommand.Execute(null);
        Assert.False(vm.IsJumpFlyoutOpen);
    }

    [AvaloniaFact]
    public async Task ApplyStructureTemplate_Guards_AndAdds()
    {
        var (vm, _, settings) = Build();
        await vm.ApplyStructureTemplateCommand.ExecuteAsync(null); // null -> no-op
        await vm.ApplyStructureTemplateCommand.ExecuteAsync("ghost-id"); // unknown -> no-op
        Assert.Empty(settings.Timeline.ManualEvents);

        var tpl = StoryStructureTemplates.All.First();
        await vm.ApplyStructureTemplateCommand.ExecuteAsync(tpl.Id);
        Assert.Equal(tpl.Beats.Count, settings.Timeline.ManualEvents.Count);
        Assert.NotEmpty(vm.AvailableStructureTemplates);
    }

    [AvaloniaFact]
    public void OpenLinkedChapter_Branches()
    {
        var ch = Chap("c1", "Ch");
        var (vm, _, _) = Build(chapters: [(ch, new[] { Scene("s1", "S") })]);
        (ChapterData, SceneData)? opened = null;
        vm.SceneOpenRequested += (c, s) => opened = (c, s);

        vm.OpenLinkedChapterCommand.Execute(""); // empty -> no-op
        vm.OpenLinkedChapterCommand.Execute("ghost"); // chapter not found -> no-op
        Assert.Null(opened);
        vm.OpenLinkedChapterCommand.Execute("c1");
        Assert.NotNull(opened);
    }

    [AvaloniaFact]
    public void OpenLinkedChapter_NoScenes_NoOp()
    {
        var ch = Chap("c1", "Ch");
        var (vm, _, _) = Build(chapters: [(ch, [])]); // no scenes
        bool fired = false;
        vm.SceneOpenRequested += (_, _) => fired = true;
        vm.OpenLinkedChapterCommand.Execute("c1");
        Assert.False(fired);
    }

    [AvaloniaFact]
    public void ExportOutline_Guards_AndWrites() => Task.Run(async () =>
    {
        // Real ExportService writes a file (async IO that yields); Task.Run contains
        // the yield so the shared Avalonia-collection runner thread isn't bounced.
        var (vm, _, _) = Build();
        await vm.ExportOutlineCommand.ExecuteAsync(null); // dialog null -> no-op

        vm.ShowSaveFileDialog = (_, _) => Task.FromResult<string?>(null);
        await vm.ExportOutlineCommand.ExecuteAsync(null); // cancelled -> no-op

        var dir = Path.Combine(Path.GetTempPath(), "nov_tl_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var outPath = Path.Combine(dir, "outline.md");
            vm.ShowSaveFileDialog = (_, _) => Task.FromResult<string?>(outPath);
            await vm.ExportOutlineCommand.ExecuteAsync(null);
            Assert.True(File.Exists(outPath));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }).GetAwaiter().GetResult();

    [AvaloniaFact]
    public void Labels_And_SelectionHandlers()
    {
        var (vm, _, _) = Build();
        Assert.False(string.IsNullOrEmpty(vm.ViewModeLabel));
        Assert.False(string.IsNullOrEmpty(vm.ZoomLabel));
        Assert.True(vm.IsVertical);

        vm.ZoomLevel = "year";
        Assert.False(string.IsNullOrEmpty(vm.ZoomLabel));
        vm.ZoomLevel = "day";
        Assert.False(string.IsNullOrEmpty(vm.ZoomLabel));

        vm.SelectedSourceFilter = new SourceFilterItem { Value = "scene" };
        Assert.Equal("scene", vm.FilterSource);
        vm.SelectedSourceFilter = null;
        Assert.Null(vm.FilterSource);

        vm.ShowAddFormCommand.Execute(null);
        vm.SelectedCategory = new CategoryItem { Value = "world" };
        Assert.Equal("world", vm.FormCategoryId);
    }

    [AvaloniaFact]
    public void ZoomChanged_UpdatesHighlightWhenAnchored()
    {
        var (vm, _, _) = Build(zoom: "month");
        vm.ScrollToTodayCommand.Execute(null); // sets AnchorDate
        vm.ZoomLevel = "year"; // OnZoomLevelChanged -> recompute highlight
        Assert.False(string.IsNullOrEmpty(vm.HighlightedGroupKey));
    }

    // ── Static helpers ──────────────────────────────────────────────
    [AvaloniaTheory]
    [InlineData("2026-03-04", true)]
    [InlineData("2026-03", true)]
    [InlineData("2026", true)]
    [InlineData("4.3.2026", true)]
    [InlineData("March 4 2026", true)] // fallback
    [InlineData("", false)]
    [InlineData("nonsense!!", false)]
    public void ParseDate_Formats(string input, bool expectParsed)
    {
        var result = TimelineViewModel.ParseDate(input);
        Assert.Equal(expectParsed, result.HasValue);
    }

    [AvaloniaFact]
    public void GroupKey_And_Label_And_Format()
    {
        var d = new DateTime(2026, 3, 4);
        Assert.Equal("2026", TimelineViewModel.GroupKey(d, "year"));
        Assert.Equal("2026-03-04", TimelineViewModel.GroupKey(d, "day"));
        Assert.Equal("2026-03", TimelineViewModel.GroupKey(d, "month"));
        Assert.Equal("no-date", TimelineViewModel.GroupKey(null, "month"));

        Assert.Equal("2026", TimelineViewModel.GroupLabel("2026", "year"));
        Assert.Equal("Mar 4, 2026", TimelineViewModel.GroupLabel("2026-03-04", "day"));
        Assert.Equal("Mar 2026", TimelineViewModel.GroupLabel("2026-03", "month"));
        Assert.Equal("???", TimelineViewModel.GroupLabel("no-date", "month"));

        Assert.Equal("2026", TimelineViewModel.FormatDateLabel(d, "", "year"));
        Assert.Contains("Mar", TimelineViewModel.FormatDateLabel(d, "", "day"));
        Assert.Contains("Mar", TimelineViewModel.FormatDateLabel(d, "", "month"));
        Assert.Equal("raw", TimelineViewModel.FormatDateLabel(null, "raw", "month"));
        Assert.Equal("???", TimelineViewModel.FormatDateLabel(null, "", "month"));
    }

    [AvaloniaFact]
    public void EventItem_Flags_AndFormattedDate()
    {
        var ev = new TimelineEventItem
        {
            Source = "act",
            Description = "d",
            ChapterGuid = "c1",
            SortDate = new DateTime(2026, 1, 1),
            DateStr = "2026-01-01",
            Characters = ["Bob"],
            Locations = ["Town"],
        };
        Assert.True(ev.HasDescription);
        Assert.True(ev.HasCharacters);
        Assert.True(ev.HasLocations);
        Assert.True(ev.HasMeta);
        Assert.True(ev.HasChapterLink);
        Assert.True(ev.IsAct);
        Assert.False(ev.HasNoDate);
        Assert.False(string.IsNullOrEmpty(ev.FormattedDate("month")));

        var empty = new TimelineEventItem();
        Assert.True(empty.HasNoDate);
        Assert.False(empty.HasMeta);
    }

    [AvaloniaFact]
    public void DisplayModels_ToString()
    {
        Assert.Equal("L", new SourceFilterItem { Value = "v", Label = "L" }.ToString());
        Assert.Equal("C", new CategoryItem { Value = "v", Label = "C" }.ToString());
        var g = new TimelineGroup { Key = "k", Label = "lbl" };
        Assert.Equal("k", g.Key);
    }
}
