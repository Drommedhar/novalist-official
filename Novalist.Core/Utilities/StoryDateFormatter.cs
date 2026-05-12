using Novalist.Core.Models;

namespace Novalist.Core.Utilities;

/// <summary>
/// Formats a scene/chapter's effective in-world date for display in trees,
/// sidebars and the timeline. Combines the legacy <see cref="SceneData.Date"/>
/// / <see cref="ChapterData.Date"/> string with the optional
/// <see cref="StoryDateRange"/> so that times and multi-day ranges are visible.
///
/// Output shapes:
///   ""                       — nothing to show
///   "2024-10-22"             — single date, no time
///   "2024-10-22 18:00"       — single date, start time only
///   "2024-10-22 18:00-18:30" — single date, both times
///   "2024-10-22 23:30 - 2024-10-23 00:30" — multi-day range (times optional)
/// </summary>
public static class StoryDateFormatter
{
    public static bool HasAnyDate(string? date, StoryDateRange? range)
        => !string.IsNullOrWhiteSpace(date) || (range != null && range.HasValue);

    public static string Format(string? date, StoryDateRange? range)
    {
        if (range != null && range.HasValue)
            return FormatRange(range);

        return string.IsNullOrWhiteSpace(date) ? string.Empty : date!.Trim();
    }

    public static string FormatRange(StoryDateRange range)
    {
        var start = (range.Start ?? string.Empty).Trim();
        var end = (range.End ?? string.Empty).Trim();
        var startTime = (range.StartTime ?? string.Empty).Trim();
        var endTime = (range.EndTime ?? string.Empty).Trim();

        if (start.Length == 0 && end.Length == 0)
        {
            if (startTime.Length == 0 && endTime.Length == 0) return string.Empty;
            return JoinTimes(startTime, endTime);
        }

        var sameDate = end.Length == 0 || end == start;
        if (sameDate)
        {
            var times = JoinTimes(startTime, endTime);
            if (times.Length == 0) return start;
            return $"{start} {times}";
        }

        var left = startTime.Length == 0 ? start : $"{start} {startTime}";
        var right = endTime.Length == 0 ? end : $"{end} {endTime}";
        return $"{left} - {right}";
    }

    /// <summary>Returns the leading YYYY-MM-DD date if the formatted string
    /// starts with one — used by callers that want to derive a weekday name.</summary>
    public static string? ExtractLeadingDate(string? formatted)
    {
        if (string.IsNullOrEmpty(formatted) || formatted.Length < 10) return null;
        var s = formatted.AsSpan(0, 10);
        if (s[4] != '-' || s[7] != '-') return null;
        for (var i = 0; i < 10; i++)
        {
            if (i == 4 || i == 7) continue;
            if (s[i] < '0' || s[i] > '9') return null;
        }
        return formatted[..10];
    }

    private static string JoinTimes(string startTime, string endTime)
    {
        if (startTime.Length == 0 && endTime.Length == 0) return string.Empty;
        if (endTime.Length == 0) return startTime;
        if (startTime.Length == 0) return endTime;
        return startTime == endTime ? startTime : $"{startTime}-{endTime}";
    }
}
