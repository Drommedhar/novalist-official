using System;
using System.Collections.Generic;
using System.Linq;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>One research item worth putting in front of the writer, and why.</summary>
/// <param name="Reason">
/// What matched, as an entity name or a tag. Shown beside the item: a list of
/// suggestions with no reason attached is a list the writer has to open one by
/// one to find out why it is there, which costs more than the Research view.
/// </param>
public sealed record ResearchSuggestion(ResearchItem Item, string Reason, int Score);

/// <summary>
/// The research the open scene is about.
///
/// Research reached the writer in two places, and neither of them was where
/// they were writing: the Research view, which means leaving the scene, and an
/// entity's Wiki article, which means already knowing which entity to look up.
/// So the note that says "check whether the bridge existed in 1755" sat filed
/// correctly and unread while the bridge got written.
///
/// Nothing here asks a model. The scene already names its cast, its point of
/// view, its location and its tags; a research item already names the entries
/// it is about and carries tags of its own. The overlap is the answer, and a
/// deterministic one can be trusted in a way a guess cannot - a suggestion the
/// writer has to double-check is worse than none.
/// </summary>
public static class SceneResearch
{
    /// <summary>Highest first. Beyond a handful this stops being a suggestion
    /// and becomes the Research view in a narrower column.</summary>
    public const int MaxSuggestions = 6;

    private const int EntityMatch = 10;
    private const int TagMatch = 4;

    /// <summary>
    /// Research items relevant to a scene, best first.
    /// </summary>
    /// <param name="entityIds">
    /// The Codex entries the scene involves - its cast, its point of view, its
    /// location, whatever it is about. Ids, not names: two characters can share
    /// a name and a research note points at one of them.
    /// </param>
    /// <param name="sceneTags">The scene's own tags.</param>
    /// <param name="names">
    /// Entity id to display name, for the reason shown beside a match. An id
    /// the map does not have still matches; it just reports the tag or nothing
    /// rather than a raw guid.
    /// </param>
    public static IReadOnlyList<ResearchSuggestion> Suggest(
        IEnumerable<ResearchItem>? items,
        IEnumerable<string>? entityIds,
        IEnumerable<string>? sceneTags,
        IReadOnlyDictionary<string, string>? names = null)
    {
        if (items == null) return [];

        var ids = new HashSet<string>(
            (entityIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.OrdinalIgnoreCase);
        var tags = new HashSet<string>(
            (sceneTags ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()),
            StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0 && tags.Count == 0) return [];

        var suggestions = new List<ResearchSuggestion>();
        foreach (var item in items)
        {
            if (item == null) continue;

            // An entity match is the stronger signal and the better reason: the
            // writer linked this note to this character on purpose, where a tag
            // may be shared by forty notes.
            var entity = (item.EntityRefs ?? []).FirstOrDefault(ids.Contains);
            var tag = (item.Tags ?? [])
                .FirstOrDefault(t => !string.Equals(t, ResearchItem.InboxTag, StringComparison.OrdinalIgnoreCase)
                    && tags.Contains(t.Trim()));

            var score = 0;
            if (entity != null) score += EntityMatch;
            if (tag != null) score += TagMatch;
            if (score == 0) continue;

            var reason = entity != null
                ? names != null && names.TryGetValue(entity, out var name) && !string.IsNullOrWhiteSpace(name)
                    ? name
                    : tag ?? string.Empty
                : tag!;
            suggestions.Add(new ResearchSuggestion(item, reason, score));
        }

        // Score, then the writer's own order, then title - so the list is the
        // same on every open. A suggestion panel that reshuffles itself is one
        // nobody learns the shape of.
        return [.. suggestions
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Item.Order)
            .ThenBy(s => s.Item.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxSuggestions)];
    }
}
