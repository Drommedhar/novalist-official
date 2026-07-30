using System.Globalization;
using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class DashboardManuscriptTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;

    public DashboardManuscriptTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-dash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "DashNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private async Task<(string chapterGuid, string sceneId)> SeedSceneAsync(string html, string plain)
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C" + Guid.NewGuid().ToString("N")[..4]);
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        await _workspace.WriteSceneAsync(chapter.Guid, scene.Id, html, plain);
        return (chapter.Guid, scene.Id);
    }

    [Fact]
    public async Task Dashboard_EmptyProject_ReportsZeroes()
    {
        var dto = await new DashboardRpc(_workspace).GetAsync(1);
        Assert.Equal(0, dto.TotalWords);
        Assert.Equal(0, dto.AverageChapterWords);
        Assert.Equal(0, dto.MaxChapterWords);
        Assert.Empty(dto.ChapterPacing);
        Assert.Equal(0, dto.LongestChapterWords);
        Assert.Equal(0, dto.ShortestChapterWords);
        Assert.Equal(0d, dto.AverageSceneWords);
        Assert.Equal(0, dto.OutlineCount);
        Assert.Equal(0, dto.FirstDraftCount);
        Assert.Equal(0, dto.RevisedCount);
        Assert.Equal(0, dto.EditedCount);
        Assert.Equal(0, dto.FinalCount);
        Assert.Equal(0, dto.DaysRemaining);
        Assert.Equal(0, dto.WordsPerDayNeeded);
        Assert.Empty(dto.RecentActivity);
        Assert.Equal(string.Empty, dto.Author);
    }

    [Fact]
    public async Task Overview_EmptyProject_HasNameAndNoChapters()
    {
        var overview = await new DashboardRpc(_workspace).OverviewAsync();
        Assert.Equal("DashNovel", overview.ProjectName);
        Assert.Empty(overview.Chapters);
    }

    [Fact]
    public async Task Overview_ReportsChaptersScenesAndReadability()
    {
        var (chapterGuid, _) = await SeedSceneAsync(
            "<p>The quick brown fox jumps over the lazy dog again and again.</p>",
            "The quick brown fox jumps over the lazy dog again and again.");
        // Second scene in the same chapter, so the chapter aggregates both scenes.
        var scene2 = await _workspace.Projects.CreateSceneAsync(chapterGuid, "S2");
        await _workspace.WriteSceneAsync(chapterGuid, scene2.Id, "<p>Short line.</p>", "Short line.");
        // An empty chapter with no words exercises the readability=0 branch.
        await _workspace.Projects.CreateChapterAsync("Empty");

        var overview = await new DashboardRpc(_workspace).OverviewAsync();

        Assert.Equal(2, overview.Chapters.Length);
        var first = overview.Chapters[0];
        Assert.Equal(2, first.Scenes.Length);
        Assert.True(first.Words > 0);
        Assert.NotNull(first.ReadabilityLevel);

        var empty = overview.Chapters[1];
        Assert.Empty(empty.Scenes);
        Assert.Equal(0, empty.Words);
        Assert.Equal(0, empty.Readability);
        Assert.Null(empty.ReadabilityLevel);
    }

    [Fact]
    public async Task Dashboard_ComputesTotalsGoalsAndHistory()
    {
        await SeedSceneAsync("<p>alpha beta gamma delta</p>", "alpha beta gamma delta");
        await SeedSceneAsync("<p>one two three</p>", "one two three");
        _workspace.Projects.ProjectSettings.Author = "Ada Author";

        var dto = await new DashboardRpc(_workspace).GetAsync(7);

        Assert.Equal("DashNovel", dto.ProjectName);
        Assert.Equal("Ada Author", dto.Author);
        Assert.Equal(7, dto.TotalWords);
        Assert.Equal(2, dto.ChapterCount);
        Assert.Equal(2, dto.SceneCount);
        Assert.Equal(1000, dto.DailyGoalTarget);
        Assert.Equal(50000, dto.ProjectGoalTarget);
        Assert.Equal(7, dto.WordHistory.Count);
        Assert.True(dto.TodayWords >= 7);
        Assert.Equal(2, dto.StatusBreakdown.Single(s => s.Status == "Outline").Count);
        Assert.Equal(2, dto.ChapterPacing.Count);
        Assert.True(dto.MaxChapterWords >= 4);

        // Enhanced pacing summary: each scene is its own chapter (4 and 3 words).
        Assert.Equal(4, dto.LongestChapterWords);
        Assert.Equal(3, dto.ShortestChapterWords);
        Assert.Equal(3.5d, dto.AverageSceneWords);

        // Status summary counts (per chapter, all five statuses present).
        Assert.Equal(2, dto.OutlineCount);
        Assert.Equal(0, dto.FirstDraftCount);
        Assert.Equal(0, dto.RevisedCount);
        Assert.Equal(0, dto.EditedCount);
        Assert.Equal(0, dto.FinalCount);

        // Recent activity: one entry per live scene, newest first, capped list.
        Assert.Equal(2, dto.RecentActivity.Count);
        Assert.All(dto.RecentActivity, a => Assert.Equal("S", a.SceneTitle));
        Assert.All(dto.RecentActivity, a => Assert.False(string.IsNullOrWhiteSpace(a.Timestamp)));
    }

    [Fact]
    public async Task Dashboard_DeadlineMetrics_SurfacedFromGoals()
    {
        await SeedSceneAsync("<p>one two three four five</p>", "one two three four five");
        var rpc = new DashboardRpc(_workspace);

        var future = DateTime.Today.AddDays(20).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        await rpc.SetGoalsAsync(50, 105, future);

        var dto = await rpc.GetAsync(1);
        Assert.Equal(future, dto.Deadline);
        Assert.Equal(20, dto.DaysRemaining);
        // 100 words left over 20 days -> 5 words/day.
        Assert.Equal(5, dto.WordsPerDayNeeded);
    }

    [Fact]
    public void ComputeDeadlineMetrics_CoversNullGarbagePastAndFuture()
    {
        Assert.Equal((0, 0), DashboardRpc.ComputeDeadlineMetrics(null, 0, 100));
        Assert.Equal((0, 0), DashboardRpc.ComputeDeadlineMetrics("   ", 0, 100));
        Assert.Equal((0, 0), DashboardRpc.ComputeDeadlineMetrics("not-a-date", 0, 100));

        // Past deadline: remaining clamps to 0, per-day falls back to remaining words.
        var past = DateTime.Today.AddDays(-3).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.Equal((0, 40), DashboardRpc.ComputeDeadlineMetrics(past, 60, 100));

        // Future deadline with work left: positive days + ceil(words-left / days).
        var future = DateTime.Today.AddDays(10).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.Equal((10, 10), DashboardRpc.ComputeDeadlineMetrics(future, 0, 100));

        // Goal already met: no words left -> zero per day.
        Assert.Equal((10, 0), DashboardRpc.ComputeDeadlineMetrics(future, 100, 100));
    }

    [Fact]
    public async Task Dashboard_SetGoals_PersistsAndAffectsPercents()
    {
        await SeedSceneAsync("<p>one two three four five</p>", "one two three four five");
        var rpc = new DashboardRpc(_workspace);

        await rpc.SetGoalsAsync(5, 10, "2027-01-01");
        var dto = await rpc.GetAsync(1);

        Assert.Equal(5, dto.DailyGoalTarget);
        Assert.Equal(10, dto.ProjectGoalTarget);
        Assert.Equal("2027-01-01", dto.Deadline);
        Assert.Equal(50, dto.ProjectGoalPercent);

        await rpc.SetGoalsAsync(0, 0, "  ");
        var cleared = await rpc.GetAsync(1);
        Assert.Null(cleared.Deadline);
        Assert.Equal(0, cleared.DailyGoalPercent);
        Assert.Equal(0, cleared.ProjectGoalPercent);
    }

    [Fact]
    public async Task Dashboard_SurfacesEchoPhrases_FromSceneText()
    {
        var repeated = string.Concat(Enumerable.Repeat("cold wind howled tonight. ", 6));
        await SeedSceneAsync($"<p>{repeated}</p>", repeated);

        var dto = await new DashboardRpc(_workspace).GetAsync(1);

        Assert.Contains(dto.EchoPhrases, e => e.Phrase == "cold wind howled" && e.Count >= 5);
    }

    [Fact]
    public void EchoPhrases_FindRepeatsAndSkipStopPhrases()
    {
        var text = string.Concat(Enumerable.Repeat("the cold wind howled tonight. ", 6));
        var echoes = DashboardRpc.FindEchoPhrases(text, 3, 5);

        Assert.Contains(echoes, e => e.Phrase == "cold wind howled" && e.Count >= 5);
        Assert.Empty(DashboardRpc.FindEchoPhrases("", 3, 5));
        Assert.Empty(DashboardRpc.FindEchoPhrases("one two", 3, 5));
        Assert.True(DashboardRpc.IsStopPhrase("the of and"));
        Assert.False(DashboardRpc.IsStopPhrase("cold wind howled"));
    }

    [Fact]
    public async Task Cover_NoCover_GetReturnsNull()
    {
        var dto = await new DashboardRpc(_workspace).GetCoverAsync();
        Assert.Null(dto);
    }

    [Fact]
    public async Task Cover_SetImportsResolvesAndPersists()
    {
        var source = Path.Combine(_root, "picked-cover.png");
        await File.WriteAllBytesAsync(source, [0x89, 0x50, 0x4E, 0x47, 1, 2, 3]);

        var rpc = new DashboardRpc(_workspace);
        await rpc.SetCoverAsync(source);

        // Stored on both project and active-book metadata as a book-relative path.
        var stored = _workspace.Projects.ActiveBook!.CoverImage;
        Assert.EndsWith("picked-cover.png", stored);
        Assert.Equal(stored, _workspace.Projects.CurrentProject!.CoverImage);

        // The picked file was copied into the book's image folder.
        var bookRoot = _workspace.Projects.ActiveBookRoot!;
        Assert.True(File.Exists(Path.Combine(bookRoot, stored.Replace('/', Path.DirectorySeparatorChar))));

        // getCover projects the stored path relative to the project root.
        var resolved = await rpc.GetCoverAsync();
        Assert.NotNull(resolved);
        Assert.EndsWith("picked-cover.png", resolved);
    }

    [Fact]
    public async Task Cover_GetFallsBackToProjectCover_WhenBookEmpty()
    {
        _workspace.Projects.ActiveBook!.CoverImage = string.Empty;
        _workspace.Projects.CurrentProject!.CoverImage = "Images/legacy.png";

        var resolved = await new DashboardRpc(_workspace).GetCoverAsync();

        Assert.NotNull(resolved);
        Assert.Contains("Images/legacy.png", resolved);
    }

    [Fact]
    public async Task Cover_SetBlankPath_ClearsCover()
    {
        var source = Path.Combine(_root, "temp-cover.png");
        await File.WriteAllBytesAsync(source, [0x89, 0x50, 0x4E, 0x47, 9]);

        var rpc = new DashboardRpc(_workspace);
        await rpc.SetCoverAsync(source);
        Assert.NotNull(await rpc.GetCoverAsync());

        await rpc.SetCoverAsync("  ");

        Assert.Null(await rpc.GetCoverAsync());
        Assert.Equal(string.Empty, _workspace.Projects.ActiveBook!.CoverImage);
        Assert.Equal(string.Empty, _workspace.Projects.CurrentProject!.CoverImage);
    }

    [Fact]
    public async Task Banner_NoImages_GetReturnsNull()
    {
        var dto = await new DashboardRpc(_workspace).GetBannerAsync();
        Assert.Null(dto);
    }

    [Fact]
    public async Task Banner_FallsBackToCover_WhenBannerEmpty()
    {
        var source = Path.Combine(_root, "legacy-banner.png");
        await File.WriteAllBytesAsync(source, [0x89, 0x50, 0x4E, 0x47, 5]);

        var rpc = new DashboardRpc(_workspace);
        // Only a portrait cover is set (pre-split project); the banner must fall
        // back to it so existing projects keep rendering a Dashboard banner.
        await rpc.SetCoverAsync(source);
        Assert.Equal(string.Empty, _workspace.Projects.ActiveBook!.BannerImage);

        var banner = await rpc.GetBannerAsync();
        Assert.NotNull(banner);
        Assert.EndsWith("legacy-banner.png", banner);
    }

    [Fact]
    public async Task Banner_And_Cover_SetIndependently()
    {
        var coverSrc = Path.Combine(_root, "portrait.png");
        var bannerSrc = Path.Combine(_root, "wide.jpg");
        await File.WriteAllBytesAsync(coverSrc, [0x89, 0x50, 1]);
        await File.WriteAllBytesAsync(bannerSrc, [0xFF, 0xD8, 2]);

        var rpc = new DashboardRpc(_workspace);
        await rpc.SetCoverAsync(coverSrc);
        await rpc.SetBannerAsync(bannerSrc);

        var book = _workspace.Projects.ActiveBook!;
        var project = _workspace.Projects.CurrentProject!;
        Assert.EndsWith("portrait.png", book.CoverImage);
        Assert.EndsWith("wide.jpg", book.BannerImage);
        Assert.EndsWith("portrait.png", project.CoverImage);
        Assert.EndsWith("wide.jpg", project.BannerImage);
        // The two fields are distinct.
        Assert.NotEqual(book.CoverImage, book.BannerImage);

        // getBanner prefers the banner over the cover.
        var banner = await rpc.GetBannerAsync();
        Assert.NotNull(banner);
        Assert.EndsWith("wide.jpg", banner);

        var cover = await rpc.GetCoverAsync();
        Assert.NotNull(cover);
        Assert.EndsWith("portrait.png", cover);
    }

    [Fact]
    public async Task Banner_ClearFallsBackToCover_ThenNull()
    {
        var coverSrc = Path.Combine(_root, "c.png");
        var bannerSrc = Path.Combine(_root, "b.png");
        await File.WriteAllBytesAsync(coverSrc, [1]);
        await File.WriteAllBytesAsync(bannerSrc, [2]);

        var rpc = new DashboardRpc(_workspace);
        await rpc.SetCoverAsync(coverSrc);
        await rpc.SetBannerAsync(bannerSrc);

        // Clearing the banner falls back to the cover.
        await rpc.SetBannerAsync(" ");
        Assert.Equal(string.Empty, _workspace.Projects.ActiveBook!.BannerImage);
        var fallback = await rpc.GetBannerAsync();
        Assert.NotNull(fallback);
        Assert.EndsWith("c.png", fallback);

        // Clearing the cover too leaves nothing.
        await rpc.SetCoverAsync("");
        Assert.Null(await rpc.GetBannerAsync());
    }

    [Fact]
    public async Task Banner_GetFallsBackToProjectBanner_WhenBookEmpty()
    {
        _workspace.Projects.ActiveBook!.BannerImage = string.Empty;
        _workspace.Projects.CurrentProject!.BannerImage = "Images/proj-banner.png";

        var resolved = await new DashboardRpc(_workspace).GetBannerAsync();

        Assert.NotNull(resolved);
        Assert.Contains("Images/proj-banner.png", resolved);
    }

    [Fact]
    public async Task Banner_GetFallsBackToProjectCover_WhenAllElseEmpty()
    {
        _workspace.Projects.ActiveBook!.BannerImage = string.Empty;
        _workspace.Projects.ActiveBook!.CoverImage = string.Empty;
        _workspace.Projects.CurrentProject!.BannerImage = string.Empty;
        _workspace.Projects.CurrentProject!.CoverImage = "Images/proj-cover.png";

        var resolved = await new DashboardRpc(_workspace).GetBannerAsync();

        Assert.NotNull(resolved);
        Assert.Contains("Images/proj-cover.png", resolved);
    }

    [Fact]
    public async Task Manuscript_GroupsFiltersAndCarriesContent()
    {
        var (chapterGuid, sceneId) = await SeedSceneAsync("<p>Es war kalt</p>", "Es war kalt");
        var manuscript = new ManuscriptRpc(_workspace);

        var all = await manuscript.GetAsync("All");
        var section = all.Single(s => s.ChapterGuid == chapterGuid);
        Assert.Contains("Es war kalt", section.Scenes.Single(s => s.SceneId == sceneId).Html);
        Assert.Equal("Outline", section.Status);

        Assert.Empty(await manuscript.GetAsync("Final"));
        var byStatus = await manuscript.GetAsync("Outline");
        Assert.NotEmpty(byStatus);
    }

    [Fact]
    public async Task Manuscript_WithChosenScenes_StitchesOnlyThoseInReadingOrder()
    {
        var (firstChapter, firstScene) = await SeedSceneAsync("<p>A</p>", "A");
        var second = await _workspace.Projects.CreateChapterAsync("Two");
        var later = await _workspace.Projects.CreateSceneAsync(second.Guid, "C");
        await _workspace.Projects.CreateSceneAsync(firstChapter, "B");
        var manuscript = new ManuscriptRpc(_workspace);

        // Given out of order on purpose: the answer is reading order.
        var sections = await manuscript.GetAsync("All", [later.Id, firstScene]);

        Assert.Equal(2, sections.Length);
        Assert.Equal(firstChapter, sections[0].ChapterGuid);
        Assert.Equal("S", Assert.Single(sections[0].Scenes).Title);
        Assert.Equal("C", Assert.Single(sections[1].Scenes).Title);
    }

    [Fact]
    public async Task Manuscript_AChosenSetIgnoresTheStatusFilter()
    {
        var (_, sceneId) = await SeedSceneAsync("<p>A</p>", "A");
        var manuscript = new ManuscriptRpc(_workspace);

        // The chapter is at Outline, so a Final filter would normally drop it.
        // A chosen set says exactly which scenes to read.
        var sections = await manuscript.GetAsync("Final", [sceneId]);

        Assert.Equal("S", Assert.Single(Assert.Single(sections).Scenes).Title);
    }

    [Fact]
    public async Task Manuscript_AnEmptyChosenSetIsTheWholeBook()
    {
        await SeedSceneAsync("<p>A</p>", "A");

        Assert.Single(await new ManuscriptRpc(_workspace).GetAsync("All", []));
    }

    [Fact]
    public async Task SetPov_SetsAndClearsOverrides()
    {
        var (chapterGuid, sceneId) = await SeedSceneAsync("<p>x</p>", "x");
        var manuscript = new ManuscriptRpc(_workspace);

        await manuscript.SetPovAsync(chapterGuid, sceneId, "Mira");
        var (_, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        Assert.Equal("Mira", scene.AnalysisOverrides?.Pov);

        await manuscript.SetPovAsync(chapterGuid, sceneId, " ");
        var (_, cleared) = _workspace.ResolveScene(chapterGuid, sceneId);
        Assert.Null(cleared.AnalysisOverrides);

        await manuscript.SetPovAsync(chapterGuid, sceneId, "");
        Assert.Null(_workspace.ResolveScene(chapterGuid, sceneId).scene.AnalysisOverrides);
    }

    // ── Pacing and history ──
    //
    // A flat daily goal plus a deadline gave one static row and a streak that
    // any missed day broke - including a day the writer had said they take off.

    [Fact]
    public async Task SetPacing_KeepsOnlyRealWeekdays_AndTreatsEveryDayAsNoRestriction()
    {
        var rpc = new DashboardRpc(_workspace);

        await rpc.SetPacingAsync(true, [1, 3, 3, 9, -1, 5]);
        var goals = _workspace.Projects.ProjectSettings.WordCountGoals;
        Assert.True(goals.AdaptiveDailyGoal);
        Assert.Equal([1, 3, 5], goals.WritingDays);

        // Every day selected is the same thing as no restriction, and storing
        // it as a list would make "is this a writing day" needlessly slower.
        await rpc.SetPacingAsync(false, [0, 1, 2, 3, 4, 5, 6]);
        Assert.Empty(_workspace.Projects.ProjectSettings.WordCountGoals.WritingDays);
    }

    [Fact]
    public async Task AdaptiveGoal_SpreadsWhatIsLeftOverTheWritingDaysThatRemain()
    {
        var (chapterGuid, sceneId) = await SeedSceneAsync("<p>one two three</p>", "one two three");
        _ = chapterGuid;
        _ = sceneId;
        var goals = _workspace.Projects.ProjectSettings.WordCountGoals;
        goals.ProjectGoal = 1000;
        goals.DailyGoal = 999;
        goals.Deadline = DateTime.Today.AddDays(9).ToString("yyyy-MM-dd");
        var rpc = new DashboardRpc(_workspace);
        await rpc.SetPacingAsync(true, null);

        var dashboard = await rpc.GetAsync(30);

        // Ten days including today, ~997 words left: a hundred a day, not 999.
        Assert.InRange(dashboard.DailyGoalTarget, 90, 110);
        Assert.True(dashboard.History.Adaptive);
    }

    [Fact]
    public async Task AdaptiveGoal_WithNoDeadline_KeepsTheFlatNumber()
    {
        var goals = _workspace.Projects.ProjectSettings.WordCountGoals;
        goals.DailyGoal = 750;
        goals.Deadline = null;
        var rpc = new DashboardRpc(_workspace);
        await rpc.SetPacingAsync(true, null);

        Assert.Equal(750, (await rpc.GetAsync(30)).DailyGoalTarget);
    }

    [Fact]
    public async Task AdaptiveGoal_PastTheDeadlineAsksForEverythingThatIsLeft()
    {
        var goals = _workspace.Projects.ProjectSettings.WordCountGoals;
        goals.ProjectGoal = 500;
        goals.Deadline = DateTime.Today.AddDays(-5).ToString("yyyy-MM-dd");
        var rpc = new DashboardRpc(_workspace);
        await rpc.SetPacingAsync(true, null);

        Assert.Equal(500, (await rpc.GetAsync(30)).DailyGoalTarget);
    }

    [Fact]
    public async Task AdaptiveGoal_AFinishedProjectAsksForNothing()
    {
        var goals = _workspace.Projects.ProjectSettings.WordCountGoals;
        goals.ProjectGoal = 0;
        goals.Deadline = DateTime.Today.AddDays(5).ToString("yyyy-MM-dd");
        var rpc = new DashboardRpc(_workspace);
        await rpc.SetPacingAsync(true, null);

        Assert.Equal(0, (await rpc.GetAsync(30)).DailyGoalTarget);
    }

    [Fact]
    public async Task History_ReportsWhatAJournalCanSayBeyondToday()
    {
        var rpc = new DashboardRpc(_workspace);

        var history = (await rpc.GetAsync(30)).History;

        // A project with no writing yet still answers, rather than dividing by
        // a day count of zero.
        Assert.Equal(0, history.LongestStreak);
        Assert.Equal(0, history.AveragePerWritingDay);
        Assert.Equal(string.Empty, history.BestDayDate);
        Assert.True(history.WritingDaysConsidered > 300);
    }

    [Fact]
    public async Task History_LeavesOutTheDaysTheWriterDoesNotWrite()
    {
        var rpc = new DashboardRpc(_workspace);
        await rpc.SetPacingAsync(false, [(int)DateTime.Today.DayOfWeek]);

        var history = (await rpc.GetAsync(30)).History;

        // One weekday over a year is about fifty-two days, not three hundred.
        Assert.InRange(history.WritingDaysConsidered, 45, 60);
    }

    // ── Horizons longer than a day ──

    [Fact]
    public async Task Horizons_AreOffUntilTheWriterSetsOne()
    {
        var dto = await new DashboardRpc(_workspace).GetAsync(1);

        // Nobody is handed a weekly budget they did not ask for.
        Assert.Equal(0, dto.Week.Goal);
        Assert.Equal(0, dto.Month.Goal);
        Assert.Equal(0, dto.Week.Percent);
    }

    [Fact]
    public async Task Horizons_CountThisWeekAndThisMonth()
    {
        var rpc = new DashboardRpc(_workspace);
        await rpc.SetGoalsAsync(500, 50000, null, weeklyGoal: 3000, monthlyGoal: 12000);
        await SeedSceneAsync("<p>one two three four five</p>", "one two three four five");

        var dto = await rpc.GetAsync(1);

        Assert.Equal(3000, dto.Week.Goal);
        Assert.Equal(12000, dto.Month.Goal);
        // Today's five words fall inside both horizons.
        Assert.Equal(5, dto.Week.Current);
        Assert.Equal(5, dto.Month.Current);
        Assert.True(dto.Week.DaysLeft >= 1);
        Assert.True(dto.Month.DaysLeft >= 1);
    }

    [Fact]
    public async Task Horizons_LeftAloneByACallerThatDoesNotKnowAboutThem()
    {
        var rpc = new DashboardRpc(_workspace);
        await rpc.SetGoalsAsync(500, 50000, null, weeklyGoal: 3000, monthlyGoal: 12000);

        // The three-argument form is what shipped first; it must not silently
        // clear horizons the writer set somewhere else.
        await rpc.SetGoalsAsync(600, 60000, null);

        var goals = _workspace.Projects.ProjectSettings.WordCountGoals;
        Assert.Equal(3000, goals.WeeklyGoal);
        Assert.Equal(12000, goals.MonthlyGoal);
        Assert.Equal(600, goals.DailyGoal);

        // And a negative is nothing rather than a goal running backwards.
        await rpc.SetGoalsAsync(600, 60000, null, weeklyGoal: -5, monthlyGoal: 0);
        Assert.Equal(0, goals.WeeklyGoal);
        Assert.Equal(0, goals.MonthlyGoal);
    }

    [Theory]
    // Monday rather than Sunday: a week that starts mid-weekend cannot be
    // caught up on the weekend, which is when most catching up happens.
    [InlineData("2026-07-30", "2026-07-27")]   // a Thursday
    [InlineData("2026-07-27", "2026-07-27")]   // the Monday itself
    [InlineData("2026-08-02", "2026-07-27")]   // the Sunday after
    public void StartOfWeek_IsTheMondayBefore(string day, string expected)
        => Assert.Equal(
            DateOnly.Parse(expected, CultureInfo.InvariantCulture),
            DashboardRpc.StartOfWeek(DateOnly.Parse(day, CultureInfo.InvariantCulture)));

}
