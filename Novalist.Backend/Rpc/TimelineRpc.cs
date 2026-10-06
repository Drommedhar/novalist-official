using System.Globalization;
using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Story timeline: acts, dated chapters/scenes, and manual events grouped by zoom.</summary>
public sealed partial class TimelineRpc
{
    private readonly Workspace _workspace;
    private readonly EntityService _entities;

    public TimelineRpc(Workspace workspace)
    {
        _workspace = workspace;
        _entities = new EntityService(workspace.Projects);
    }

    [JsonRpcMethod("timeline/get")]
    public async Task<TimelineDto> Get()
    {
        var projects = _workspace.Projects;
        var book = projects.ActiveBook ?? throw new InvalidOperationException("No project open.");
        var timeline = projects.ProjectSettings.Timeline;
        var manifest = projects.ScenesManifest;
        var zoom = timeline.ZoomLevel;

        var events = new List<TimelineEventDto>();
        // Acts, chapters and scenes are the manuscript's own chronology, so they
        // belong to the first timeline. Showing them under a backstory timeline
        // as well would put the war back among the Tuesdays, which is the whole
        // thing a second timeline exists to stop.
        var manuscriptShown = string.IsNullOrEmpty(timeline.ActiveTimelineId)
            || (timeline.Timelines.Count > 0
                && timeline.Timelines[0].Id == timeline.ActiveTimelineId);
        // Names for the ids a scene's cast holds, so an event can say who is in
        // it rather than only which thread it belongs to.
        var characterNames = (await _entities.LoadCharactersAsync())
            .ToDictionary(c => c.Id, c => c.Name, StringComparer.Ordinal);
        var locationNames = (await _entities.LoadLocationsAsync())
            .ToDictionary(l => l.Id, l => l.Name, StringComparer.Ordinal);

        // Relative offsets resolved in one pass, so "the next morning" has a
        // date to be shown at rather than being dropped for having none.
        var resolved = StoryClock
            .Resolve(book.Chapters
                .OrderBy(c => c.Order)
                .SelectMany(c => (manifest?.Chapters.GetValueOrDefault(c.Guid) ?? [])
                    .Where(s => s.ArchivedAt == null)
                    .OrderBy(s => s.Order)
                    .Select(s => (c, s))))
            .SelectMany(r => r.Derived && r.Iso is { } iso
                ? new[] { KeyValuePair.Create(r.SceneId, iso) }
                : [])
            .ToDictionary(StringComparer.Ordinal);

        if (manuscriptShown)
            events.AddRange(ManuscriptEvents(book, manifest, (characterNames, locationNames), resolved));

        var active = timeline.ActiveTimelineId;
        foreach (var manual in timeline.ManualEvents)
        {
            if (!string.IsNullOrEmpty(active) && !OnTimeline(manual, timeline, active)) continue;
            events.Add(ManualEvent(manual));
        }

        var custom = book.Calendar is { Type: InWorldCalendarType.Custom } ? book.Calendar : null;
        var groups = custom == null
            ? [.. events
                .OrderBy(e => Iso(ParseDate(e.DateStr)) == null ? 1 : 0)
                .ThenBy(e => e.SortDate, StringComparer.Ordinal)
                .ThenBy(e => e.ChapterOrder)
                .GroupBy(e => GroupKey(ParseDate(e.DateStr), zoom))
                .Select(g => new TimelineGroupDto(g.Key, GroupLabel(g.Key, zoom), g.ToArray()))]
            : GroupByInWorldYear(events, custom);

        return new TimelineDto(
            timeline.ViewMode, zoom, groups, await BuildEntityLinksAsync(events),
            [.. timeline.Timelines.Select(l => new TimelineTrackDto(l.Id, l.Name))],
            timeline.ActiveTimelineId);
    }

