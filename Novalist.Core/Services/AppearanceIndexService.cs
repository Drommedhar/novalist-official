using System.Text.RegularExpressions;
using Novalist.Core.Models;
using Novalist.Core.Utilities;

namespace Novalist.Core.Services;

/// <summary>
/// One scene in which an entity is mentioned, carrying the manuscript
/// coordinates (for reading-order sorting) and the display fields the Wiki's
/// Appearances timeline needs — resolved in the same pass so no downstream
/// re-lookup is required.
/// </summary>
public sealed record SceneAppearance(
    string ChapterGuid,
    string SceneId,
    int ChapterOrder,
    int SceneOrder,
    string ChapterTitle,
    string SceneTitle,
    string? Synopsis,
    string StoryDate,
    string? IsoDate,
    string Pov,
    IReadOnlyList<string> PlotlineIds,
    IReadOnlyList<string> EntityIds);

/// <summary>
/// Builds, in a single pass over the active book's scenes, the map from each
/// entity id to the scenes that mention it. Mentions are read from the
/// persisted <c>&lt;span class="nv-entity-mention" data-entity-id="..."&gt;</c>
/// markers the editor writes into scene HTML — the same author-confirmed link
/// the rename sync relies on. Used by the Wiki view's Appearances timeline and
/// its derived stats (POV scenes, co-appearances, plotlines).
/// </summary>
public sealed class AppearanceIndexService
{
    /// <summary>Extracts the entity id from a persisted mention span attribute.
    /// Public so callers and tests share one pattern.</summary>
    public static readonly Regex EntityIdRegex = new(
        @"data-entity-id\s*=\s*[""']([^""']+)[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The entity ids explicitly `@`-mentioned in a scene's HTML, in the order they
    /// first appear and without duplicates. These markers are author-confirmed, so
    /// they are the strongest available statement about who a scene involves —
    /// unlike name matching, they cannot be a false positive.
    /// </summary>
    public static IReadOnlyList<string> ExtractMentionIds(string? html)
    {
        if (string.IsNullOrEmpty(html)
            || html.IndexOf("nv-entity-mention", StringComparison.OrdinalIgnoreCase) < 0)
            return [];

        var entityIds = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in EntityIdRegex.Matches(html))
        {
            var entityId = match.Groups[1].Value;
            if (entityId.Length > 0 && seen.Add(entityId))
                entityIds.Add(entityId);
        }
        return entityIds;
    }

    private readonly IProjectService _projects;

    public AppearanceIndexService(IProjectService projects)
    {
        _projects = projects;
    }

    /// <summary>
    /// Scans every scene once and returns a map of entity id to the (deduped,
    /// reading-ordered) scenes that mention it, each enriched with its title,
    /// synopsis, resolved story date, effective POV, plotlines, and the full set
    /// of entities co-mentioned in the scene. Scenes with no mention spans are
    /// skipped without further cost. <paramref name="characters"/> is used only
    /// to detect a scene's POV when no manual override is set.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<SceneAppearance>>> BuildAsync(
        IReadOnlyList<CharacterData> characters)
    {
        var index = new Dictionary<string, List<SceneAppearance>>(StringComparer.Ordinal);

        foreach (var chapter in _projects.GetChaptersOrdered())
        {
            foreach (var scene in _projects.GetScenesForChapter(chapter.Guid))
            {
                var html = await _projects.ReadSceneContentAsync(chapter, scene);
                if (string.IsNullOrEmpty(html) ||
                    html.IndexOf("nv-entity-mention", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var entityIds = ExtractMentionIds(html);
                if (entityIds.Count == 0)
                    continue;

                var storyDate = SceneStoryDate.Resolve(chapter, scene);
                var pov = ResolvePov(scene, html, characters);
                // Immutable and identical for every entity in this scene, so build once and share.
                var appearance = new SceneAppearance(
                    chapter.Guid, scene.Id, chapter.Order, scene.Order,
                    chapter.Title, scene.Title,
                    string.IsNullOrWhiteSpace(scene.Synopsis) ? null : scene.Synopsis,
                    storyDate,
                    StoryDateFormatter.ExtractLeadingDate(storyDate),
                    pov,
                    scene.PlotlineIds ?? [],
                    entityIds);

                foreach (var entityId in entityIds)
                {
                    if (!index.TryGetValue(entityId, out var list))
                    {
                        list = new List<SceneAppearance>();
                        index[entityId] = list;
                    }
                    list.Add(appearance);
                }
            }
        }

        return index.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<SceneAppearance>)kvp.Value,
            StringComparer.Ordinal);
    }

    /// <summary>Effective POV: the manual override if set, else best-effort
    /// detection from the scene text against the character codex.</summary>
    private static string ResolvePov(SceneData scene, string html, IReadOnlyList<CharacterData> characters)
    {
        var manual = scene.AnalysisOverrides?.Pov;
        if (!string.IsNullOrWhiteSpace(manual))
            return manual.Trim();
        return PovDetector.Detect(TextDiff.StripHtml(html), characters);
    }
}
