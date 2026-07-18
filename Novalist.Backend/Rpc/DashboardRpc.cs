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

        return new DashboardDto(
            projects.CurrentProject!.Name,
            totalWords,
            chapters.Count,
            sceneCount,
            characters.Count,
            locations.Count,
            TextStatistics.EstimateReadingTime(totalWords),
            chapters.Count > 0 ? (int)Math.Round((double)totalWords / chapters.Count) : 0,
            dailyCurrent,
            goals.DailyGoal,
            goals.DailyGoal > 0 ? Math.Min(100, (int)Math.Round(dailyCurrent * 100.0 / goals.DailyGoal)) : 0,
            goals.ProjectGoal,
            goals.ProjectGoal > 0 ? Math.Min(100, (int)Math.Round(totalWords * 100.0 / goals.ProjectGoal)) : 0,
            goals.Deadline,
            _workspace.WordHistory.TotalForDay(today, book.Id),
            _workspace.WordHistory.CurrentStreak(today, Math.Max(1, goals.DailyGoal), book.Id),
            statusAgg.Select(kv => new StatusBreakdownDto(kv.Key, kv.Value.Count, kv.Value.Words)).ToArray(),
            pacing.ToArray(),
            maxChapterWords,
            FindEchoPhrases(allText.ToString(), 3, 5).Take(20)
                .Select(e => new EchoPhraseDto(e.Phrase, e.Count)).ToArray(),
            bars.ToArray());
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
    int TodayWords,
    int CurrentStreak,
    IReadOnlyList<StatusBreakdownDto> StatusBreakdown,
    IReadOnlyList<ChapterPacingDto> ChapterPacing,
    int MaxChapterWords,
    IReadOnlyList<EchoPhraseDto> EchoPhrases,
    IReadOnlyList<WordHistoryBarDto> WordHistory);

public sealed record StatusBreakdownDto(string Status, int Count, int WordCount);

public sealed record ChapterPacingDto(string Title, int Words);

public sealed record EchoPhraseDto(string Phrase, int Count);

public sealed record WordHistoryBarDto(string Date, int Words, bool MetGoal);
