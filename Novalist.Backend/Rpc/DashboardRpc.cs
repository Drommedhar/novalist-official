using System.Globalization;
using System.Text.RegularExpressions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Aggregated project statistics for the dashboard view.</summary>
public sealed partial class DashboardRpc
{
    private readonly Workspace _workspace;
    private readonly EntityService _entities;

    public DashboardRpc(Workspace workspace)
    {
        _workspace = workspace;
        _entities = new EntityService(workspace.Projects);
    }

    /// <summary>
    /// Per-chapter / per-scene word + readability breakdown for the status-bar
    /// project-overview popover (mirrors the desktop status-bar overview). Kept
    /// separate from <c>dashboard/get</c> so it is only computed on demand when
    /// the popover opens.
    /// </summary>
    [JsonRpcMethod("dashboard/overview")]
    public async Task<ProjectOverviewDto> OverviewAsync()
    {
        var projects = _workspace.Projects;
        var project = projects.CurrentProject ?? throw new InvalidOperationException("No project open.");
        var book = projects.ActiveBook ?? throw new InvalidOperationException("No project open.");
        var manifest = projects.ScenesManifest;
        var language = _workspace.Settings.Effective.AutoReplacementLanguage;
        var wordsPerPage = projects.ProjectSettings.WordsPerPage;

        var chapters = new List<ChapterOverviewDto>();
        foreach (var chapter in book.Chapters.OrderBy(c => c.Order))
        {
            var scenes = (manifest?.Chapters.GetValueOrDefault(chapter.Guid) ?? [])
                .Where(s => s.ArchivedAt == null)
                .ToList();

            var chapterText = new System.Text.StringBuilder();
            var sceneDtos = new List<SceneOverviewDto>();
            foreach (var scene in scenes)
            {
                chapterText.Append(await projects.ReadSceneContentAsync(chapter, scene)).Append(' ');
                sceneDtos.Add(new SceneOverviewDto(scene.Title, scene.WordCount));
            }

            var words = scenes.Sum(s => s.WordCount);
            int readability = 0;
            string? readabilityLevel = null;
            if (words > 0)
            {
                var stats = TextStatistics.Calculate(chapterText.ToString(), language);
                readability = stats.Readability.Score;
                readabilityLevel = TextStatistics.FormatReadabilityLevel(stats.Readability.Level);
            }

            chapters.Add(new ChapterOverviewDto(
                chapter.Title, words, readability, readabilityLevel, sceneDtos.ToArray(),
                PageEstimate.Pages(words, wordsPerPage)));
        }

        return new ProjectOverviewDto(
            project.Name, chapters.ToArray(),
            PageEstimate.Pages(chapters.Sum(c => c.Words), wordsPerPage),
            wordsPerPage);
    }

    [JsonRpcMethod("dashboard/get")]
    public async Task<DashboardDto> GetAsync(int historyRangeDays)
    {
        var projects = _workspace.Projects;
        var project = projects.CurrentProject ?? throw new InvalidOperationException("No project open.");
        var book = projects.ActiveBook ?? throw new InvalidOperationException("No project open.");
        var manuscript = await ReadManuscriptAsync(book);
        var totalWords = manuscript.TotalWords;
        var goals = projects.ProjectSettings.WordCountGoals;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var (dailyCurrent, bars) = await ReadDailyProgressAsync(goals, totalWords, today, book.Id, historyRangeDays);
        var characters = await _entities.LoadCharactersAsync();
        var locations = await _entities.LoadLocationsAsync();
        var pacing = manuscript.Pacing;
        var maxChapterWords = pacing.Count > 0 ? pacing.Max(p => p.Words) : 0;
        var minChapterWords = pacing.Count > 0 ? pacing.Min(p => p.Words) : 0;
        var allSceneWords = manuscript.SceneWords;
        var avgSceneWords = allSceneWords.Count > 0 ? allSceneWords.Average() : 0d;

        var (daysRemaining, wordsPerDayNeeded) =
            ComputeDeadlineMetrics(goals.Deadline, totalWords, goals.ProjectGoal);

        var effectiveDailyGoal = goals.AdaptiveDailyGoal
            ? AdaptiveGoal(goals, totalWords, today)
            : goals.DailyGoal;

        var history = BuildHistoryStats(goals, book.Id, today);

        var (weekCurrent, weekLeft) = HorizonProgress(book.Id, StartOfWeek(today), today);
        var (monthCurrent, monthLeft) =
            HorizonProgress(book.Id, new DateOnly(today.Year, today.Month, 1), today);

        var recentActivity = manuscript.Activity
            .OrderByDescending(a => a.Modified)
            .Take(8)
            .Select(a => new RecentActivityDto(a.SceneTitle, a.ChapterTitle, a.ChapterGuid, a.SceneId,
                a.Modified.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)))
            .ToArray();

        int StatusCount(string status) => manuscript.Status.GetValueOrDefault(status).Count;

