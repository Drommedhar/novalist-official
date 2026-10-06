using System.Globalization;
using System.Text.RegularExpressions;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class DashboardRpc
{
    /// <summary>Percent of a goal, capped, or 0 when no goal is set.</summary>
    private static int Percent(int current, int goal)
        => goal > 0 ? Math.Min(100, (int)Math.Round(current * 100.0 / goal)) : 0;

    /// <summary>
    /// Monday of the week <paramref name="day"/> falls in. Monday rather than
    /// Sunday because a writing week that starts mid-weekend cannot be caught up
    /// on the weekend, which is when most of the catching up happens.
    /// </summary>
    internal static DateOnly StartOfWeek(DateOnly day)
        => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>
    /// Words written since <paramref name="from"/>, and how many writing days
    /// are left in the horizon including today - which is what turns "behind" into
    /// "behind, with three days to fix it".
    /// </summary>
    private (int Written, int DaysLeft) HorizonProgress(
        string bookId, DateOnly from, DateOnly today)
    {
        var written = 0;
        for (var day = from; day <= today; day = day.AddDays(1))
            written += _workspace.WordHistory.TotalForDay(day, bookId);

        var goals = _workspace.Projects.ProjectSettings.WordCountGoals;
        var end = from.Day == 1 && from.Month == today.Month
            ? new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month))
            : from.AddDays(6);

        var daysLeft = 0;
        for (var day = today; day <= end; day = day.AddDays(1))
            if (goals.IsWritingDay(day)) daysLeft++;

        return (written, daysLeft);
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
}