    private static List<TimelineEventDto> ManuscriptEvents(BookData book, ScenesManifest? manifest,
        (IReadOnlyDictionary<string, string> Characters, IReadOnlyDictionary<string, string> Locations) names,
        IReadOnlyDictionary<string, string> resolved)
    {
        var events = new List<TimelineEventDto>();
        var seenActs = new HashSet<string>();
        var readingIndex = 0;
        foreach (var chapter in book.Chapters.OrderBy(c => c.Order))
        {
            if (!string.IsNullOrEmpty(chapter.Act) && seenActs.Add(chapter.Act))
            {
                events.Add(new TimelineEventDto(
                    $"act-{chapter.Act}", chapter.Act, "", null, "", "act",
                    null, null, null, chapter.Order - 0.5, [], [], false, string.Empty, [],
                    string.Empty, 0));
            }

            if (!string.IsNullOrEmpty(chapter.Date))
            {
                events.Add(new TimelineEventDto(
                    $"ch-{chapter.Guid}", chapter.Title, chapter.Date, Iso(ParseDate(chapter.Date)),
                    "", "chapter", null, chapter.Guid, null, chapter.Order, [], [], false,
                    string.Empty, [], string.Empty, 0));
            }

            var scenes = (manifest?.Chapters.GetValueOrDefault(chapter.Guid) ?? [])
                .Where(s => s.ArchivedAt == null)
                .OrderBy(s => s.Order);
            foreach (var scene in scenes)
            {
                var date = string.IsNullOrEmpty(scene.Date) ? chapter.Date : scene.Date;
                // A scene that only says "two hours later" used to fall out of
                // the timeline entirely, which is how a whole book ends up
                // looking undated.
                if (string.IsNullOrEmpty(date)) date = resolved.GetValueOrDefault(scene.Id) ?? string.Empty;
                if (string.IsNullOrEmpty(date)) continue;
                var cast = scene.Cast ?? [];
                events.Add(new TimelineEventDto(
                    $"sc-{chapter.Guid}-{scene.Id}", $"{chapter.Title}: {scene.Title}", date,
                    Iso(ParseDate(date)), scene.Synopsis ?? "", "scene",
                    null, chapter.Guid, scene.Id, chapter.Order,
                    [.. cast.Where(names.Characters.ContainsKey).Select(id => names.Characters[id])],
                    [.. cast.Where(names.Locations.ContainsKey).Select(id => names.Locations[id])],
                    false,
                    scene.AnalysisOverrides?.Pov ?? string.Empty,
                    scene.PlotlineIds ?? [],
                    scene.NarrativeMode ?? string.Empty,
                    // Reading order is what a reader meets, whatever the dates
                    // say; numbered here so the two orders can be compared.
                    ++readingIndex,
                    // A scene that spans days has always known it; the timeline
                    // just never passed the far end on.
                    scene.DateRange?.End ?? string.Empty,
                    Iso(ParseDate(scene.DateRange?.End ?? string.Empty))));
            }
        }

        return events;
    }

    private static TimelineEventDto ManualEvent(TimelineManualEvent manual) => new(
        $"manual-{manual.Id}", manual.Title, manual.Date, Iso(ParseDate(manual.Date)),
        manual.Description, "manual", manual.CategoryId,
        string.IsNullOrEmpty(manual.LinkedChapterGuid) ? null : manual.LinkedChapterGuid,
        string.IsNullOrEmpty(manual.LinkedSceneId) ? null : manual.LinkedSceneId,
        double.MaxValue, manual.Characters.ToArray(), manual.Locations.ToArray(), true,
        string.Empty, [], string.Empty, 0,
        manual.EndDate, Iso(ParseDate(manual.EndDate)),
        [.. manual.TimelineIds ?? []],
        manual.DependsOnEventId ?? string.Empty,
        manual.DependsOnOffsetDays,
        manual.DependsOnFrom ?? TimelineDependencies.FromStart,
        manual.DateLocked);

    /// <summary>
    /// Whether an event belongs on a timeline. An event naming none is on the
    /// first, so nothing written before timelines existed disappears.
    /// </summary>
    private static bool OnTimeline(TimelineManualEvent manual, TimelineData timeline, string timelineId)
    {
        var ids = manual.TimelineIds;
        if (ids == null || ids.Count == 0)
            return timeline.Timelines.Count > 0 && timeline.Timelines[0].Id == timelineId;
        return ids.Contains(timelineId, StringComparer.Ordinal);
    }

