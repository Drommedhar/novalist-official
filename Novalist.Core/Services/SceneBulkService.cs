using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>
/// Acts on a set of scenes at once.
///
/// Every operation resolves the whole selection first and only then mutates, so
/// a selection holding one stale id cannot leave half the work done. Ids that
/// name nothing are dropped: a selection outlives the binder that built it, and
/// refusing the entire operation over one deleted scene is worse than doing the
/// rest.
/// </summary>
public sealed class SceneBulkService : ISceneBulkService
{
    private readonly IProjectService _projectService;
    private readonly IInWorldCalendarService _calendar;

    public SceneBulkService(IProjectService projectService, IInWorldCalendarService calendar)
    {
        _projectService = projectService;
        _calendar = calendar;
    }

    public IReadOnlyList<ResolvedScene> Resolve(IReadOnlyList<string> sceneIds)
    {
        var wanted = new HashSet<string>(
            sceneIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal);
        if (wanted.Count == 0) return [];

        var result = new List<ResolvedScene>();
        // Book order, so a bulk operation reads the same way the binder does.
        foreach (var chapter in _projectService.GetChaptersOrdered())
        {
            foreach (var scene in _projectService.GetScenesForChapter(chapter.Guid).OrderBy(s => s.Order))
            {
                if (wanted.Contains(scene.Id)) result.Add(new ResolvedScene(chapter.Guid, scene));
            }
        }

        return result;
    }

    public async Task<int> DeleteAsync(IReadOnlyList<string> sceneIds)
    {
        var targets = Resolve(sceneIds);
        foreach (var target in targets)
            await _projectService.DeleteSceneAsync(target.ChapterGuid, target.Scene.Id);

        return targets.Count;
    }

    public async Task<int> ArchiveAsync(IReadOnlyList<string> sceneIds)
    {
        var targets = Resolve(sceneIds);
        foreach (var target in targets)
            await _projectService.ArchiveSceneAsync(target.ChapterGuid, target.Scene.Id);

        return targets.Count;
    }

    public async Task<int> SetTagsAsync(
        IReadOnlyList<string> sceneIds, IReadOnlyList<string> tags, bool replace)
    {
        var clean = tags
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var targets = Resolve(sceneIds);
        var changed = 0;
        foreach (var target in targets)
        {
            var existing = replace
                ? []
                : target.Scene.AnalysisOverrides?.Tags ?? [];

            var merged = existing
                .Concat(clean)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Nothing to write when the scene already carries exactly these tags.
            if (existing.Count == merged.Count && !replace) continue;

            var overrides = target.Scene.AnalysisOverrides ?? new SceneAnalysisOverrides();
            overrides.Tags = merged;
            await _projectService.SetSceneAnalysisOverridesAsync(
                target.ChapterGuid, target.Scene.Id, overrides);
            changed++;
        }

        return changed;
    }

    public IReadOnlyList<SceneDateShift> PreviewDateShift(IReadOnlyList<string> sceneIds, long days)
    {
        var calendar = _projectService.ActiveBook?.Calendar;
        var rows = new List<SceneDateShift>();
        foreach (var target in Resolve(sceneIds))
        {
            var before = StartDateOf(target.Scene);
            // A scene with no date has nothing to shift, and is shown unchanged
            // rather than hidden — otherwise a selection of ten scenes silently
            // previews as three.
            var after = string.IsNullOrWhiteSpace(before)
                ? before
                : _calendar.AddDays(before, days, calendar);
            rows.Add(new SceneDateShift(target.Scene.Id, target.Scene.Title, before, after));
        }

        return rows;
    }

    public async Task<int> ShiftDatesAsync(IReadOnlyList<string> sceneIds, long days)
    {
        var calendar = _projectService.ActiveBook?.Calendar;
        var moved = 0;
        foreach (var target in Resolve(sceneIds))
        {
            var range = target.Scene.DateRange;
            if (range?.HasValue == true)
            {
                var shifted = range.Clone();
                shifted.Start = Shift(range.Start, days, calendar);
                shifted.End = Shift(range.End, days, calendar);
                if (shifted.Start == range.Start && shifted.End == range.End) continue;
                await _projectService.SetSceneDateRangeAsync(
                    target.ChapterGuid, target.Scene.Id, shifted);
                moved++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(target.Scene.Date)) continue;
            var next = Shift(target.Scene.Date, days, calendar);
            if (next == target.Scene.Date) continue;
            await _projectService.SetSceneDateAsync(target.ChapterGuid, target.Scene.Id, next);
            moved++;
        }

        return moved;
    }

    /// <summary>The date a scene reads by: its range start when it has one, its
    /// plain date otherwise. Matches what the calendar and timeline show.</summary>
    private static string StartDateOf(SceneData scene)
        => scene.DateRange?.HasValue == true && !string.IsNullOrWhiteSpace(scene.DateRange.Start)
            ? scene.DateRange.Start
            : scene.Date;

    /// <summary>Adds days to a date string, leaving a blank one blank. The
    /// calendar service already returns the input unchanged when it cannot parse,
    /// so an unparseable date is preserved rather than blanked.</summary>
    private string Shift(string raw, long days, InWorldCalendar? calendar)
        => string.IsNullOrWhiteSpace(raw) ? raw : _calendar.AddDays(raw, days, calendar);
}