        return new DashboardDto(
            project.Name,
            projects.ProjectSettings.Author,
            totalWords,
            manuscript.ChapterCount,
            manuscript.SceneCount,
            characters.Count,
            locations.Count,
            TextStatistics.EstimateReadingTime(totalWords),
            manuscript.ChapterCount > 0 ? (int)Math.Round((double)totalWords / manuscript.ChapterCount) : 0,
            dailyCurrent,
            effectiveDailyGoal,
            effectiveDailyGoal > 0
                ? Math.Min(100, (int)Math.Round(dailyCurrent * 100.0 / effectiveDailyGoal))
                : 0,
            goals.ProjectGoal,
            goals.ProjectGoal > 0 ? Math.Min(100, (int)Math.Round(totalWords * 100.0 / goals.ProjectGoal)) : 0,
            goals.Deadline,
            daysRemaining,
            wordsPerDayNeeded,
            _workspace.WordHistory.TotalForDay(today, book.Id),
            _workspace.WordHistory.CurrentStreak(today, Math.Max(1, effectiveDailyGoal), book.Id),
            history,
            maxChapterWords,
            minChapterWords,
            avgSceneWords,
            StatusCount("Outline"),
            StatusCount("FirstDraft"),
            StatusCount("Revised"),
            StatusCount("Edited"),
            StatusCount("Final"),
            manuscript.Status.Select(kv => new StatusBreakdownDto(kv.Key, kv.Value.Count, kv.Value.Words)).ToArray(),
            pacing.ToArray(),
            maxChapterWords,
            FindEchoPhrases(manuscript.Text.ToString(), 3, 5).Take(20)
                .Select(e => new EchoPhraseDto(e.Phrase, e.Count)).ToArray(),
            bars.ToArray(),
            recentActivity,
            new HorizonDto(
                weekCurrent, goals.WeeklyGoal, Percent(weekCurrent, goals.WeeklyGoal), weekLeft),
            new HorizonDto(
                monthCurrent, goals.MonthlyGoal, Percent(monthCurrent, goals.MonthlyGoal),
                monthLeft));
    }

    private async Task<ManuscriptMetrics> ReadManuscriptAsync(BookData book)
    {
        var projects = _workspace.Projects;
        var manifest = projects.ScenesManifest;
        var chapters = book.Chapters.OrderBy(c => c.Order).ToList();
        var metrics = new ManuscriptMetrics { ChapterCount = chapters.Count };
        foreach (var chapter in chapters)
        {
            var scenes = manifest?.Chapters.GetValueOrDefault(chapter.Guid) ?? [];
            var live = scenes.Where(s => s.ArchivedAt == null && !s.Inactive).ToList();
            var chapterWords = live.Sum(s => s.WordCount);
            metrics.TotalWords += chapterWords;
            metrics.SceneCount += live.Count;
            var status = chapter.Status.ToString();
            var agg = metrics.Status.GetValueOrDefault(status);
            metrics.Status[status] = (agg.Count + 1, agg.Words + chapterWords);
            metrics.Pacing.Add(new ChapterPacingDto(chapter.Title, chapterWords));
            foreach (var scene in live)
            {
                metrics.Text.Append(await projects.ReadSceneContentAsync(chapter, scene)).Append(' ');
                metrics.SceneWords.Add(scene.WordCount);
                metrics.Activity.Add((scene.Title, chapter.Title, chapter.Guid, scene.Id,
                    System.IO.File.GetLastWriteTime(projects.GetSceneFilePath(chapter, scene))));
            }
        }
        return metrics;
    }

    private async Task<(int Current, List<WordHistoryBarDto> Bars)> ReadDailyProgressAsync(
        ProjectWordCountGoals goals, int totalWords, DateOnly today, string bookId, int historyRangeDays)
    {
        if (goals.DailyBaselineDate != today.ToString("yyyy-MM-dd"))
        {
            goals.DailyBaselineDate = today.ToString("yyyy-MM-dd");
            goals.DailyBaselineWords = totalWords;
            await _workspace.Projects.SaveProjectSettingsAsync();
        }
        var current = Math.Max(0, totalWords - (goals.DailyBaselineWords ?? totalWords));
        await _workspace.WordHistory.LoadAsync();
        var bars = new List<WordHistoryBarDto>();
        for (var i = historyRangeDays - 1; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            var words = _workspace.WordHistory.TotalForDay(day, bookId);
            bars.Add(new WordHistoryBarDto(day.ToString("yyyy-MM-dd"), words,
                goals.DailyGoal > 0 && words >= goals.DailyGoal));
        }
        return (current, bars);
    }

    private sealed class ManuscriptMetrics
    {
        public int ChapterCount { get; init; }
        public int TotalWords { get; set; }
        public int SceneCount { get; set; }
        public Dictionary<string, (int Count, int Words)> Status { get; } = [];
        public List<ChapterPacingDto> Pacing { get; } = [];
        public System.Text.StringBuilder Text { get; } = new();
        public List<int> SceneWords { get; } = [];
        public List<(string SceneTitle, string ChapterTitle, string ChapterGuid,
            string SceneId, DateTime Modified)> Activity { get; } = [];
    }

    /// <summary>
    /// The word goals. <paramref name="weeklyGoal"/> and
    /// <paramref name="monthlyGoal"/> are optional so a caller written before
    /// longer horizons existed keeps working and leaves them alone; 0 turns one
    /// off, which is what every project starts at.
    /// </summary>
    [JsonRpcMethod("dashboard/setGoals")]
    public async Task SetGoalsAsync(
        int dailyGoal, int projectGoal, string? deadline,
        int? weeklyGoal = null, int? monthlyGoal = null)
    {
        var goals = _workspace.Projects.ProjectSettings.WordCountGoals;
        goals.DailyGoal = dailyGoal;
        goals.ProjectGoal = projectGoal;
        goals.Deadline = string.IsNullOrWhiteSpace(deadline) ? null : deadline;
        if (weeklyGoal.HasValue) goals.WeeklyGoal = Math.Max(0, weeklyGoal.Value);
        if (monthlyGoal.HasValue) goals.MonthlyGoal = Math.Max(0, monthlyGoal.Value);
        await _workspace.Projects.SaveProjectSettingsAsync();
    }

    /// <summary>
    /// Which days the writer writes, and whether today's goal follows what is
    /// left rather than being the same number every day.
    /// </summary>
    [JsonRpcMethod("dashboard/setPacing")]
    public async Task SetPacingAsync(bool adaptive, int[]? writingDays)
    {
        var goals = _workspace.Projects.ProjectSettings.WordCountGoals;
        goals.AdaptiveDailyGoal = adaptive;
        // Out-of-range numbers would silently mean "never a writing day", and
        // every day selected is the same thing as no restriction.
        var days = (writingDays ?? [])
            .Where(d => d is >= 0 and <= 6)
            .Distinct()
            .OrderBy(d => d)
            .ToList();
        goals.WritingDays = days.Count == 7 ? [] : days;
        await _workspace.Projects.SaveProjectSettingsAsync();
    }
}

