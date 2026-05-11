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
