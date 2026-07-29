using System.Globalization;
using System.Text.RegularExpressions;
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
        var book = projects.ActiveBook ?? throw new InvalidOperationException("No project open.");
        var manifest = projects.ScenesManifest;
        var language = _workspace.Settings.Effective.AutoReplacementLanguage;

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
                chapter.Title, words, readability, readabilityLevel, sceneDtos.ToArray()));
        }

        return new ProjectOverviewDto(projects.CurrentProject!.Name, chapters.ToArray());
    }

    [JsonRpcMethod("dashboard/get")]
    public async Task<DashboardDto> GetAsync(int historyRangeDays)
    {
        var projects = _workspace.Projects;
        var book = projects.ActiveBook ?? throw new InvalidOperationException("No project open.");
        var manifest = projects.ScenesManifest;

        var chapters = book.Chapters.OrderBy(c => c.Order).ToList();
        var totalWords = 0;
        var sceneCount = 0;
        var statusAgg = new Dictionary<string, (int Count, int Words)>();
        var pacing = new List<ChapterPacingDto>();
        var allText = new System.Text.StringBuilder();
        var allSceneWords = new List<int>();
        var activity = new List<(string SceneTitle, string ChapterTitle, string ChapterGuid,
            string SceneId, DateTime Modified)>();

        foreach (var chapter in chapters)
        {
            var scenes = manifest?.Chapters.GetValueOrDefault(chapter.Guid) ?? [];
            var live = scenes.Where(s => s.ArchivedAt == null).ToList();
            var chapterWords = live.Sum(s => s.WordCount);
            totalWords += chapterWords;
            sceneCount += live.Count;

            var status = chapter.Status.ToString();
            var agg = statusAgg.GetValueOrDefault(status);
            statusAgg[status] = (agg.Count + 1, agg.Words + chapterWords);
            pacing.Add(new ChapterPacingDto(chapter.Title, chapterWords));

            foreach (var scene in live)
            {
                allText.Append(await projects.ReadSceneContentAsync(chapter, scene)).Append(' ');
                allSceneWords.Add(scene.WordCount);
                activity.Add((scene.Title, chapter.Title, chapter.Guid, scene.Id,
                    System.IO.File.GetLastWriteTime(projects.GetSceneFilePath(chapter, scene))));
            }
        }

        var goals = projects.ProjectSettings.WordCountGoals;
        var today = DateOnly.FromDateTime(DateTime.Now);
        // Daily baseline resets per calendar day, matching the Avalonia shell.
        if (goals.DailyBaselineDate != today.ToString("yyyy-MM-dd"))
        {
            goals.DailyBaselineDate = today.ToString("yyyy-MM-dd");
            goals.DailyBaselineWords = totalWords;
            await projects.SaveProjectSettingsAsync();
        }
        var dailyCurrent = Math.Max(0, totalWords - (goals.DailyBaselineWords ?? totalWords));

        await _workspace.WordHistory.LoadAsync();
        var bars = new List<WordHistoryBarDto>();
        for (var i = historyRangeDays - 1; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            var words = _workspace.WordHistory.TotalForDay(day, book.Id);
            bars.Add(new WordHistoryBarDto(day.ToString("yyyy-MM-dd"), words,
                goals.DailyGoal > 0 && words >= goals.DailyGoal));
        }

        var characters = await _entities.LoadCharactersAsync();
        var locations = await _entities.LoadLocationsAsync();
        var maxChapterWords = pacing.Count > 0 ? pacing.Max(p => p.Words) : 0;
        var minChapterWords = pacing.Count > 0 ? pacing.Min(p => p.Words) : 0;
        var avgSceneWords = allSceneWords.Count > 0 ? allSceneWords.Average() : 0d;

        var (daysRemaining, wordsPerDayNeeded) =
            ComputeDeadlineMetrics(goals.Deadline, totalWords, goals.ProjectGoal);

        // Today's target: the flat number, or what is actually left spread over
        // the days the writer says they write. A goal that ignores the days off
        // it was told about is quietly asking for the impossible.
        var effectiveDailyGoal = goals.AdaptiveDailyGoal
            ? AdaptiveGoal(goals, totalWords, today)
            : goals.DailyGoal;

        var history = BuildHistoryStats(goals, book.Id, today);

        var recentActivity = activity
            .OrderByDescending(a => a.Modified)
            .Take(8)
            .Select(a => new RecentActivityDto(a.SceneTitle, a.ChapterTitle, a.ChapterGuid, a.SceneId,
                a.Modified.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)))
            .ToArray();

        int StatusCount(string status) => statusAgg.GetValueOrDefault(status).Count;

        return new DashboardDto(
            projects.CurrentProject!.Name,
            projects.ProjectSettings.Author,
            totalWords,
            chapters.Count,
            sceneCount,
            characters.Count,
            locations.Count,
            TextStatistics.EstimateReadingTime(totalWords),
            chapters.Count > 0 ? (int)Math.Round((double)totalWords / chapters.Count) : 0,
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
            statusAgg.Select(kv => new StatusBreakdownDto(kv.Key, kv.Value.Count, kv.Value.Words)).ToArray(),
            pacing.ToArray(),
            maxChapterWords,
            FindEchoPhrases(allText.ToString(), 3, 5).Take(20)
                .Select(e => new EchoPhraseDto(e.Phrase, e.Count)).ToArray(),
            bars.ToArray(),
            recentActivity);
    }

    /// <summary>
    /// Today's target when the goal adapts: what is left of the project spread
    /// over the writing days between now and the deadline. Falls back to the
    /// flat goal when there is no deadline to plan against.
    /// </summary>
    private int AdaptiveGoal(
        Core.Models.ProjectWordCountGoals goals, int totalWords, DateOnly today)
    {
        if (!DateOnly.TryParse(goals.Deadline, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var deadline))
            return goals.DailyGoal;

        var remaining = Math.Max(0, goals.ProjectGoal - totalWords);
        if (remaining == 0) return 0;

        var writingDays = 0;
        for (var day = today; day <= deadline; day = day.AddDays(1))
            if (goals.IsWritingDay(day)) writingDays++;

        // Past the deadline, or every remaining day is one they do not write:
        // the honest answer is everything that is left, today.
        return writingDays == 0
            ? remaining
            : (int)Math.Ceiling(remaining / (double)writingDays);
    }

    /// <summary>
    /// What the per-day journal can say beyond today: the longest run of days
    /// the goal was met, how often it was met at all, and the best day.
    /// Days the writer said they do not write are left out of every figure.
    /// </summary>
    private HistoryStatsDto BuildHistoryStats(
        Core.Models.ProjectWordCountGoals goals, string bookId, DateOnly today)
    {
        var goal = Math.Max(1, goals.DailyGoal);
        var start = today.AddDays(-364);
        var longest = 0;
        var run = 0;
        var written = 0;
        var hit = 0;
        var considered = 0;
        var total = 0;
        var bestWords = 0;
        var bestDate = string.Empty;

        for (var day = start; day <= today; day = day.AddDays(1))
        {
            if (!goals.IsWritingDay(day)) continue;
            considered++;
            var words = _workspace.WordHistory.TotalForDay(day, bookId);
            total += words;
            if (words > 0) written++;
            if (words > bestWords)
            {
                bestWords = words;
                bestDate = day.ToString("yyyy-MM-dd");
            }
            if (words >= goal)
            {
                hit++;
                run++;
                if (run > longest) longest = run;
            }
            else
            {
                run = 0;
            }
        }

        return new HistoryStatsDto(
            longest, written, hit, considered, bestWords, bestDate,
            written > 0 ? (int)Math.Round(total / (double)written) : 0,
            goals.AdaptiveDailyGoal,
            [.. goals.WritingDays]);
    }

    // Ported from the Avalonia DashboardViewModel.ComputeDeadlineMetrics so the
    // deadline detail block (days-left / words-per-day) matches the old shell.
    internal static (int DaysRemaining, int WordsPerDayNeeded) ComputeDeadlineMetrics(
        string? deadline, int totalWords, int projectGoal)
    {
        if (string.IsNullOrWhiteSpace(deadline)
            || !DateTime.TryParse(deadline, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return (0, 0);
        }

        var remaining = Math.Max(0, (date.Date - DateTime.Today).Days);
        var wordsLeft = Math.Max(0, projectGoal - totalWords);
        var perDay = remaining > 0 ? (int)Math.Ceiling(wordsLeft / (double)remaining) : wordsLeft;
        return (remaining, perDay);
    }

    [JsonRpcMethod("dashboard/setGoals")]
    public async Task SetGoalsAsync(int dailyGoal, int projectGoal, string? deadline)
    {
        var goals = _workspace.Projects.ProjectSettings.WordCountGoals;
        goals.DailyGoal = dailyGoal;
        goals.ProjectGoal = projectGoal;
        goals.Deadline = string.IsNullOrWhiteSpace(deadline) ? null : deadline;
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

    /// <summary>
    /// Returns the active book's cover image as a path relative to the project
    /// root so the renderer can serve it through <c>novalist-project://</c>, or
    /// null when no cover is set. Prefers the book cover, falling back to the
    /// project-level cover.
    /// </summary>
    [JsonRpcMethod("dashboard/getCover")]
    public Task<string?> GetCoverAsync()
    {
        var projects = _workspace.Projects;
        var stored = projects.ActiveBook?.CoverImage;
        if (string.IsNullOrEmpty(stored))
            stored = projects.CurrentProject?.CoverImage;
        return Task.FromResult(Resolve(stored));
    }

    /// <summary>
    /// Imports the picked image into the active book's image folder and records
    /// the resulting book-relative path as both the project and active-book
    /// portrait cover. A null or blank path clears the cover instead.
    /// </summary>
    [JsonRpcMethod("dashboard/setCover")]
    public async Task SetCoverAsync(string? path)
    {
        var projects = _workspace.Projects;
        var project = projects.CurrentProject
            ?? throw new InvalidOperationException("No project open.");

        var relative = await ImportOrClearAsync(path);
        project.CoverImage = relative;
        if (projects.ActiveBook != null)
            projects.ActiveBook.CoverImage = relative;
        await projects.SaveProjectAsync();
        // Keep the welcome-screen thumbnail in step with the new/removed cover.
        await _workspace.RefreshRecentCoverAsync();
    }

    /// <summary>
    /// Returns the active book's wide Dashboard banner as a project-relative
    /// path, or null when none. Prefers the book banner, then the project
    /// banner, then falls back to the book / project portrait cover so
    /// pre-split projects keep rendering their existing banner.
    /// </summary>
    [JsonRpcMethod("dashboard/getBanner")]
    public Task<string?> GetBannerAsync()
    {
        var projects = _workspace.Projects;
        var stored = projects.ActiveBook?.BannerImage;
        if (string.IsNullOrEmpty(stored))
            stored = projects.CurrentProject?.BannerImage;
        if (string.IsNullOrEmpty(stored))
            stored = projects.ActiveBook?.CoverImage;
        if (string.IsNullOrEmpty(stored))
            stored = projects.CurrentProject?.CoverImage;
        return Task.FromResult(Resolve(stored));
    }

    /// <summary>
    /// Imports the picked image and records the resulting book-relative path as
    /// both the project and active-book banner. A null or blank path clears the
    /// banner (the Dashboard then falls back to the portrait cover).
    /// </summary>
    [JsonRpcMethod("dashboard/setBanner")]
    public async Task SetBannerAsync(string? path)
    {
        var projects = _workspace.Projects;
        var project = projects.CurrentProject
            ?? throw new InvalidOperationException("No project open.");

        var relative = await ImportOrClearAsync(path);
        project.BannerImage = relative;
        if (projects.ActiveBook != null)
            projects.ActiveBook.BannerImage = relative;
        await projects.SaveProjectAsync();
    }

    private string? Resolve(string? stored)
        => string.IsNullOrEmpty(stored) ? null : _entities.ResolveProjectRelativeImage(stored);

    private async Task<string> ImportOrClearAsync(string? path)
        => string.IsNullOrWhiteSpace(path) ? string.Empty : await _entities.ImportImageAsync(path);

    // Ported verbatim from the Avalonia DashboardViewModel so results match.
    internal static List<(string Phrase, int Count)> FindEchoPhrases(string text, int minWords, int threshold)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var clean = Regex.Replace(text, "<[^>]+>", " ");
        clean = Regex.Replace(clean, @"\s+", " ").Trim().ToLowerInvariant();
        var words = clean.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < minWords) return [];

        var phraseCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var n = minWords; n <= Math.Min(minWords + 1, 4); n++)
        {
            for (var i = 0; i <= words.Length - n; i++)
            {
                var phrase = string.Join(' ', words.Skip(i).Take(n));
                if (IsStopPhrase(phrase)) continue;
                phraseCounts.TryGetValue(phrase, out var count);
                phraseCounts[phrase] = count + 1;
            }
        }

        return phraseCounts
            .Where(kv => kv.Value >= threshold)
            .OrderByDescending(kv => kv.Value)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
    }

    internal static bool IsStopPhrase(string phrase)
    {
        var words = phrase.Split(' ');
        var stopCount = words.Count(w => StopWords.Contains(w));
        return stopCount >= words.Length - 1;
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for",
        "of", "with", "by", "is", "was", "are", "were", "be", "been", "being",
        "have", "has", "had", "do", "does", "did", "will", "would", "could",
        "should", "may", "might", "shall", "can", "it", "its", "he", "she",
        "him", "her", "his", "they", "them", "their", "this", "that", "these",
        "those", "i", "you", "we", "not", "no", "if", "so", "as", "from"
    };
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
    IReadOnlyList<RecentActivityDto> RecentActivity);

public sealed record StatusBreakdownDto(string Status, int Count, int WordCount);

public sealed record ProjectOverviewDto(string ProjectName, ChapterOverviewDto[] Chapters);

public sealed record ChapterOverviewDto(
    string Title, int Words, int Readability, string? ReadabilityLevel, SceneOverviewDto[] Scenes);

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
