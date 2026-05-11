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
