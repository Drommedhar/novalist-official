using Novalist.Core.Models;

namespace Novalist.Core.Services;

public interface IInWorldCalendarService
{
    /// <summary>Parse a date string against the given calendar. Returns the
    /// ordinal day-since-epoch as a long. Null if parsing fails.</summary>
    long? Parse(string raw, InWorldCalendar? calendar);

    /// <summary>Difference in days between two date strings, or null when
    /// either cannot be parsed.</summary>
    long? DiffDays(string from, string to, InWorldCalendar? calendar);

    /// <summary>Add N days to a parsed date and return the formatted result,
    /// or the original string when arithmetic isn't possible.</summary>
    string AddDays(string raw, long days, InWorldCalendar? calendar);

    /// <summary>Human-friendly duration label ("3 days", "2 weeks", "—").
    /// Empty when the range carries no parseable values.</summary>
    string DurationLabel(StoryDateRange? range, InWorldCalendar? calendar);
}
