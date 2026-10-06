using System.Globalization;
using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class TimelineRpc
{
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
    // aislop-ignore-next-line complexity/too-many-params -- Published JSON-RPC parameter names and ordering are part of the renderer protocol and must remain compatible.
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
        // Once authored, this is the writer's event rather than a disposable
        // placeholder. Switching story structures must never erase that work.
        existing.StructureTemplateId = null;
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
        new StoryStructureService(_workspace.Projects).Available()
            .Select(t => new StructureTemplateDto(t.Id, t.DisplayName, t.Description))
            .ToArray();

    // Replace untouched placeholders, keeping authored events and manuscript
    // scenes. Legacy events have no provenance and must not be guessed at.
    [JsonRpcMethod("timeline/applyStructureTemplate")]
    public async Task<TimelineDto> ApplyStructureTemplateAsync(string templateId)
    {
        var template = new StoryStructureService(_workspace.Projects).Find(templateId);
        if (template == null) return await Get();

        var timeline = _workspace.Projects.ProjectSettings.Timeline;
        if (string.Equals(timeline.StructureTemplateId, template.Id, StringComparison.OrdinalIgnoreCase))
            return await Get();

        var removedIds = timeline.ManualEvents.Where(e => e.StructureTemplateId != null && !(e.Properties?.Count > 0))
            .Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        timeline.ManualEvents.RemoveAll(e => removedIds.Contains(e.Id));
        foreach (var orphan in timeline.ManualEvents.Where(e => e.DependsOnEventId != null && removedIds.Contains(e.DependsOnEventId)))
            orphan.DependsOnEventId = null;

        timeline.StructureTemplateId = template.Id;
        var nextOrder = timeline.ManualEvents.Select(e => e.Order).DefaultIfEmpty(-1).Max() + 1;
        foreach (var beat in template.Beats)
        {
            timeline.ManualEvents.Add(new TimelineManualEvent
            {
                Id = $"evt-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid().ToString()[..7]}",
                Title = beat.Title,
                Description = beat.Description,
                CategoryId = beat.CategoryId,
                StructureTemplateId = template.Id,
                Order = nextOrder++
            });
        }
        await _workspace.Projects.SaveProjectSettingsAsync();
        return await Get();
    }
}
