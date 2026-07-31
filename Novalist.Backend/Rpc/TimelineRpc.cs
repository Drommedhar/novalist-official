using System.Globalization;
using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Story timeline: acts, dated chapters/scenes, and manual events grouped by zoom.</summary>
public sealed class TimelineRpc
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
        var seenActs = new HashSet<string>();
        // Acts, chapters and scenes are the manuscript's own chronology, so they
        // belong to the first timeline. Showing them under a backstory timeline
        // as well would put the war back among the Tuesdays, which is the whole
        // thing a second timeline exists to stop.
        var manuscriptShown = string.IsNullOrEmpty(timeline.ActiveTimelineId)
            || (timeline.Timelines.Count > 0
                && timeline.Timelines[0].Id == timeline.ActiveTimelineId);
        // Names for the ids a scene's cast holds, so an event can say who is in
        // it rather than only which thread it belongs to.
        var readingIndex = 0;
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
            .Where(r => r.Derived && r.Iso != null)
            .ToDictionary(r => r.SceneId, r => r.Iso!, StringComparer.Ordinal);

        foreach (var chapter in manuscriptShown
            ? book.Chapters.OrderBy(c => c.Order)
            : Enumerable.Empty<ChapterData>())
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
                    [.. cast.Where(characterNames.ContainsKey).Select(id => characterNames[id])],
                    [.. cast.Where(locationNames.ContainsKey).Select(id => locationNames[id])],
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

        var active = timeline.ActiveTimelineId;
        foreach (var manual in timeline.ManualEvents)
        {
            // Empty means the first timeline, which is what every event written
            // before there was more than one means. An empty active shows all.
            if (!string.IsNullOrEmpty(active) && !OnTimeline(manual, timeline, active)) continue;

            events.Add(new TimelineEventDto(
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
                manual.DependsOnFrom ?? Core.Services.TimelineDependencies.FromStart,
                manual.DateLocked));
        }

        // A book on its own calendar has dates no Gregorian parser can read,
        // so every scene used to fall into the undated bucket.
        var custom = book.Calendar is { Type: Core.Models.InWorldCalendarType.Custom }
            ? book.Calendar
            : null;
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

    [JsonRpcMethod("timeline/setView")]
    public async Task SetViewAsync(string viewMode, string zoomLevel)
    {
        var timeline = _workspace.Projects.ProjectSettings.Timeline;
        timeline.ViewMode = viewMode;
        timeline.ZoomLevel = zoomLevel;
        await _workspace.Projects.SaveProjectSettingsAsync();
    }

    /// <summary>
    /// Writes a manual event.
    ///
    /// <paramref name="characters"/>, <paramref name="locations"/> and
    /// <paramref name="endDate"/> are optional so a caller written before the
    /// editor could set them keeps working. The model has held all three for a
    /// long time and only scene analysis ever filled them in, so backstory that
    /// never appears in a scene could not be attached to the people it defines,
    /// and a span could be stored but never authored.
    /// </summary>
    [JsonRpcMethod("timeline/saveEvent")]
    public async Task<TimelineDto> SaveEventAsync(
        string? id, string title, string date, string description, string categoryId,
        string? linkedChapterGuid, string[]? characters = null, string[]? locations = null,
        string? endDate = null, string[]? timelineIds = null,
        string? dependsOnEventId = null, int? dependsOnOffsetDays = null,
        string? dependsOnFrom = null, bool? dateLocked = null)
    {
        var timeline = _workspace.Projects.ProjectSettings.Timeline;
        var existing = id == null ? null : timeline.ManualEvents.FirstOrDefault(e => e.Id == id);
        if (existing == null)
        {
            existing = new TimelineManualEvent
            {
                Id = $"evt-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid().ToString("N")[..7]}"
            };
            timeline.ManualEvents.Add(existing);
        }
        existing.Title = title;
        existing.Date = date;
        existing.Description = description;
        existing.CategoryId = categoryId;
        existing.LinkedChapterGuid = linkedChapterGuid ?? string.Empty;
        if (characters != null)
            existing.Characters = [.. characters.Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)];
        if (locations != null)
            existing.Locations = [.. locations.Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)];
        if (endDate != null) existing.EndDate = endDate.Trim();
        if (timelineIds != null)
        {
            // Only ids the project actually has: a stale one would put the event
            // on a timeline nothing can select, which is the same as losing it.
            var known = timeline.Timelines.Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
            var kept = timelineIds.Where(known.Contains).Distinct(StringComparer.Ordinal).ToList();
            existing.TimelineIds = kept.Count == 0 ? null : kept;
        }
        else if (existing.TimelineIds == null && !string.IsNullOrEmpty(timeline.ActiveTimelineId))
        {
            // Written while looking at one timeline: it belongs there, or it
            // would vanish the moment it was saved.
            existing.TimelineIds = [timeline.ActiveTimelineId];
        }
        if (dependsOnEventId != null)
        {
            // Only an event that exists, and never itself: either would be a
            // dependency that can never resolve.
            var anchor = dependsOnEventId.Trim();
            existing.DependsOnEventId =
                anchor.Length > 0
                && !string.Equals(anchor, existing.Id, StringComparison.Ordinal)
                && timeline.ManualEvents.Any(e => e.Id == anchor)
                    ? anchor
                    : null;
        }
        if (dependsOnOffsetDays.HasValue) existing.DependsOnOffsetDays = dependsOnOffsetDays.Value;
        if (dependsOnFrom != null)
        {
            existing.DependsOnFrom =
                string.Equals(dependsOnFrom, Core.Services.TimelineDependencies.FromEnd,
                    StringComparison.OrdinalIgnoreCase)
                    ? Core.Services.TimelineDependencies.FromEnd
                    : null;
        }
        if (dateLocked.HasValue) existing.DateLocked = dateLocked.Value;

        // Moving one date moves everything downstream of it. Doing this on
        // save rather than on read means the dates on disk are the dates the
        // writer would see, so an export or an extension reading the file
        // gets the same chronology the timeline shows.
        Core.Services.TimelineDependencies.Resolve(timeline.ManualEvents);

        await _workspace.Projects.SaveProjectSettingsAsync();
        return await Get();
    }

    /// <summary>
    /// Adds a timeline and returns the view. A project starts with one; a second
    /// is what separates backstory from the manuscript's own dates.
    /// </summary>
    [JsonRpcMethod("timeline/addTimeline")]
    public async Task<TimelineDto> AddTimelineAsync(string name)
    {
        var timeline = _workspace.Projects.ProjectSettings.Timeline;
        timeline.Timelines.Add(new TimelineTrack
        {
            Id = $"tl-{Guid.NewGuid().ToString("N")[..8]}",
            Name = string.IsNullOrWhiteSpace(name) ? "Timeline" : name.Trim()
        });
        await _workspace.Projects.SaveProjectSettingsAsync();
        return await Get();
    }

    /// <summary>Renames a timeline. An unknown id changes nothing.</summary>
    [JsonRpcMethod("timeline/renameTimeline")]
    public async Task<TimelineDto> RenameTimelineAsync(string timelineId, string name)
    {
        var track = _workspace.Projects.ProjectSettings.Timeline.Timelines
            .FirstOrDefault(l => l.Id == timelineId);
        if (track != null && !string.IsNullOrWhiteSpace(name))
        {
            track.Name = name.Trim();
            await _workspace.Projects.SaveProjectSettingsAsync();
        }
        return await Get();
    }

    /// <summary>
    /// Removes a timeline. The first one cannot go: it is where everything
    /// unassigned lives, and without it those events would have no home.
    ///
    /// Events are not deleted with it - one that named only this timeline falls
    /// back to the first rather than being thrown away with the container.
    /// </summary>
    [JsonRpcMethod("timeline/deleteTimeline")]
    public async Task<TimelineDto> DeleteTimelineAsync(string timelineId)
    {
        var timeline = _workspace.Projects.ProjectSettings.Timeline;
        if (timeline.Timelines.Count < 2 || timeline.Timelines[0].Id == timelineId)
            return await Get();

        timeline.Timelines.RemoveAll(l => l.Id == timelineId);
        foreach (var manual in timeline.ManualEvents)
        {
            if (manual.TimelineIds == null) continue;
            manual.TimelineIds.RemoveAll(x => string.Equals(x, timelineId, StringComparison.Ordinal));
            if (manual.TimelineIds.Count == 0) manual.TimelineIds = null;
        }
        if (timeline.ActiveTimelineId == timelineId) timeline.ActiveTimelineId = string.Empty;

        await _workspace.Projects.SaveProjectSettingsAsync();
        return await Get();
    }

    /// <summary>Shows one timeline, or all of them with an empty id.</summary>
    [JsonRpcMethod("timeline/setActiveTimeline")]
    public async Task<TimelineDto> SetActiveTimelineAsync(string? timelineId)
    {
        var timeline = _workspace.Projects.ProjectSettings.Timeline;
        var id = timelineId ?? string.Empty;
        timeline.ActiveTimelineId =
            id.Length > 0 && timeline.Timelines.Any(l => l.Id == id) ? id : string.Empty;
        await _workspace.Projects.SaveProjectSettingsAsync();
        return await Get();
    }

    [JsonRpcMethod("timeline/deleteEvent")]
    public async Task<TimelineDto> DeleteEventAsync(string id)
    {
        var timeline = _workspace.Projects.ProjectSettings.Timeline;
        timeline.ManualEvents.RemoveAll(e => e.Id == id);
        // Anything hanging off it keeps the date it has rather than pointing
        // at an anchor that is gone.
        foreach (var orphan in timeline.ManualEvents.Where(e => e.DependsOnEventId == id))
            orphan.DependsOnEventId = null;
        await _workspace.Projects.SaveProjectSettingsAsync();
        return await Get();
    }

    [JsonRpcMethod("timeline/structureTemplates")]
    public StructureTemplateDto[] GetStructureTemplates() =>
        StoryStructureTemplates.All
            .Select(t => new StructureTemplateDto(t.Id, t.DisplayName, t.Description))
            .ToArray();

    // Ported from TimelineViewModel.ApplyStructureTemplateAsync: appends the
    // template's beats as manual events; unknown ids are a no-op.
    [JsonRpcMethod("timeline/applyStructureTemplate")]
    public async Task<TimelineDto> ApplyStructureTemplateAsync(string templateId)
    {
        var template = StoryStructureTemplates.GetById(templateId);
        if (template == null) return await Get();

        var timeline = _workspace.Projects.ProjectSettings.Timeline;
        var nextOrder = timeline.ManualEvents.Count;
        foreach (var beat in template.Beats)
        {
            timeline.ManualEvents.Add(new TimelineManualEvent
            {
                Id = $"evt-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid().ToString()[..7]}",
                Title = beat.Title,
                Description = beat.Description,
                CategoryId = beat.CategoryId,
                Order = nextOrder++
            });
        }
        await _workspace.Projects.SaveProjectSettingsAsync();
        return await Get();
    }

    private static string? Iso(DateTime? date) => date?.ToString("yyyy-MM-dd");

    // Ported verbatim from TimelineViewModel so grouping matches the Avalonia app.
    /// <summary>
    /// Groups a chronology by in-world year, ordered by the calendar's own
    /// day arithmetic. Years are labelled the way the book reckons them, so a
    /// chronology reads as "342 AC" rather than as a bare number.
    /// </summary>
    private static TimelineGroupDto[] GroupByInWorldYear(
        IReadOnlyList<TimelineEventDto> events, Core.Models.InWorldCalendar calendar)
    {
        var service = new InWorldCalendarService();
        return [.. events
            .OrderBy(e => service.Parse(e.DateStr ?? string.Empty, calendar) == null ? 1 : 0)
            .ThenBy(e => service.Parse(e.DateStr ?? string.Empty, calendar) ?? 0)
            .ThenBy(e => e.ChapterOrder)
            .GroupBy(e => service.YearOf(e.DateStr ?? string.Empty, calendar))
            .Select(g => new TimelineGroupDto(
                g.Key?.ToString(CultureInfo.InvariantCulture) ?? "?",
                g.Key == null ? "?" : service.FormatYear(g.Key.Value, calendar),
                [.. g]))];
    }

    internal static DateTime? ParseDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return null;
        var s = dateStr.Trim();
        if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
            return iso;
        if (DateTime.TryParseExact(s, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var ym))
            return ym;
        if (DateTime.TryParseExact(s, "yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var y))
            return y;
        if (DateTime.TryParseExact(s, "d.M.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var eu))
            return eu;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fallback))
            return fallback;
        return null;
    }

    internal static string GroupKey(DateTime? date, string zoom)
    {
        if (!date.HasValue) return "no-date";
        var d = date.Value;
        return zoom switch
        {
            "year" => $"{d.Year}",
            "day" => $"{d.Year}-{d.Month:D2}-{d.Day:D2}",
            _ => $"{d.Year}-{d.Month:D2}"
        };
    }

    internal static string GroupLabel(string key, string zoom)
    {
        if (key == "no-date") return "???";
        var parts = key.Split('-').Select(int.Parse).ToArray();
        var months = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        return zoom switch
        {
            "year" => $"{parts[0]}",
            "day" => $"{months[parts[1] - 1]} {parts[2]}, {parts[0]}",
            _ => $"{months[parts[1] - 1]} {parts[0]}"
        };
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
