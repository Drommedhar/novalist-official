using System.Text.RegularExpressions;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>How much of the book one thing accounts for.</summary>
public sealed record DistributionRow(
    string Key,
    string Label,
    int SceneCount,
    int WordCount,
    int Percent);

/// <summary>Which chapters an entity appears in, and how often.</summary>
public sealed record PresenceRow(
    string EntityId,
    string Label,
    int TotalScenes,
    IReadOnlyList<int> ScenesPerChapter);

/// <summary>Everything the whole-book charts read from.</summary>
public sealed class BookAnalytics
{
    public IReadOnlyList<string> ChapterTitles { get; init; } = [];

    /// <summary>Scenes and words per POV character, commonest first. An empty
    /// key is the scenes with no POV set.</summary>
    public IReadOnlyList<DistributionRow> Pov { get; init; } = [];

    /// <summary>Scenes and words per act.</summary>
    public IReadOnlyList<DistributionRow> Acts { get; init; } = [];

    /// <summary>Which chapters each character appears in.</summary>
    public IReadOnlyList<PresenceRow> Characters { get; init; } = [];

    /// <summary>Which chapters each location appears in.</summary>
    public IReadOnlyList<PresenceRow> Locations { get; init; } = [];

    /// <summary>
    /// Entities the Codex has that the manuscript never mentions.
    ///
    /// The most useful thing on the page: a character invented in the planning
    /// and then quietly dropped is invisible until something counts.
    /// </summary>
    public IReadOnlyList<string> Unused { get; init; } = [];
}

/// <summary>
/// Where things sit across the whole book, rather than one scene at a time.
///
/// Novalist computed POV and mentions per scene and only ever showed them for
/// the scene in view - so "which character is this book actually about" and
/// "have I forgotten this location since chapter two" had no answer anywhere.
/// </summary>
public sealed partial class BookAnalyticsService
{
    private readonly IProjectService _projectService;
    private readonly IEntityService _entityService;

    public BookAnalyticsService(IProjectService projectService, IEntityService entityService)
    {
        _projectService = projectService;
        _entityService = entityService;
    }

    /// <summary>Mention spans carry the entity id, which is what makes presence
    /// exact rather than a name search that a shared first name would confuse.</summary>
    [GeneratedRegex(@"data-entity-id\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex MentionIdRegex();

    public async Task<BookAnalytics> ComputeAsync()
    {
        var chapters = _projectService.GetChaptersOrdered();
        if (chapters.Count == 0) return new BookAnalytics();

        var characters = await _entityService.LoadCharactersAsync();
        var locations = await _entityService.LoadLocationsAsync();

        var characterNames = characters.ToDictionary(
            c => c.Id, c => EntityResolveIndex.Compose(c.Name, c.Surname), StringComparer.Ordinal);
        var locationNames = locations.ToDictionary(l => l.Id, l => l.Name, StringComparer.Ordinal);

        // Per entity, a count per chapter index.
        var presence = new Dictionary<string, int[]>(StringComparer.Ordinal);
        var pov = new Dictionary<string, (int Scenes, int Words)>(StringComparer.OrdinalIgnoreCase);
        var acts = new Dictionary<string, (int Scenes, int Words)>(StringComparer.OrdinalIgnoreCase);
        var totalScenes = 0;

        for (var index = 0; index < chapters.Count; index++)
        {
            var chapter = chapters[index];
            foreach (var scene in _projectService.GetScenesForChapter(chapter.Guid).OrderBy(s => s.Order))
            {
                totalScenes++;

                Add(pov, scene.AnalysisOverrides?.Pov ?? string.Empty, scene.WordCount);
                Add(acts, chapter.Act, scene.WordCount);

                var html = await _projectService.ReadSceneContentAsync(chapter, scene);
                // Distinct per scene: a character named eight times in one scene
                // is present once, not eight times.
                foreach (Match match in MentionIdRegex().Matches(html))
                {
                    var id = match.Groups[1].Value;
                    if (!presence.TryGetValue(id, out var counts))
                    {
                        counts = new int[chapters.Count];
                        presence[id] = counts;
                    }
                    counts[index]++;
                }
            }
        }

        var mentioned = presence.Keys.ToHashSet(StringComparer.Ordinal);

        return new BookAnalytics
        {
            ChapterTitles = [.. chapters.Select(c => c.Title)],
            Pov = Rank(pov, totalScenes),
            Acts = Rank(acts, totalScenes),
            Characters = Presence(presence, characterNames),
            Locations = Presence(presence, locationNames),
            Unused =
            [
                .. characterNames.Where(kv => !mentioned.Contains(kv.Key)).Select(kv => kv.Value)
                    .Concat(locationNames.Where(kv => !mentioned.Contains(kv.Key)).Select(kv => kv.Value))
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            ]
        };
    }

    private static void Add(
        Dictionary<string, (int Scenes, int Words)> into, string key, int words)
    {
        var normalized = (key ?? string.Empty).Trim();
        into.TryGetValue(normalized, out var current);
        into[normalized] = (current.Scenes + 1, current.Words + words);
    }

    /// <summary>
    /// Commonest first, with the share of the book each accounts for. Share is
    /// by scene count rather than words, because "how much of the book is in
    /// her POV" is a question about how often, and a single long scene should
    /// not read as dominance.
    /// </summary>
    private static IReadOnlyList<DistributionRow> Rank(
        Dictionary<string, (int Scenes, int Words)> counts, int totalScenes)
    {
        if (totalScenes == 0) return [];

        return [.. counts
            .OrderByDescending(kv => kv.Value.Scenes)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new DistributionRow(
                kv.Key,
                kv.Key,
                kv.Value.Scenes,
                kv.Value.Words,
                (int)Math.Round(kv.Value.Scenes * 100.0 / totalScenes)))];
    }

    /// <summary>The presence rows for one entity kind, busiest first. Entities
    /// the manuscript never mentions are reported separately as unused rather
    /// than as a row of zeroes.</summary>
    private static IReadOnlyList<PresenceRow> Presence(
        Dictionary<string, int[]> presence, Dictionary<string, string> names)
        => [.. presence
            .Where(kv => names.ContainsKey(kv.Key))
            .Select(kv => new PresenceRow(
                kv.Key, names[kv.Key], kv.Value.Sum(), kv.Value))
            .OrderByDescending(r => r.TotalScenes)
            .ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase)];
}
