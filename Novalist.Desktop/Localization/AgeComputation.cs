using System;
using System.Globalization;
using Novalist.Core.Models;

namespace Novalist.Desktop.Localization;

/// <summary>
/// Computes age/interval from a birth date to a reference date with proper calendar math.
/// </summary>
public static class AgeComputation
{
    /// <summary>
    /// Compute the interval between two dates in the given unit, returned as a formatted string.
    /// </summary>
    public static string ComputeInterval(DateTime from, DateTime to, IntervalUnit unit)
    {
        if (from > to)
            return string.Empty;

        return unit switch
        {
            IntervalUnit.Years => ComputeYears(from, to).ToString(),
            IntervalUnit.Months => ComputeMonths(from, to).ToString(),
            IntervalUnit.Days => ((int)(to - from).TotalDays).ToString(),
            _ => string.Empty
        };
    }

    /// <summary>
    /// Compute the interval between a birth date and a reference date, returning
    /// a display-ready age string. If referenceDate is null, uses today.
    /// </summary>
    public static string ComputeAge(string? birthDateIso, string? referenceDateIso, IntervalUnit unit)
    {
        if (string.IsNullOrWhiteSpace(birthDateIso))
            return string.Empty;

        if (!DateTime.TryParse(birthDateIso, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var birth))
            return string.Empty;

        DateTime reference;
        if (!string.IsNullOrWhiteSpace(referenceDateIso) &&
            DateTime.TryParse(referenceDateIso, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var refDate))
        {
            reference = refDate;
        }
        else
        {
            reference = DateTime.Today;
        }

        if (birth > reference)
            return string.Empty;

        return ComputeInterval(birth, reference, unit);
    }

    private static int ComputeYears(DateTime from, DateTime to)
    {
        var years = to.Year - from.Year;
        if (to.Month < from.Month || (to.Month == from.Month && to.Day < from.Day))
            years--;
        return Math.Max(0, years);
    }

    private static int ComputeMonths(DateTime from, DateTime to)
    {
        var months = (to.Year - from.Year) * 12 + to.Month - from.Month;
        if (to.Day < from.Day)
            months--;
        return Math.Max(0, months);
    }
}
