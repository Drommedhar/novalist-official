using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>One thing a gate found, and where.</summary>
/// <param name="RuleId">
/// Which gate. Stable and not localised, so a setting can turn one off and a
/// report can group by it.
/// </param>
/// <param name="Subject">
/// What the finding is about - an entry's name, or empty when the scene itself
/// is the subject. Never prose.
/// </param>
public sealed record ContinuityFinding(
    string RuleId,
    string ChapterGuid,
    string SceneId,
    string Subject,
    string Detail);

/// <summary>A scene as the gates need to see it.</summary>
/// <param name="ReadingIndex">Its place in reading order, from zero.</param>
public sealed record GateScene(
    string ChapterGuid,
    string SceneId,
    int ReadingIndex,
    IReadOnlyList<string> Cast,
    string? Date,
    string? NarrativeMode);

/// <summary>An entry as the gates need to see it.</summary>
/// <param name="GoneFromReadingIndex">
/// Where the entry leaves the story, or null when it never does.
/// </param>
public sealed record GateEntity(
    string Id,
    string Name,
    int? GoneFromReadingIndex);

/// <summary>
/// Deterministic continuity checks over a whole book.
///
/// Novalist could tell a writer what a scene was like and never whether the
/// book contradicted itself. The checks that existed were per-scene and about
/// prose - head-hopping, watch words - so a character standing in a scene two
/// chapters after their funeral was nobody's job to notice.
///
/// Every rule here is deterministic and offline. No model is asked, nothing is
/// inferred from names, and a rule that cannot decide stays quiet: a
/// continuity report that cries wolf is one the writer stops reading, and then
/// it may as well not run.
/// </summary>
public static class ContinuityGates
{
    /// <summary>An entry appears after the point it left the story.</summary>
    public const string GoneThenPresent = "gone-then-present";

    /// <summary>A scene's cast names an entry the Codex no longer has.</summary>
    public const string UnknownCast = "unknown-cast";

    /// <summary>
    /// A scene dated before the one the reader met last, with nothing saying it
    /// is meant to be. A flashback says so; this is the one that does not.
    /// </summary>
    public const string TimeRunsBackwards = "time-runs-backwards";

    /// <summary>Every rule, so a settings screen can list them without a second list.</summary>
    public static IReadOnlyList<string> AllRules { get; } =
        [GoneThenPresent, UnknownCast, TimeRunsBackwards];

    /// <summary>
    /// Runs the gates over a book in reading order.
    /// </summary>
    /// <param name="disabledRules">
    /// Rules the writer turned off. A gate that keeps reporting something they
    /// have decided is fine is a gate they will stop reading.
    /// </param>
    public static IReadOnlyList<ContinuityFinding> Run(
        IReadOnlyList<GateScene> scenes,
        IReadOnlyList<GateEntity> entities,
        IReadOnlySet<string>? disabledRules = null)
    {
        var findings = new List<ContinuityFinding>();
        var byId = new Dictionary<string, GateEntity>(StringComparer.Ordinal);
        foreach (var entity in entities) byId[entity.Id] = entity;

        var enabled = disabledRules ?? new HashSet<string>(StringComparer.Ordinal);
        var ordered = scenes.OrderBy(s => s.ReadingIndex).ToList();

        if (!enabled.Contains(GoneThenPresent) || !enabled.Contains(UnknownCast))
        {
            foreach (var scene in ordered)
            {
                foreach (var castId in scene.Cast ?? [])
                {
                    if (!byId.TryGetValue(castId, out var entity))
                    {
                        if (!enabled.Contains(UnknownCast))
                        {
                            findings.Add(new ContinuityFinding(
                                UnknownCast, scene.ChapterGuid, scene.SceneId, string.Empty,
                                // The id, not a name: there is no entry to take a
                                // name from, and an id is what the writer needs to
                                // find the stale reference.
                                castId));
                        }
                        continue;
                    }

                    if (!enabled.Contains(GoneThenPresent)
                        && entity.GoneFromReadingIndex is { } gone
                        && scene.ReadingIndex > gone)
                    {
                        findings.Add(new ContinuityFinding(
                            GoneThenPresent, scene.ChapterGuid, scene.SceneId,
                            entity.Name, string.Empty));
                    }
                }
            }
        }

        if (!enabled.Contains(TimeRunsBackwards))
            findings.AddRange(Backwards(ordered));

        return findings;
    }

    private static IEnumerable<ContinuityFinding> Backwards(IReadOnlyList<GateScene> ordered)
    {
        DateTime? previous = null;
        string previousDate = string.Empty;

        foreach (var scene in ordered)
        {
            var date = TimelineDependencies.Parse(scene.Date);
            if (date == null) continue;

            // A scene that says it is out of order is not a contradiction. That
            // is exactly what narrative mode is for.
            var explained = !string.IsNullOrWhiteSpace(scene.NarrativeMode);

            // An explained scene is neither a finding nor a new baseline: a
            // flashback is not where the chronology resumes.
            if (explained) continue;

            if (previous != null && date < previous)
            {
                yield return new ContinuityFinding(
                    TimeRunsBackwards, scene.ChapterGuid, scene.SceneId, string.Empty,
                    // Both dates, so the writer can see the jump without opening
                    // either scene. A date is not story content.
                    $"{previousDate} -> {scene.Date}");
            }

            // The baseline follows the book, including backwards. Holding it at
            // the highest date seen would turn one out-of-order scene into a
            // finding on every scene after it - a report that cries wolf is one
            // the writer stops reading, and then it may as well not run.
            previous = date;
            previousDate = scene.Date ?? string.Empty;
        }
    }

    /// <summary>
    /// Where an entry leaves the story, from its state overrides, or null.
    ///
    /// The overrides say where by chapter and scene; the gates need one number
    /// in reading order, so the caller hands over the index of each.
    /// </summary>
    public static int? GoneFrom(
        IEnumerable<EntityStateOverride> overrides,
        Func<string, string?, int?> readingIndexOf)
    {
        int? earliest = null;
        foreach (var state in overrides)
        {
            if (!state.Gone) continue;
            var index = readingIndexOf(state.Chapter, state.Scene);
            if (index == null) continue;
            if (earliest == null || index < earliest) earliest = index;
        }
        return earliest;
    }
}