public sealed record DashboardDto(
    string ProjectName,
    string Author,
    int TotalWords,
    int ChapterCount,
    int SceneCount,
    int CharacterCount,
    int LocationCount,
    int ReadingTimeMinutes,
    int AverageChapterWords,
    int DailyGoalCurrent,
    int DailyGoalTarget,
    int DailyGoalPercent,
    int ProjectGoalTarget,
    int ProjectGoalPercent,
    string? Deadline,
    int DaysRemaining,
    int WordsPerDayNeeded,
    int TodayWords,
    int CurrentStreak,
    HistoryStatsDto History,
    int LongestChapterWords,
    int ShortestChapterWords,
    double AverageSceneWords,
    int OutlineCount,
    int FirstDraftCount,
    int RevisedCount,
    int EditedCount,
    int FinalCount,
    IReadOnlyList<StatusBreakdownDto> StatusBreakdown,
    IReadOnlyList<ChapterPacingDto> ChapterPacing,
    int MaxChapterWords,
    IReadOnlyList<EchoPhraseDto> EchoPhrases,
    IReadOnlyList<WordHistoryBarDto> WordHistory,
    IReadOnlyList<RecentActivityDto> RecentActivity,
    /// <summary>This calendar week, Monday to Sunday.</summary>
    HorizonDto Week,
    /// <summary>This calendar month.</summary>
    HorizonDto Month);

/// <summary>
/// Progress over a horizon longer than a day.
///
/// <c>DaysLeft</c> counts the writing days remaining in it including today,
/// which is what turns "behind" into "behind, with three days to fix it".
/// </summary>
public sealed record HorizonDto(int Current, int Goal, int Percent, int DaysLeft);

public sealed record StatusBreakdownDto(string Status, int Count, int WordCount);

public sealed record ProjectOverviewDto(
    string ProjectName,
    ChapterOverviewDto[] Chapters,
    /// <summary>Estimated printed pages for the whole book.</summary>
    int Pages,
    /// <summary>The figure the estimate used, so the UI can say what it assumed
    /// rather than presenting a number out of nowhere.</summary>
    int WordsPerPage);

public sealed record ChapterOverviewDto(
    string Title, int Words, int Readability, string? ReadabilityLevel, SceneOverviewDto[] Scenes,
    /// <summary>Estimated printed pages. An estimate, and the UI says so.</summary>
    int Pages);

public sealed record SceneOverviewDto(string Title, int Words);

public sealed record ChapterPacingDto(string Title, int Words);

public sealed record EchoPhraseDto(string Phrase, int Count);

public sealed record WordHistoryBarDto(string Date, int Words, bool MetGoal);

/// <summary>
/// What a per-day journal can say beyond today's number: how long the best run
/// was, how often the goal was met, and the best day so far.
/// </summary>
public sealed record HistoryStatsDto(
    int LongestStreak,
    int DaysWritten,
    int DaysHitGoal,
    int WritingDaysConsidered,
    int BestDayWords,
    string BestDayDate,
    int AveragePerWritingDay,
    bool Adaptive,
    int[] WritingDays);

/// <summary>A recently edited scene. The chapter/scene ids let the dashboard row
/// open that scene in the editor.</summary>
public sealed record RecentActivityDto(
    string SceneTitle, string ChapterTitle, string ChapterGuid, string SceneId, string Timestamp);
