using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Novalist.Desktop.Dialogs;
using Novalist.Desktop.ViewModels;
using Xunit;

namespace Novalist.Desktop.Tests.ViewModels;

[Collection("Avalonia")]
public class DashboardViewModelTests
{
    private static void DoRefresh(DashboardViewModel vm, string? deadline = null, string? coverPath = null, string? coverRel = null, int totalWords = 100, int projectGoal = 1000)
        => vm.Refresh("Proj", totalWords, 3, 9, 5, 4, 12, 500, 200, 500, 40, projectGoal, 10, deadline, coverPath, coverRel);

    [AvaloniaFact]
    public void Refresh_SetsAllBasics_AndDisplays()
    {
        var vm = new DashboardViewModel();
        DoRefresh(vm);
        Assert.Equal("Proj", vm.ProjectName);
        Assert.Equal(100, vm.TotalWords);
        Assert.Equal(3, vm.ChapterCount);
        Assert.False(string.IsNullOrEmpty(vm.TotalWordsDisplay));
        Assert.NotNull(vm.AverageChapterWordsDisplay);
        Assert.False(string.IsNullOrEmpty(vm.ReadingTimeDisplay));
        Assert.False(vm.HasCoverImage);
        Assert.False(vm.HasDeadline);
    }

    [AvaloniaFact]
    public void Author_And_Deadline_Flags()
    {
        var vm = new DashboardViewModel { Author = "Me" };
        Assert.True(vm.HasAuthor);
        vm.Author = "";
        Assert.False(vm.HasAuthor);

        DoRefresh(vm, deadline: "2099-01-01");
        Assert.True(vm.HasDeadline);
        Assert.Equal("2099-01-01", vm.DeadlineDisplay);
        Assert.True(vm.DaysRemaining > 0);
        Assert.True(vm.WordsPerDayNeeded > 0);
        Assert.False(string.IsNullOrEmpty(vm.DaysRemainingLabel));
        Assert.False(string.IsNullOrEmpty(vm.WordsPerDayLabel));
    }

    [AvaloniaFact]
    public void Deadline_PastAndInvalid()
    {
        var vm = new DashboardViewModel();
        DoRefresh(vm, deadline: "2000-01-01", totalWords: 100, projectGoal: 1000);
        Assert.Equal(0, vm.DaysRemaining);
        Assert.Equal(900, vm.WordsPerDayNeeded); // words left, no days

        DoRefresh(vm, deadline: "not-a-date");
        Assert.Equal(0, vm.DaysRemaining);
        Assert.Equal(0, vm.WordsPerDayNeeded);
    }

    [AvaloniaFact]
    public void RecentActivity_Flag()
    {
        var vm = new DashboardViewModel();
        Assert.False(vm.HasRecentActivity);
        vm.RecentActivity = [new ActivityItem()];
        Assert.True(vm.HasRecentActivity);
    }

    [AvaloniaFact]
    public void LoadCoverImage_MissingNull_PresentRuns()
    {
        var vm = new DashboardViewModel();
        DoRefresh(vm, coverPath: @"C:\nope\missing.png");
        Assert.False(vm.HasCoverImage); // missing -> null

        using var dir = new TempDir();
        var p = Path.Combine(dir.Path, "c.png");
        File.WriteAllBytes(p, new byte[] { 1, 2, 3 });
        DoRefresh(vm, coverPath: p); // exists -> DecodeCover line runs (may yield null on garbage)
    }

    [AvaloniaFact]
    public void RefreshEnhancedStats_BreakdownPacingEcho()
    {
        var vm = new DashboardViewModel();
        var chapters = new List<ChapterData>
        {
            new() { Guid = "c1", Title = "One", Status = ChapterStatus.Outline },
            new() { Guid = "c2", Title = "Two", Status = ChapterStatus.Final },
        };
        var scenes = new Dictionary<string, List<SceneData>>
        {
            ["c1"] = [new() { WordCount = 100 }, new() { WordCount = 200 }],
            ["c2"] = [new() { WordCount = 50 }],
        };
        var echoText = string.Concat(Enumerable.Repeat("dragon fire mountain ", 6));
        var contents = new Dictionary<string, string> { ["s"] = echoText };

        vm.RefreshEnhancedStats(chapters, scenes, contents);

        Assert.True(vm.HasEnhancedStats);
        Assert.Equal(1, vm.ScenesInOutline);
        Assert.Equal(1, vm.ScenesInFinal);
        Assert.Equal(300, vm.LongestChapterWords);
        Assert.Equal(50, vm.ShortestChapterWords);
        Assert.True(vm.AverageSceneWords > 0);
        Assert.Equal(2, vm.StatusBreakdown.Count); // Outline + Final present
        Assert.Equal(2, vm.ChapterPacing.Count);
        Assert.Contains(vm.EchoPhrases, e => e.Phrase.Contains("dragon"));
    }

