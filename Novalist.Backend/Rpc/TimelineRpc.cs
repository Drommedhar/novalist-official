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
        // Names for the ids a scene's cast holds, so an event can say who is in
        // it rather than only which thread it belongs to.
        var characterNames = (await _entities.LoadCharactersAsync())
            .ToDictionary(c => c.Id, c => c.Name, StringComparer.Ordinal);
        var locationNames = (await _entities.LoadLocationsAsync())
            .ToDictionary(l => l.Id, l => l.Name, StringComparer.Ordinal);

        foreach (var chapter in book.Chapters.OrderBy(c => c.Order))
        {
            if (!string.IsNullOrEmpty(chapter.Act) && seenActs.Add(chapter.Act))
            {
                events.Add(new TimelineEventDto(
                    $"act-{chapter.Act}", chapter.Act, "", null, "", "act",
                    null, null, null, chapter.Order - 0.5, [], [], false, string.Empty, []));
            }

            if (!string.IsNullOrEmpty(chapter.Date))
            {
                events.Add(new TimelineEventDto(
                    $"ch-{chapter.Guid}", chapter.Title, chapter.Date, Iso(ParseDate(chapter.Date)),
                    "", "chapter", null, chapter.Guid, null, chapter.Order, [], [], false,
                    string.Empty, []));
            }

            var scenes = (manifest?.Chapters.GetValueOrDefault(chapter.Guid) ?? [])
                .Where(s => s.ArchivedAt == null)
                .OrderBy(s => s.Order);
            foreach (var scene in scenes)
            {
                var date = string.IsNullOrEmpty(scene.Date) ? chapter.Date : scene.Date;
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
                    scene.PlotlineIds ?? []));
            }
        }

        foreach (var manual in timeline.ManualEvents)
        {
            events.Add(new TimelineEventDto(
                $"manual-{manual.Id}", manual.Title, manual.Date, Iso(ParseDate(manual.Date)),
                manual.Description, "manual", manual.CategoryId,
                string.IsNullOrEmpty(manual.LinkedChapterGuid) ? null : manual.LinkedChapterGuid,
                string.IsNullOrEmpty(manual.LinkedSceneId) ? null : manual.LinkedSceneId,
                double.MaxValue, manual.Characters.ToArray(), manual.Locations.ToArray(), true,
                string.Empty, []));
        }

        var groups = events
            .OrderBy(e => Iso(ParseDate(e.DateStr)) == null ? 1 : 0)
            .ThenBy(e => e.SortDate, StringComparer.Ordinal)
            .ThenBy(e => e.ChapterOrder)
            .GroupBy(e => GroupKey(ParseDate(e.DateStr), zoom))
            .Select(g => new TimelineGroupDto(g.Key, GroupLabel(g.Key, zoom), g.ToArray()))
            .ToArray();

        return new TimelineDto(timeline.ViewMode, zoom, groups, await BuildEntityLinksAsync(events));
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

    [JsonRpcMethod("timeline/saveEvent")]
    public async Task<TimelineDto> SaveEventAsync(
        string? id, string title, string date, string description, string categoryId, string? linkedChapterGuid)
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
        await _workspace.Projects.SaveProjectSettingsAsync();
        return await Get();
    }

    [JsonRpcMethod("timeline/deleteEvent")]
    public async Task<TimelineDto> DeleteEventAsync(string id)
    {
        var timeline = _workspace.Projects.ProjectSettings.Timeline;
        timeline.ManualEvents.RemoveAll(e => e.Id == id);
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
    IReadOnlyList<TimelineEntityLinkDto> EntityLinks);

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
    IReadOnlyList<string> PlotlineIds);
