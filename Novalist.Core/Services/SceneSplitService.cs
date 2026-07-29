using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>
/// Splitting one scene into two and merging two back into one.
///
/// Doing this by hand means creating a scene, cutting, pasting, and then
/// repairing order, synopsis, date, plotlines, stage and analysis overrides one
/// at a time - which is where the mistakes come from. The metadata carry-over
/// is the whole feature; moving the text is the easy part.
/// </summary>
public sealed class SceneSplitService
{
    private readonly IProjectService _projectService;

    public SceneSplitService(IProjectService projectService)
    {
        _projectService = projectService;
    }

    /// <summary>
    /// Splits a scene in two. The first half keeps the original scene, its id
    /// and its history; the second becomes a new scene immediately after it.
    ///
    /// The new scene inherits the metadata that still describes it - the same
    /// day, the same plotlines, the same revision stage, the same POV - because
    /// a scene split in half is still the same scene twice over. What it does
    /// not inherit is the synopsis: that described the whole, and leaving a copy
    /// on both halves would make two scenes claim to be about the same thing.
    /// </summary>
    public async Task<SceneData?> SplitAsync(
        string chapterGuid, string sceneId, string beforeHtml, string afterHtml, string newTitle)
    {
        var chapter = _projectService.GetChaptersOrdered()
            .FirstOrDefault(c => c.Guid == chapterGuid);
        var original = chapter == null
            ? null
            : _projectService.GetScenesForChapter(chapterGuid).FirstOrDefault(s => s.Id == sceneId);
        if (chapter == null || original == null) return null;

        var title = string.IsNullOrWhiteSpace(newTitle)
            ? ContinuationTitle(original.Title)
            : newTitle.Trim();

        var created = await _projectService.CreateSceneAsync(chapterGuid, title);
        CarryMetadata(original, created);

        await _projectService.WriteSceneContentAsync(chapter, original, beforeHtml);
        await _projectService.WriteSceneContentAsync(chapter, created, afterHtml);

        // CreateSceneAsync appends, so the new half has to be moved up to sit
        // directly after the scene it came out of.
        var scenes = _projectService.GetScenesForChapter(chapterGuid).OrderBy(s => s.Order).ToList();
        scenes.Remove(created);
        scenes.Insert(scenes.IndexOf(original) + 1, created);
        for (var i = 0; i < scenes.Count; i++) scenes[i].Order = i + 1;

        await _projectService.SaveScenesAsync();
        return created;
    }

    /// <summary>
    /// Merges the second scene into the first and deletes it.
    ///
    /// The first scene wins every conflict, because it is the one that survives
    /// and the writer chose the direction. Plotlines are the exception: they are
    /// unioned, since a merged scene genuinely belongs to both threads.
    /// </summary>
    public async Task<bool> MergeAsync(string chapterGuid, string firstSceneId, string secondSceneId)
    {
        if (string.Equals(firstSceneId, secondSceneId, StringComparison.Ordinal)) return false;

        var chapter = _projectService.GetChaptersOrdered()
            .FirstOrDefault(c => c.Guid == chapterGuid);
        if (chapter == null) return false;

        var scenes = _projectService.GetScenesForChapter(chapterGuid);
        var first = scenes.FirstOrDefault(s => s.Id == firstSceneId);
        var second = scenes.FirstOrDefault(s => s.Id == secondSceneId);
        if (first == null || second == null) return false;

        var firstHtml = await _projectService.ReadSceneContentAsync(chapter, first);
        var secondHtml = await _projectService.ReadSceneContentAsync(chapter, second);
        await _projectService.WriteSceneContentAsync(chapter, first, firstHtml + secondHtml);

        first.WordCount += second.WordCount;

        // A synopsis on only one of them is better kept than dropped.
        if (string.IsNullOrWhiteSpace(first.Synopsis) && !string.IsNullOrWhiteSpace(second.Synopsis))
            first.Synopsis = second.Synopsis;
        if (string.IsNullOrWhiteSpace(first.Notes) && !string.IsNullOrWhiteSpace(second.Notes))
            first.Notes = second.Notes;

        if (second.PlotlineIds is { Count: > 0 })
        {
            first.PlotlineIds = [.. (first.PlotlineIds ?? [])
                .Concat(second.PlotlineIds)
                .Distinct(StringComparer.Ordinal)];
        }

        await _projectService.DeleteSceneAsync(chapterGuid, secondSceneId);
        await _projectService.SaveScenesAsync();
        return true;
    }

    /// <summary>
    /// A default name for the second half. "Arrival" becomes "Arrival (2)", and
    /// splitting that again gives "Arrival (3)" rather than "Arrival (2) (2)".
    /// </summary>
    internal static string ContinuationTitle(string title)
    {
        var trimmed = (title ?? string.Empty).Trim();
        if (trimmed.Length == 0) return "(2)";

        if (trimmed.EndsWith(')') )
        {
            var open = trimmed.LastIndexOf('(');
            if (open > 0 && int.TryParse(trimmed[(open + 1)..^1], out var n) && n > 0)
                return $"{trimmed[..open].TrimEnd()} ({n + 1})";
        }

        return $"{trimmed} (2)";
    }

    /// <summary>
    /// What still describes both halves after a split. Deliberately not the
    /// synopsis, which described the whole and would make both halves claim to
    /// be about the same thing.
    /// </summary>
    private static void CarryMetadata(SceneData from, SceneData to)
    {
        to.Date = from.Date;
        to.DateRange = from.DateRange?.Clone();
        to.Stage = from.Stage;
        to.LabelColor = from.LabelColor;
        to.PlotlineIds = from.PlotlineIds is { Count: > 0 } ? [.. from.PlotlineIds] : null;
        to.AnalysisOverrides = from.AnalysisOverrides?.Clone();
    }
}
