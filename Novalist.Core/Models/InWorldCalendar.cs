using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Novalist.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InWorldCalendarType
{
    Gregorian,
    Custom
}

/// <summary>
/// A named stretch of years - "AC", "Before the Fall", "Fourth Age".
/// </summary>
public class CalendarEra
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The first year of the era. Negative years are ordinary here: a
    /// reckoning that counts down to a founding needs them.
    /// </summary>
    [JsonPropertyName("startYear")]
    public long StartYear { get; set; }

    /// <summary>
    /// Whether years inside it count down towards the next era rather than up.
    /// This is what "12 Before the Fall" means, and printing it as "-12" is
    /// what makes a fantasy chronology unreadable.
    /// </summary>
    [JsonPropertyName("countsDown")]
    public bool CountsDown { get; set; }
}

/// <summary>
/// Per-book calendar configuration. Drives parsing/formatting of
/// <see cref="StoryDateRange"/> values. Default = Gregorian.
/// </summary>
public class InWorldCalendar
{
    [JsonPropertyName("type")]
    public InWorldCalendarType Type { get; set; } = InWorldCalendarType.Gregorian;

    /// <summary>Era / year-suffix label (e.g. "AC", "of the Fourth Age").</summary>
    [JsonPropertyName("yearLabel")]
    public string YearLabel { get; set; } = string.Empty;

    /// <summary>Names of months in order. Ignored when Type=Gregorian.</summary>
    [JsonPropertyName("monthNames")]
    public List<string> MonthNames { get; set; } = [];

    /// <summary>Days per month (parallel to MonthNames). Ignored when Type=Gregorian.</summary>
    [JsonPropertyName("daysPerMonth")]
    public List<int> DaysPerMonth { get; set; } = [];

    /// <summary>
    /// Named stretches of years, each starting at a year number. A date's era
    /// is the latest one that starts at or before it, so a story spanning a
    /// reckoning change reads correctly on both sides of it. Empty means the
    /// single <see cref="YearLabel"/> applies throughout.
    /// </summary>
    [JsonPropertyName("eras")]
    public List<CalendarEra> Eras { get; set; } = [];

    /// <summary>Names of weekdays in order. Ignored when Type=Gregorian.</summary>
    [JsonPropertyName("weekdayNames")]
    public List<string> WeekdayNames { get; set; } = [];

    /// <summary>Days in a "year" for the custom calendar — derived from
    /// DaysPerMonth if the user did not specify months. Ignored for Gregorian.</summary>
    [JsonIgnore]
    public int CustomYearLength
    {
        get
        {
            int sum = 0;
            foreach (var d in DaysPerMonth) sum += d;
            return sum;
        }
    }
}
