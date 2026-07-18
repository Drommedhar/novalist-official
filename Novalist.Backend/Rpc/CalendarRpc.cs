using System.Globalization;
using Novalist.Core.Utilities;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Story calendar: scene events resolved onto Gregorian dates.</summary>
public sealed class CalendarRpc
{
    private readonly Workspace _workspace;

    public CalendarRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    [JsonRpcMethod("calendar/get")]
    public CalendarEventDto[] Get(string fromIso, string toIso)
    {
        var projects = _workspace.Projects;
        var book = projects.ActiveBook ?? throw new InvalidOperationException("No project open.");
        var manifest = projects.ScenesManifest;
        var from = ParseIso(fromIso);
        var to = ParseIso(toIso);

        var events = new List<CalendarEventDto>();
        foreach (var chapter in book.Chapters.OrderBy(c => c.Order))
        {
            var scenes = (manifest?.Chapters.GetValueOrDefault(chapter.Guid) ?? [])
                .Where(s => s.ArchivedAt == null)
                .OrderBy(s => s.Order);
            foreach (var scene in scenes)
            {
                var range = StoryDateResolver.Resolve(scene, chapter, book.Acts);
                if (range?.Start == null) continue;
                if (!TryParseDate(range.Start, out var start)) continue;
                var end = TryParseDate(range.End, out var parsedEnd) ? parsedEnd : start;
                var startTime = ParseTime(range.StartTime);
                var endTime = ParseTime(range.EndTime);

                for (var day = start.Date; day <= end.Date; day = day.AddDays(1))
                {
                    if (day < from || day > to) continue;
                    events.Add(new CalendarEventDto(
                        day.ToString("yyyy-MM-dd"),
                        chapter.Guid,
                        scene.Id,
                        scene.Title,
                        chapter.Title,
                        scene.Synopsis,
                        startTime == null,
                        startTime?.Hours ?? 0,
                        startTime?.Minutes ?? 0,
                        endTime?.Hours ?? 0,
                        endTime?.Minutes ?? 0));
                }
            }
        }
        return events.ToArray();
    }

    [JsonRpcMethod("calendar/reschedule")]
    public async Task RescheduleAsync(string chapterGuid, string sceneId, string dateIso)
    {
        await _workspace.Projects.SetSceneDateAsync(chapterGuid, sceneId, dateIso);
    }

    [JsonRpcMethod("calendar/getAnchor")]
    public string? GetAnchor() => _workspace.Projects.ProjectSettings.CalendarAnchor;

    [JsonRpcMethod("calendar/setAnchor")]
    public async Task SetAnchorAsync(string anchorIso)
    {
        _workspace.Projects.ProjectSettings.CalendarAnchor = anchorIso;
        await _workspace.Projects.SaveProjectSettingsAsync();
    }

    private static DateTime ParseIso(string iso) =>
        DateTime.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal static bool TryParseDate(string? value, out DateTime date)
    {
        date = default;
        return !string.IsNullOrWhiteSpace(value)
            && DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    internal static TimeSpan? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var formats = new[] { @"hh\:mm", @"h\:mm", @"hh\:mm\:ss" };
        if (TimeSpan.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, out var exact))
            return exact;
        return TimeSpan.TryParse(value.Trim(), CultureInfo.InvariantCulture, out var loose) ? loose : null;
    }
}

public sealed record CalendarEventDto(
    string Date,
    string ChapterGuid,
    string SceneId,
    string Title,
    string ChapterTitle,
    string? Synopsis,
    bool AllDay,
    int StartHour,
    int StartMinute,
    int EndHour,
    int EndMinute);
