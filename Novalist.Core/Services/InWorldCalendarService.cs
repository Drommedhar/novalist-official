using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public sealed partial class InWorldCalendarService : IInWorldCalendarService
{
    [GeneratedRegex(@"^\s*(-?\d+)[\.\-/](\d+)[\.\-/](\d+)\s*$", RegexOptions.Compiled)]
    private static partial Regex YmdRegex();

    public long? Parse(string raw, InWorldCalendar? calendar)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var cal = calendar ?? new InWorldCalendar();

        if (cal.Type == InWorldCalendarType.Gregorian)
        {
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
                || DateTime.TryParse(raw, CultureInfo.CurrentCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dt))
                return dt.ToOADate() is var d && double.IsFinite(d) ? (long)d : null;
            return null;
        }

        // Custom: expect "Y.M.D" or "Y-M-D" or "Y/M/D".
        var m = YmdRegex().Match(raw);
        if (!m.Success) return null;
        var year = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var month = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var day = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);

        if (cal.DaysPerMonth.Count == 0) return null;
        if (month < 1 || month > cal.DaysPerMonth.Count) return null;
        if (day < 1 || day > cal.DaysPerMonth[month - 1]) return null;

        long ordinal = year * cal.CustomYearLength;
        for (int i = 0; i < month - 1; i++) ordinal += cal.DaysPerMonth[i];
        ordinal += day - 1;
        return ordinal;
    }

    /// <summary>
    /// The year a date falls in, or null when it cannot be read. Used for
    /// grouping a chronology into years without re-parsing the whole date.
    /// </summary>
    public long? YearOf(string raw, InWorldCalendar? calendar)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var cal = calendar ?? new InWorldCalendar();

        if (cal.Type == InWorldCalendarType.Gregorian)
        {
            return DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
                ? dt.Year
                : null;
        }

        var m = YmdRegex().Match(raw);
        return m.Success ? long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>
    /// A year written the way the book reckons it: "342 AC", "12 Before the
    /// Fall". Falls back to the single year label, and to the bare number when
    /// there is not even one - printing a negative year as "-12" is what makes
    /// a fantasy chronology unreadable.
    /// </summary>
    public string FormatYear(long year, InWorldCalendar? calendar)
    {
        var cal = calendar ?? new InWorldCalendar();

        // The latest era that starts at or before this year, so a story
        // spanning a reckoning change reads correctly on both sides.
        CalendarEra? era = null;
        foreach (var candidate in cal.Eras)
            if (candidate.StartYear <= year && (era == null || candidate.StartYear > era.StartYear))
                era = candidate;

        if (era == null)
        {
            var label = cal.YearLabel.Trim();
            return label.Length == 0
                ? year.ToString(CultureInfo.InvariantCulture)
                : $"{year.ToString(CultureInfo.InvariantCulture)} {label}";
        }

        // A counting-down era measures backwards from the era that follows it,
        // which is where "12 Before the Fall" comes from.
        if (!era.CountsDown)
            return $"{(year - era.StartYear + 1).ToString(CultureInfo.InvariantCulture)} {era.Name}".Trim();

        long? next = null;
        foreach (var candidate in cal.Eras)
            if (candidate.StartYear > era.StartYear && (next == null || candidate.StartYear < next))
                next = candidate.StartYear;

        var counted = (next ?? 0) - year;
        return $"{counted.ToString(CultureInfo.InvariantCulture)} {era.Name}".Trim();
    }

    public long? DiffDays(string from, string to, InWorldCalendar? calendar)
    {
        var a = Parse(from, calendar);
        var b = Parse(to, calendar);
        if (a == null || b == null) return null;
        return b - a;
    }

    public string AddDays(string raw, long days, InWorldCalendar? calendar)
    {
        var cal = calendar ?? new InWorldCalendar();
        if (cal.Type == InWorldCalendarType.Gregorian)
        {
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
                return dt.AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return raw;
        }

        var ordinal = Parse(raw, calendar);
        if (ordinal == null || cal.CustomYearLength == 0) return raw;
        ordinal += days;

        long year = ordinal.Value / cal.CustomYearLength;
        long dayOfYear = ordinal.Value % cal.CustomYearLength;
        if (dayOfYear < 0) { dayOfYear += cal.CustomYearLength; year--; }

        int month = 1;
        foreach (var dpm in cal.DaysPerMonth)
        {
            if (dayOfYear < dpm) break;
            dayOfYear -= dpm;
            month++;
        }
        var day = dayOfYear + 1;
        return $"{year}.{month}.{day}";
    }

    public string DurationLabel(StoryDateRange? range, InWorldCalendar? calendar)
    {
        if (range == null || !range.HasValue) return string.Empty;
        if (string.IsNullOrWhiteSpace(range.Start) || string.IsNullOrWhiteSpace(range.End))
            return string.Empty;

        var diff = DiffDays(range.Start, range.End, calendar);
        if (diff == null) return string.Empty;
        var d = Math.Abs(diff.Value);
        if (d == 0) return "same day";
        if (d == 1) return "1 day";
        if (d < 14) return $"{d} days";
        if (d < 60) return $"{d / 7} weeks";
        if (d < 730) return $"{d / 30} months";
        return $"{d / 365} years";
    }
}
