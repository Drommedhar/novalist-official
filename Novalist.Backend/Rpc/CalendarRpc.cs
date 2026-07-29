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

    /// <summary>
    /// The book's calendar configuration. Returns Gregorian defaults when the
    /// book has never been configured, so the editor always has something to
    /// show rather than a null to special-case.
    /// </summary>
    [JsonRpcMethod("calendar/getConfig")]
    public CalendarConfigDto GetConfig()
    {
        var calendar = _workspace.Projects.ActiveBook?.Calendar ?? new Core.Models.InWorldCalendar();
        return new CalendarConfigDto(
            calendar.Type.ToString(),
            calendar.YearLabel,
            calendar.MonthNames.ToArray(),
            calendar.DaysPerMonth.ToArray(),
            calendar.WeekdayNames.ToArray(),
            calendar.CustomYearLength,
            [.. calendar.Eras.Select(e => new CalendarEraDto(e.Name, e.StartYear, e.CountsDown))]);
    }

    /// <summary>
    /// Replaces the book's calendar. Month names and their day counts are
    /// zipped to the shorter of the two, so a half-finished edit can never
    /// produce a calendar whose months and lengths disagree.
    /// </summary>
    [JsonRpcMethod("calendar/setConfig")]
    public async Task<CalendarConfigDto> SetConfigAsync(
        string type, string yearLabel, string[] monthNames, int[] daysPerMonth,
        string[] weekdayNames, CalendarEraDto[]? eras = null)
    {
        var book = _workspace.Projects.ActiveBook
            ?? throw new InvalidOperationException("No project open.");

        var months = new List<string>();
        var days = new List<int>();
        var pairs = Math.Min(monthNames?.Length ?? 0, daysPerMonth?.Length ?? 0);
        for (var i = 0; i < pairs; i++)
        {
            var name = (monthNames![i] ?? string.Empty).Trim();
            if (name.Length == 0)
                continue;

            months.Add(name);
            // A month of zero or negative days would make year length arithmetic
            // meaningless; one day is the smallest month that can exist.
            days.Add(Math.Max(1, daysPerMonth![i]));
        }

        book.Calendar = new Core.Models.InWorldCalendar
        {
            Type = string.Equals(type, "Custom", StringComparison.OrdinalIgnoreCase)
                ? Core.Models.InWorldCalendarType.Custom
                : Core.Models.InWorldCalendarType.Gregorian,
            YearLabel = (yearLabel ?? string.Empty).Trim(),
            MonthNames = months,
            DaysPerMonth = days,
            WeekdayNames = (weekdayNames ?? [])
                .Select(w => (w ?? string.Empty).Trim())
                .Where(w => w.Length > 0)
                .ToList(),
            // An era with no name cannot label anything, and two eras starting
            // in the same year would make a date's era ambiguous.
            Eras = [.. (eras ?? [])
                .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                .GroupBy(e => e.StartYear)
                .Select(g => g.First())
                .OrderBy(e => e.StartYear)
                .Select(e => new Core.Models.CalendarEra
                {
                    Name = e.Name!.Trim(),
                    StartYear = e.StartYear,
                    CountsDown = e.CountsDown
                })]
        };

        await _workspace.Projects.SaveProjectAsync();
        return GetConfig();
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
                        string.IsNullOrWhiteSpace(range.Note) ? null : range.Note,
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
    string? Note,
    bool AllDay,
    int StartHour,
    int StartMinute,
    int EndHour,
    int EndMinute);

/// <summary>A named stretch of years in the book's reckoning.</summary>
public sealed record CalendarEraDto(string? Name, long StartYear, bool CountsDown);

public sealed record CalendarConfigDto(
    string Type,
    string YearLabel,
    string[] MonthNames,
    int[] DaysPerMonth,
    string[] WeekdayNames,
    int YearLength,
    CalendarEraDto[] Eras);