    /// <summary>Resolves the character/location names carried by manual events to
    /// their Codex entities through the shared <see cref="EntityResolveIndex"/>, so
    /// the renderer can turn each chip into a link. Names that are ambiguous or
    /// unknown resolve to nothing and stay plain text.</summary>
    private async Task<IReadOnlyList<TimelineEntityLinkDto>> BuildEntityLinksAsync(
        IReadOnlyList<TimelineEventDto> events)
    {
        var names = events
            .SelectMany(e => e.Characters.Concat(e.Locations))
            .Select(EntityResolveIndex.Normalize)
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0)
            return [];

        var resolve = await EntityResolveIndex.BuildAsync(_entities);
        return names
            .Where(resolve.ContainsKey)
            .Select(n => new TimelineEntityLinkDto(n, resolve[n].Id, resolve[n].TypeKey))
            .ToArray();
    }
}

public sealed record StructureTemplateDto(string Id, string DisplayName, string Description);

public sealed record TimelineDto(
    string ViewMode,
    string ZoomLevel,
    IReadOnlyList<TimelineGroupDto> Groups,
    IReadOnlyList<TimelineEntityLinkDto> EntityLinks,
    IReadOnlyList<TimelineTrackDto> Timelines,
    string ActiveTimelineId);

/// <summary>One named timeline of the project.</summary>
public sealed record TimelineTrackDto(string Id, string Name);

/// <summary>A character/location name used on a manual event that resolves to
/// exactly one Codex entity, so the renderer can link the chip to its article.
/// Ambiguous or unknown names are simply absent.</summary>
public sealed record TimelineEntityLinkDto(string Name, string EntityId, string TypeKey);

public sealed record TimelineGroupDto(
    string Key,
    string Label,
    IReadOnlyList<TimelineEventDto> Events);

public sealed record TimelineEventDto(
    string Id,
    string Title,
    string DateStr,
    string? SortDate,
    string Description,
    string Source,
    string? CategoryId,
    string? ChapterGuid,
    string? SceneId,
    double ChapterOrder,
    IReadOnlyList<string> Characters,
    IReadOnlyList<string> Locations,
    bool IsManual,
    /// <summary>The POV the writer set on the scene, for lanes. Empty for
    /// events that are not scenes.</summary>
    string Pov,
    /// <summary>Plotlines the scene belongs to, for lanes.</summary>
    IReadOnlyList<string> PlotlineIds,
    /// <summary>How the scene sits in time: flashback, parallel and the rest.
    /// Empty for events that are not scenes, and for scenes that simply happen
    /// next.</summary>
    string NarrativeMode,
    /// <summary>Its place in reading order, from one. Zero for events that are
    /// not scenes.</summary>
    int ReadingIndex,
    /// <summary>
    /// End of the span as written, or empty for something instantaneous.
    ///
    /// Duration was computed and printed as text - "3 weeks" - and the timeline
    /// drew a dot, so a war spanning ten chapters and a pregnancy spanning
    /// twenty could not be compared and their overlap was invisible.
    /// </summary>
    string EndDateStr = "",
    /// <summary>The end date sortable, or null when it cannot be read.</summary>
    string? SortEndDate = null,
    /// <summary>The timelines this event sits on. Empty means the first,
    /// which is what every event written before there was more than one
    /// timeline means.</summary>
    IReadOnlyList<string>? TimelineIds = null,
    /// <summary>The event this one hangs off, or empty.</summary>
    string DependsOnEventId = "",
    /// <summary>Days after the anchor. Negative puts it before.</summary>
    int DependsOnOffsetDays = 0,
    /// <summary>"start" or "end" of the anchor.</summary>
    string DependsOnFrom = "start",
    /// <summary>The writer pinned this date, so a cascade leaves it.</summary>
    bool DateLocked = false);
