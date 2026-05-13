using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>
/// Tracks per-day, per-scene word totals so the Dashboard and other surfaces
/// can show today's progress, a rolling history chart, and streak counts.
/// Storage is append-only JSON Lines at .novalist/word-history.jsonl.
/// </summary>
public interface IWordHistoryService
{
    /// <summary>Loads the on-disk journal into memory. Call after project open.</summary>
    Task LoadAsync();

    /// <summary>Resets the cache (used when project is closed).</summary>
    void Reset();

    /// <summary>Seeds the journal from the legacy single-day baseline fields on
    /// <see cref="ProjectSettings.WordCountGoals"/>. No-op if the journal already
    /// has rows. Writes a delta=0 baseline row per scene so today's totals do not
    /// over-count work that pre-dates the rework.</summary>
    Task MigrateLegacyBaselineAsync();

    /// <summary>Records a save for the given scene with its absolute word count.
    /// Computes the delta vs. the previous row for the same scene and persists.</summary>
    Task RecordSaveAsync(string bookId, string sceneId, int wordsAfterSave);

    /// <summary>Sum of positive deltas on the given local date.</summary>
    int TotalForDay(DateOnly date, string? bookId = null);

    /// <summary>Returns history rows in [from, to] inclusive.</summary>
    IReadOnlyList<WordHistoryEntry> ReadRange(DateOnly from, DateOnly to, string? bookId = null);

    /// <summary>Consecutive-days streak ending today where day total >= dailyGoal.</summary>
    int CurrentStreak(DateOnly today, int dailyGoal, string? bookId = null);

    /// <summary>Per-scene delta map for the given local date.</summary>
    IReadOnlyDictionary<string, int> ScenesTouchedOn(DateOnly date, string? bookId = null);

    /// <summary>Fires after every successful <see cref="RecordSaveAsync"/>.</summary>
    event Action? HistoryChanged;
}