    [AvaloniaFact]
    public void RefreshEnhancedStats_Empty_Zeros()
    {
        var vm = new DashboardViewModel();
        vm.RefreshEnhancedStats([], new(), new());
        Assert.False(vm.HasEnhancedStats);
        Assert.Equal(0, vm.LongestChapterWords);
        Assert.Empty(vm.EchoPhrases);
    }

    [AvaloniaFact]
    public void RefreshEnhancedStats_ShortText_NoEcho()
    {
        var vm = new DashboardViewModel();
        var chapters = new List<ChapterData> { new() { Guid = "c1", Title = "T", Status = ChapterStatus.Outline } };
        var scenes = new Dictionary<string, List<SceneData>> { ["c1"] = [new() { WordCount = 5 }] };
        vm.RefreshEnhancedStats(chapters, scenes, new() { ["s"] = "two words" }); // < 3 words -> no echo
        Assert.Empty(vm.EchoPhrases);
    }

    [AvaloniaFact]
    public void RefreshEnhancedStats_AllStatuses_Colors()
    {
        var vm = new DashboardViewModel();
        var chapters = Enum.GetValues<ChapterStatus>()
            .Select((s, i) => new ChapterData { Guid = $"c{i}", Title = $"T{i}", Status = s })
            .ToList();
        var scenes = chapters.ToDictionary(c => c.Guid, c => new List<SceneData> { new() { WordCount = 10 } });
        vm.RefreshEnhancedStats(chapters, scenes, new());
        Assert.Equal(5, vm.StatusBreakdown.Count);
        Assert.All(vm.StatusBreakdown, b => Assert.StartsWith("#", b.Color));
    }

    [AvaloniaFact]
    public void WordHistory_BarsStreakToday()
    {
        var vm = new DashboardViewModel();
        var svc = Substitute.For<IWordHistoryService>();
        svc.TotalForDay(Arg.Any<DateOnly>(), Arg.Any<string?>()).Returns(150);
        svc.CurrentStreak(Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<string?>()).Returns(7);
        vm.AttachWordHistory(svc);
        DoRefresh(vm); // sets DailyGoalTarget=500, calls RefreshWordHistory
        vm.SetActiveBookId("b1");

        Assert.Equal(150, vm.TodayWords);
        Assert.Equal(7, vm.CurrentStreak);
        Assert.Equal(30, vm.WordHistoryBars.Count);
        Assert.All(vm.WordHistoryBars, b => Assert.True(b.HeightPx >= 2));

        vm.SetHistoryRangeCommand.Execute("7");
        Assert.Equal(7, vm.WordHistoryBars.Count);
        vm.SetHistoryRangeCommand.Execute("bad"); // unparsable -> unchanged
        Assert.Equal(7, vm.WordHistoryBars.Count);
    }

    [AvaloniaFact]
    public void WordHistory_ReattachUnsubscribesOld_FiresOnChange()
    {
        var vm = new DashboardViewModel();
        var a = Substitute.For<IWordHistoryService>();
        var b = Substitute.For<IWordHistoryService>();
        vm.AttachWordHistory(a);
        vm.AttachWordHistory(b); // unsubscribes a
        b.TotalForDay(Arg.Any<DateOnly>(), Arg.Any<string?>()).Returns(5);
        b.HistoryChanged += Raise.Event<Action>(); // OnHistoryChanged -> RefreshWordHistory
        Assert.Equal(5, vm.TodayWords);
    }

    [AvaloniaFact]
    public async Task PickCover_NoCallback_NoThrow()
    {
        var vm = new DashboardViewModel(); // CoverImageSelected null
        await vm.PickCoverImageCommand.ExecuteAsync(null);
    }

    [AvaloniaFact]
    public async Task PickCover_ExistingCover_SwapBranches()
    {
        var vm = new DashboardViewModel();
        DoRefresh(vm, coverRel: "old.png");
        string? selectedResult = null;
        vm.CoverImageSelected = s => { selectedResult = s; return Task.CompletedTask; };

        // PickCoverImageRequested null -> return
        await vm.PickCoverImageCommand.ExecuteAsync(null);
        Assert.Null(selectedResult);

        // returns same path -> no swap
        vm.PickCoverImageRequested = _ => Task.FromResult<string?>("old.png");
        await vm.PickCoverImageCommand.ExecuteAsync(null);
        Assert.Null(selectedResult);

        // returns new path -> swap
        vm.PickCoverImageRequested = _ => Task.FromResult<string?>("new.png");
        await vm.PickCoverImageCommand.ExecuteAsync(null);
        Assert.Equal("new.png", selectedResult);
    }

