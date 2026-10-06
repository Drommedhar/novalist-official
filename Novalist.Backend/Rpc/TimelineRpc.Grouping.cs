using System.Globalization;
using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class TimelineRpc
{
    private static string? Iso(DateTime? date) => date?.ToString("yyyy-MM-dd");

    // Ported verbatim from TimelineViewModel so grouping matches the Avalonia app.
    /// <summary>
    /// Groups a chronology by in-world year, ordered by the calendar's own
    /// day arithmetic. Years are labelled the way the book reckons them, so a
    /// chronology reads as "342 AC" rather than as a bare number.
    /// </summary>
    private static TimelineGroupDto[] GroupByInWorldYear(
        IReadOnlyList<TimelineEventDto> events, Core.Models.InWorldCalendar calendar)
    {
        var service = new InWorldCalendarService();
        return [.. events
            .OrderBy(e => service.Parse(e.DateStr ?? string.Empty, calendar) == null ? 1 : 0)
            .ThenBy(e => service.Parse(e.DateStr ?? string.Empty, calendar) ?? 0)
            .ThenBy(e => e.ChapterOrder)
            .GroupBy(e => service.YearOf(e.DateStr ?? string.Empty, calendar))
            .Select(g => new TimelineGroupDto(
                g.Key?.ToString(CultureInfo.InvariantCulture) ?? "?",
                g.Key == null ? "?" : service.FormatYear(g.Key.Value, calendar),
                [.. g]))];
    }

    internal static DateTime? ParseDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return null;
        var s = dateStr.Trim();
        if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
            return iso;
        if (DateTime.TryParseExact(s, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var ym))
            return ym;
        if (DateTime.TryParseExact(s, "yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var y))
            return y;
        if (DateTime.TryParseExact(s, "d.M.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var eu))
            return eu;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fallback))
            return fallback;
        return null;
    }

    internal static string GroupKey(DateTime? date, string zoom)
    {
        if (!date.HasValue) return "no-date";
        var d = date.Value;
        return zoom switch
        {
            "year" => $"{d.Year}",
            "day" => $"{d.Year}-{d.Month:D2}-{d.Day:D2}",
            _ => $"{d.Year}-{d.Month:D2}"
        };
    }

    internal static string GroupLabel(string key, string zoom)
    {
        if (key == "no-date") return "???";
        var parts = key.Split('-').Select(int.Parse).ToArray();
        var months = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        return zoom switch
        {
            "year" => $"{parts[0]}",
            "day" => $"{months[parts[1] - 1]} {parts[2]}, {parts[0]}",
            _ => $"{months[parts[1] - 1]} {parts[0]}"
        };
    }
}
