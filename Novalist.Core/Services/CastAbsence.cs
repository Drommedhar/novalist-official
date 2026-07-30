namespace Novalist.Core.Services;

/// <summary>
/// How long a character is gone for, and whether they ever come back.
/// </summary>
/// <param name="LongestGap">
/// The most chapters in a row, between two appearances, where the character
/// does not appear. Chapters before their first appearance do not count: that
/// is an entrance, not a disappearance.
/// </param>
/// <param name="GapStartChapter">First chapter of that gap, zero-based.</param>
/// <param name="GapEndChapter">Last chapter of that gap, zero-based.</param>
/// <param name="ChaptersSinceLastSeen">
/// Chapters after their last appearance. A cast member who simply stops is the
/// other half of the question, and a gap measured between appearances cannot
/// see them - there is no second appearance to measure to.
/// </param>
public sealed record AbsenceRow(
    string EntityId,
    string Label,
    int TotalScenes,
    int LongestGap,
    int GapStartChapter,
    int GapEndChapter,
    int FirstChapter,
    int LastChapter,
    int ChaptersSinceLastSeen);

/// <summary>
/// Turns per-chapter presence into the question a revision asks: who dropped
/// out of act two.
///
/// Novalist has counted appearances per chapter for a while and only ever drawn
/// them as a grid, plus "last seen N chapters ago" for one entry at a time in
/// the Inspector. Reading a grid of forty rows to find the character who
/// vanished is exactly the work a report is for.
/// </summary>
public static class CastAbsence
{
    /// <summary>
    /// The cast ordered by how badly they disappear - longest gap first, then
    /// by how long ago they were last seen.
    /// </summary>
    /// <param name="minimumGap">
    /// Gaps shorter than this are not worth reporting. A character missing from
    /// one chapter is a scene, not a problem.
    /// </param>
    public static IReadOnlyList<AbsenceRow> From(
        IEnumerable<PresenceRow> presence, int chapterCount, int minimumGap = 2)
    {
        var rows = new List<AbsenceRow>();

        foreach (var entry in presence)
        {
            var counts = entry.ScenesPerChapter;
            var first = -1;
            var last = -1;
            for (var i = 0; i < counts.Count; i++)
            {
                if (counts[i] <= 0) continue;
                if (first < 0) first = i;
                last = i;
            }

            // Nobody who never appears has anything to be absent from.
            if (first < 0) continue;

            var (longest, gapStart, gapEnd) = LongestGap(counts, first, last);
            var since = chapterCount > 0 ? chapterCount - 1 - last : 0;

            if (longest < minimumGap && since < minimumGap) continue;

            rows.Add(new AbsenceRow(
                entry.EntityId, entry.Label, entry.TotalScenes,
                longest, gapStart, gapEnd, first, last, since));
        }

        return [.. rows
            .OrderByDescending(r => r.LongestGap)
            .ThenByDescending(r => r.ChaptersSinceLastSeen)
            .ThenBy(r => r.Label, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static (int Longest, int Start, int End) LongestGap(
        IReadOnlyList<int> counts, int first, int last)
    {
        var longest = 0;
        var bestStart = -1;
        var bestEnd = -1;
        var runStart = -1;

        for (var i = first; i <= last; i++)
        {
            if (counts[i] > 0)
            {
                runStart = -1;
                continue;
            }
            if (runStart < 0) runStart = i;
            var length = i - runStart + 1;
            if (length <= longest) continue;
            longest = length;
            bestStart = runStart;
            bestEnd = i;
        }

        return (longest, bestStart, bestEnd);
    }
}
