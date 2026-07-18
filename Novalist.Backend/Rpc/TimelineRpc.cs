using System.Globalization;
using Novalist.Core.Models;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Story timeline: acts, dated chapters/scenes, and manual events grouped by zoom.</summary>
public sealed class TimelineRpc
{
    private readonly Workspace _workspace;

    public TimelineRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    [JsonRpcMethod("timeline/get")]
    public TimelineDto Get()
    {
        var projects = _workspace.Projects;
        var book = projects.ActiveBook ?? throw new InvalidOperationException("No project open.");
        var timeline = projects.ProjectSettings.Timeline;
        var manifest = projects.ScenesManifest;
        var zoom = timeline.ZoomLevel;

        var events = new List<TimelineEventDto>();
        var seenActs = new HashSet<string>();

        foreach (var chapter in book.Chapters.OrderBy(c => c.Order))
        {
            if (!string.IsNullOrEmpty(chapter.Act) && seenActs.Add(chapter.Act))
            {
                events.Add(new TimelineEventDto(
                    $"act-{chapter.Act}", chapter.Act, "", null, "", "act",
                    null, null, null, chapter.Order - 0.5, [], [], false));
            }

            if (!string.IsNullOrEmpty(chapter.Date))
            {
                events.Add(new TimelineEventDto(
                    $"ch-{chapter.Guid}", chapter.Title, chapter.Date, Iso(ParseDate(chapter.Date)),
                    "", "chapter", null, chapter.Guid, null, chapter.Order, [], [], false));
            }

            var scenes = (manifest?.Chapters.GetValueOrDefault(chapter.Guid) ?? [])
                .Where(s => s.ArchivedAt == null)
                .OrderBy(s => s.Order);
            foreach (var scene in scenes)
            {
                var date = string.IsNullOrEmpty(scene.Date) ? chapter.Date : scene.Date;
                if (string.IsNullOrEmpty(date)) continue;
                events.Add(new TimelineEventDto(
                    $"sc-{chapter.Guid}-{scene.Id}", $"{chapter.Title}: {scene.Title}", date,
                    Iso(ParseDate(date)), scene.Synopsis ?? "", "scene",
                    null, chapter.Guid, scene.Id, chapter.Order, [], [], false));
            }
        }

        foreach (var manual in timeline.ManualEvents)
        {
            events.Add(new TimelineEventDto(
                $"manual-{manual.Id}", manual.Title, manual.Date, Iso(ParseDate(manual.Date)),
                manual.Description, "manual", manual.CategoryId,
                string.IsNullOrEmpty(manual.LinkedChapterGuid) ? null : manual.LinkedChapterGuid,
                string.IsNullOrEmpty(manual.LinkedSceneId) ? null : manual.LinkedSceneId,
                double.MaxValue, manual.Characters.ToArray(), manual.Locations.ToArray(), true));
        }

        var groups = events
            .OrderBy(e => Iso(ParseDate(e.DateStr)) == null ? 1 : 0)
            .ThenBy(e => e.SortDate, StringComparer.Ordinal)
            .ThenBy(e => e.ChapterOrder)
            .GroupBy(e => GroupKey(ParseDate(e.DateStr), zoom))
            .Select(g => new TimelineGroupDto(g.Key, GroupLabel(g.Key, zoom), g.ToArray()))
            .ToArray();

        return new TimelineDto(timeline.ViewMode, zoom, groups);
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
        return Get();
    }

    [JsonRpcMethod("timeline/deleteEvent")]
    public async Task<TimelineDto> DeleteEventAsync(string id)
    {
        var timeline = _workspace.Projects.ProjectSettings.Timeline;
        timeline.ManualEvents.RemoveAll(e => e.Id == id);
        await _workspace.Projects.SaveProjectSettingsAsync();
        return Get();
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

public sealed record TimelineDto(
    string ViewMode,
    string ZoomLevel,
    IReadOnlyList<TimelineGroupDto> Groups);

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
    bool IsManual);