    [AvaloniaFact]
    public async Task PickCover_NoCover_AllSourceChoices()
    {
        // Library branch
        var (vm, picked) = NoCoverVm();
        vm.ChooseAddImageSourceRequested = () => Task.FromResult<AddImageSourceChoice?>(AddImageSourceChoice.Library);
        vm.PickCoverImageRequested = _ => Task.FromResult<string?>("lib.png");
        await vm.PickCoverImageCommand.ExecuteAsync(null);
        Assert.Equal("lib.png", picked());

        // Import branch
        (vm, picked) = NoCoverVm();
        vm.ChooseAddImageSourceRequested = () => Task.FromResult<AddImageSourceChoice?>(AddImageSourceChoice.Import);
        vm.BrowseImageRequested = () => Task.FromResult<string?>(@"C:\f.png");
        await vm.PickCoverImageCommand.ExecuteAsync(null);
        Assert.Equal(@"import:C:\f.png", picked());

        // Clipboard branch
        (vm, picked) = NoCoverVm();
        vm.ChooseAddImageSourceRequested = () => Task.FromResult<AddImageSourceChoice?>(AddImageSourceChoice.Clipboard);
        vm.ImportExternalImageRequested = _ => Task.FromResult<string?>("ext.png");
        await vm.PickCoverImageCommand.ExecuteAsync(null);
        Assert.Equal("import:ext.png", picked());

        // Null choice -> no-op
        (vm, picked) = NoCoverVm();
        vm.ChooseAddImageSourceRequested = () => Task.FromResult<AddImageSourceChoice?>(null);
        await vm.PickCoverImageCommand.ExecuteAsync(null);
        Assert.Null(picked());

        // Default (unknown enum) -> no-op
        (vm, picked) = NoCoverVm();
        vm.ChooseAddImageSourceRequested = () => Task.FromResult<AddImageSourceChoice?>((AddImageSourceChoice)99);
        await vm.PickCoverImageCommand.ExecuteAsync(null);
        Assert.Null(picked());
    }

    [AvaloniaFact]
    public async Task PickCover_NoCover_EmptyResults_NoOp()
    {
        var (vm, picked) = NoCoverVm();
        vm.ChooseAddImageSourceRequested = () => Task.FromResult<AddImageSourceChoice?>(AddImageSourceChoice.Library);
        vm.PickCoverImageRequested = _ => Task.FromResult<string?>(""); // empty -> return
        await vm.PickCoverImageCommand.ExecuteAsync(null);
        Assert.Null(picked());

        (vm, picked) = NoCoverVm();
        vm.ChooseAddImageSourceRequested = () => Task.FromResult<AddImageSourceChoice?>(AddImageSourceChoice.Import);
        vm.BrowseImageRequested = () => Task.FromResult<string?>(""); // empty -> return
        await vm.PickCoverImageCommand.ExecuteAsync(null);
        Assert.Null(picked());
    }

    private static (DashboardViewModel Vm, Func<string?> Picked) NoCoverVm()
    {
        var vm = new DashboardViewModel();
        string? captured = null;
        vm.CoverImageSelected = s => { captured = s; return Task.CompletedTask; };
        return (vm, () => captured);
    }

    [AvaloniaFact]
    public void SubItems_Displays()
    {
        var sb = new StatusBreakdownItem("Final", 2, 1500, "#10B981", 0.5);
        Assert.False(string.IsNullOrEmpty(sb.WordCountDisplay));
        var cp = new ChapterPacingItem("Ch", 1200, 80);
        Assert.False(string.IsNullOrEmpty(cp.WordsDisplay));
        var echo = new EchoPhrase("a b c", 5);
        Assert.Equal(5, echo.Count);

        var bar = new WordHistoryBarItem { Date = new DateOnly(2026, 1, 2), Words = 100, MetGoal = true };
        Assert.Equal("#5BA855", bar.BarColor);
        bar.MetGoal = false;
        Assert.Equal("#7C7CB2", bar.BarColor);
        Assert.False(string.IsNullOrEmpty(bar.DateLabel));
        Assert.Contains("100", bar.Tooltip);
    }
}
