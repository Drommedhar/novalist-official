using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>How a writer has been doing, across their finished sprints.</summary>
public sealed record SprintSummary(
    int Count,
    int TotalWords,
    int TotalSeconds,
    int BestWords,
    int AverageWordsPerMinute);

/// <summary>
/// The record of writing sprints.
///
/// Novalist's smallest unit was a calendar day, so "how did this sitting go"
/// had no answer. The timer itself lives in the renderer, which is where the
/// clock and the live word count already are; this is the part that has to
/// survive closing the app.
/// </summary>
public sealed class SprintService
{
    private readonly IProjectService _projectService;

    /// <summary>How many sprints are kept. Enough to see a pattern, few enough
    /// that the settings file does not grow without bound over a novel.</summary>
    internal const int HistoryLimit = 200;

    public SprintService(IProjectService projectService)
    {
        _projectService = projectService;
    }

    /// <summary>Finished sprints, newest first.</summary>
    public IReadOnlyList<WritingSprint> History()
        => [.. _projectService.ProjectSettings.Sprints.AsEnumerable().Reverse()];

    /// <summary>
    /// Records a finished sprint. One that produced nothing in no time is
    /// dropped: a timer started and immediately stopped is not a sitting, and
    /// keeping it would drag every average down.
    /// </summary>
    public async Task<IReadOnlyList<WritingSprint>> RecordAsync(
        int seconds, int targetMinutes, int words, DateTime startedAt)
    {
        // Nowhere to persist to without a project, and the settings object
        // exists regardless - so the load state is the real condition.
        if (!_projectService.IsProjectLoaded) return [];
        if (seconds <= 0 || (words <= 0 && seconds < WritingSprint.MinimumSecondsForPace))
            return History();

        var settings = _projectService.ProjectSettings;

        settings.Sprints.Add(new WritingSprint
        {
            StartedAt = startedAt,
            Seconds = seconds,
            TargetMinutes = Math.Max(0, targetMinutes),
            // A sprint that ended below where it started (a deletion pass) is
            // recorded as zero rather than as negative progress.
            Words = Math.Max(0, words)
        });

        if (settings.Sprints.Count > HistoryLimit)
            settings.Sprints.RemoveRange(0, settings.Sprints.Count - HistoryLimit);

        await _projectService.SaveProjectSettingsAsync();
        return History();
    }

    /// <summary>Clears the history.</summary>
    public async Task ClearAsync()
    {
        var settings = _projectService.ProjectSettings;
        if (!_projectService.IsProjectLoaded || settings.Sprints.Count == 0) return;
        settings.Sprints.Clear();
        await _projectService.SaveProjectSettingsAsync();
    }

    /// <summary>
    /// The totals across every recorded sprint. The average pace weights by
    /// time rather than averaging each sprint's rate, so a two-minute sprint
    /// does not count as much as an hour.
    /// </summary>
    public SprintSummary Summary()
    {
        var sprints = _projectService.ProjectSettings.Sprints;
        if (sprints.Count == 0) return new SprintSummary(0, 0, 0, 0, 0);

        var seconds = sprints.Sum(s => s.Seconds);
        var words = sprints.Sum(s => s.Words);
        return new SprintSummary(
            sprints.Count,
            words,
            seconds,
            sprints.Max(s => s.Words),
            seconds >= WritingSprint.MinimumSecondsForPace
                ? (int)Math.Round(words * 60.0 / seconds)
                : 0);
    }
}
