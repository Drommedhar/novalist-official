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
}
