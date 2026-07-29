using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// The record of writing sprints. The timer runs in the renderer, where the
/// clock and the live word count already are; this is the part that has to
/// survive closing the app.
/// </summary>
public sealed class SprintRpc
{
    private readonly Workspace _workspace;

    public SprintRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private SprintService Service => new(_workspace.Projects);

    [JsonRpcMethod("sprints/history")]
    public SprintHistoryDto History() => Build();

    /// <summary>Records a finished sprint and returns the updated history.</summary>
    [JsonRpcMethod("sprints/record")]
    public async Task<SprintHistoryDto> RecordAsync(
        int seconds, int targetMinutes, int words, string startedAtIso)
    {
        var startedAt = DateTime.TryParse(
            startedAtIso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            // An unparseable stamp still records the sprint: losing the sitting
            // over a bad timestamp would be the worse failure.
            : DateTime.UtcNow;

        await Service.RecordAsync(seconds, targetMinutes, words, startedAt);
        return Build();
    }

    [JsonRpcMethod("sprints/clear")]
    public async Task<SprintHistoryDto> ClearAsync()
    {
        await Service.ClearAsync();
        return Build();
    }

    private SprintHistoryDto Build()
    {
        var summary = Service.Summary();
        return new SprintHistoryDto(
            [.. Service.History().Select(s => new SprintDto(
                s.StartedAt.ToString("o"), s.Seconds, s.TargetMinutes, s.Words, s.WordsPerMinute))],
            new SprintSummaryDto(
                summary.Count, summary.TotalWords, summary.TotalSeconds,
                summary.BestWords, summary.AverageWordsPerMinute));
    }
}

/// <summary>One finished sprint. <c>WordsPerMinute</c> is zero for a sprint too
/// short to divide by.</summary>
public sealed record SprintDto(
    string StartedAt, int Seconds, int TargetMinutes, int Words, int WordsPerMinute);

public sealed record SprintSummaryDto(
    int Count, int TotalWords, int TotalSeconds, int BestWords, int AverageWordsPerMinute);

public sealed record SprintHistoryDto(SprintDto[] Sprints, SprintSummaryDto Summary);
