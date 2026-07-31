using System.Globalization;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>One event whose date the engine moved, and where it moved it to.</summary>
public sealed record DependencyMove(string EventId, string Date, string EndDate);

/// <summary>What resolving a set of dependencies did, and what it could not do.</summary>
/// <param name="Moved">Events whose dates changed.</param>
/// <param name="Cycles">
/// Ids of events in a dependency cycle. Their dates are left alone: there is no
/// right answer, and guessing one would quietly corrupt a chronology.
/// </param>
public sealed record DependencyResult(
    IReadOnlyList<DependencyMove> Moved,
    IReadOnlyList<string> Cycles);

/// <summary>
/// Dates that follow other dates.
///
/// Every date in a project was an independent string, so moving a siege by a
/// week meant finding and retyping every date that hung off it - and the ones
/// that were missed did not announce themselves. They just quietly said the
/// wrong thing until somebody read the book and noticed the funeral happening
/// before the death.
///
/// An event can hang off another by an offset in days, measured from that
/// event's start or its end. Moving the anchor moves everything downstream of
/// it, and a span keeps its own length rather than being flattened to a point.
///
/// Two things are deliberately refused rather than guessed at:
///
/// - A cycle. Two events each waiting on the other have no answer, so both are
///   left where they are and reported.
/// - A locked event. The writer pinned that date; a cascade that overrode it
///   would make the lock a decoration. Its own dependents still follow it.
/// </summary>
public static class TimelineDependencies
{
    /// <summary>Offsets are counted from this end of the anchor.</summary>
    public const string FromStart = "start";

    /// <summary>Counted from the anchor's end, which is what "the week after
    /// the siege" means when the siege lasts a month.</summary>
    public const string FromEnd = "end";

    /// <summary>
    /// Recomputes every event that depends on another, in dependency order.
    /// The events are updated in place.
    /// </summary>
    public static DependencyResult Resolve(IReadOnlyList<TimelineManualEvent> events)
    {
        var byId = new Dictionary<string, TimelineManualEvent>(StringComparer.Ordinal);
        foreach (var e in events) byId[e.Id] = e;

        var moved = new List<DependencyMove>();
        var cycles = new List<string>();
        // Visited: resolved and settled. Visiting: on the current path, which
        // is how a cycle announces itself.
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        foreach (var e in events)
            Visit(e, byId, moved, cycles, visited, visiting);

        return new DependencyResult(moved, cycles);
    }

    private static void Visit(
        TimelineManualEvent target,
        Dictionary<string, TimelineManualEvent> byId,
        List<DependencyMove> moved,
        List<string> cycles,
        HashSet<string> visited,
        HashSet<string> visiting)
    {
        if (visited.Contains(target.Id)) return;
        if (!visiting.Add(target.Id))
        {
            // Already on this path: a cycle. Everything in it keeps its date.
            if (!cycles.Contains(target.Id, StringComparer.Ordinal)) cycles.Add(target.Id);
            return;
        }

        var anchorId = target.DependsOnEventId;
        if (!string.IsNullOrEmpty(anchorId)
            && !string.Equals(anchorId, target.Id, StringComparison.Ordinal)
            && byId.TryGetValue(anchorId, out var anchor))
        {
            // The anchor settles first, or the offset is measured from a date
            // that is itself about to move.
            Visit(anchor, byId, moved, cycles, visited, visiting);

            if (!cycles.Contains(target.Id, StringComparer.Ordinal)
                && !cycles.Contains(anchor.Id, StringComparer.Ordinal))
            {
                Apply(target, anchor, moved);
            }
        }
        else if (!string.IsNullOrEmpty(anchorId)
            && string.Equals(anchorId, target.Id, StringComparison.Ordinal))
        {
            // An event waiting on itself is the smallest cycle there is.
            if (!cycles.Contains(target.Id, StringComparer.Ordinal)) cycles.Add(target.Id);
        }

        visiting.Remove(target.Id);
        visited.Add(target.Id);
    }

    private static void Apply(
        TimelineManualEvent target, TimelineManualEvent anchor, List<DependencyMove> moved)
    {
        // The writer pinned this date. A cascade that moved it anyway would
        // make the lock a decoration.
        if (target.DateLocked) return;

        var from = string.Equals(target.DependsOnFrom, FromEnd, StringComparison.OrdinalIgnoreCase)
            ? Parse(anchor.EndDate) ?? Parse(anchor.Date)
            : Parse(anchor.Date);
        if (from == null) return;

        var start = from.Value.AddDays(target.DependsOnOffsetDays);
        // A span keeps its own length rather than collapsing to a point: a
        // three-week siege that moves is still three weeks long.
        var length = Length(target);
        var date = Format(start);
        var endDate = length.HasValue ? Format(start.AddDays(length.Value)) : target.EndDate;

        if (string.Equals(date, target.Date, StringComparison.Ordinal)
            && string.Equals(endDate, target.EndDate, StringComparison.Ordinal))
            return;

        target.Date = date;
        target.EndDate = endDate;
        moved.Add(new DependencyMove(target.Id, date, endDate));
    }

    /// <summary>How many days an event covers, or null when it is a point or
    /// its dates cannot be read.</summary>
    private static int? Length(TimelineManualEvent e)
    {
        var start = Parse(e.Date);
        var end = Parse(e.EndDate);
        if (start == null || end == null) return null;
        var days = (int)(end.Value - start.Value).TotalDays;
        return days >= 0 ? days : null;
    }

    /// <summary>
    /// The formats the timeline already reads. A date it cannot parse is left
    /// alone - an in-world calendar string is not a bug to be corrected.
    /// </summary>
    public static DateTime? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var s = value.Trim();
        string[] formats = ["yyyy-MM-dd", "yyyy-MM", "yyyy", "d.M.yyyy"];
        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(s, format, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var exact))
                return exact;
        }
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var loose)
            ? loose
            : null;
    }

    private static string Format(DateTime value)
        => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
