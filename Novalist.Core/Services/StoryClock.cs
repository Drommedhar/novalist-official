using System.Globalization;
using Novalist.Core.Models;
using Novalist.Core.Utilities;

namespace Novalist.Core.Services;

/// <summary>One scene's place in story time, after the relative offsets resolve.</summary>
public sealed record ResolvedSceneTime(
    string SceneId,
    /// <summary>The date as it should be shown, or empty when nothing anchors it.</summary>
    string Display,
    /// <summary>Sortable ISO date, or null when nothing anchors it.</summary>
    string? Iso,
    /// <summary>True when this came from a relative offset rather than a date
    /// the writer typed. The UI shows the two differently: one is a statement,
    /// the other is arithmetic.</summary>
    bool Derived);

/// <summary>
/// Turns "the next morning" into a date.
///
/// Novalist stored absolute dates and nothing else, so a writer who knows a
/// scene is two hours after the last one - and neither knows nor cares which day
/// - had to invent a date or leave it blank. Blank meant the scene fell out of
/// the Calendar and the Timeline, which is how a whole book ends up undated.
///
/// The walk is one pass in reading order. Every scene with a date of its own
/// re-anchors the clock; a scene with only an offset is placed relative to
/// wherever the clock currently is. Scenes before the first real date stay
/// unanchored rather than being hung off an invented epoch, because a book whose
/// first date arrives in chapter nine has eight chapters with no answer and
/// pretending otherwise would put them all on the wrong day.
/// </summary>
public static class StoryClock
{
    /// <summary>
    /// Resolves every scene in reading order.
    /// </summary>
    /// <param name="scenes">
    /// The scenes in reading order, each with the chapter it belongs to so a
    /// chapter-level date can anchor the ones inside it.
    /// </param>
    public static IReadOnlyList<ResolvedSceneTime> Resolve(
        IEnumerable<(ChapterData Chapter, SceneData Scene)> scenes)
    {
        var results = new List<ResolvedSceneTime>();
        DateTime? clock = null;

        foreach (var (chapter, scene) in scenes)
        {
            var written = SceneStoryDate.Resolve(chapter, scene);

            // A date on the scene itself always wins and re-anchors the clock -
            // relative time is for the gaps, never an override.
            var own = FirstParsable(scene.DateRange?.Start, scene.Date);
            if (own != null)
            {
                clock = own;
                results.Add(new ResolvedSceneTime(scene.Id, written, Iso(own.Value), false));
                continue;
            }

            // Then the scene's own offset, which beats a date inherited from the
            // chapter: the chapter date says when the chapter starts, and a
            // scene saying "one day later" is the more specific statement.
            if (scene.RelativeTime is { } offset && clock != null)
            {
                clock = clock.Value.AddMinutes(offset.TotalMinutes);
                results.Add(new ResolvedSceneTime(scene.Id, Iso(clock.Value), Iso(clock.Value), true));
                continue;
            }

            // Failing both, the chapter's date - which is what a scene with
            // nothing of its own has always shown.
            var inherited = FirstParsable(chapter.DateRange?.Start, chapter.Date);
            if (inherited != null)
            {
                clock = inherited;
                results.Add(new ResolvedSceneTime(scene.Id, written, Iso(inherited.Value), false));
                continue;
            }

            // Either no statement at all, or a relative one with nothing to be
            // relative to yet. Whatever was written stands, unanchored.
            results.Add(new ResolvedSceneTime(scene.Id, written, null, false));
        }

        return results;
    }

    /// <summary>
    /// A phrase for an offset - "2 hours later", "the next day" - built from the
    /// unit rather than from a formatted duration, because "1 days later" reads
    /// like a bug and is the first thing anybody notices.
    /// </summary>
    public static string Describe(RelativeStoryTime? offset)
    {
        if (offset == null || offset.Amount == 0) return string.Empty;

        var amount = Math.Abs(offset.Amount);
        var unit = offset.Unit switch
        {
            StoryTimeUnit.Minutes => amount == 1 ? "minute" : "minutes",
            StoryTimeUnit.Hours => amount == 1 ? "hour" : "hours",
            StoryTimeUnit.Days => amount == 1 ? "day" : "days",
            _ => amount == 1 ? "week" : "weeks"
        };
        return offset.Amount > 0
            ? $"{amount} {unit} later"
            : $"{amount} {unit} earlier";
    }

    /// <summary>The first of these that parses as a date, or null.</summary>
    private static DateTime? FirstParsable(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (DateTime.TryParse(
                    candidate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return parsed;
        }
        return null;
    }

    private static string Iso(DateTime value)
        // Time only where there is one: a scene two hours after another has a
        // time, a scene two days after one does not need to claim midnight.
        => value.TimeOfDay == TimeSpan.Zero
            ? value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
