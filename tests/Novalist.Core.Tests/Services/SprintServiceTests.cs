using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// The record of writing sprints.
///
/// Novalist's smallest unit was a calendar day, so "how did this sitting go"
/// had no answer. The timer runs in the renderer; this is the part that has to
/// survive closing the app.
/// </summary>
public class SprintServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProjectService _projects = new(new FileService());
    private readonly SprintService _sut;

    public SprintServiceTests()
    {
        _sut = new SprintService(_projects);
    }

    public void Dispose() => _dir.Dispose();

    private Task BookAsync() => _projects.CreateProjectAsync(_dir.Path, "P", "Book");

    private Task<IReadOnlyList<WritingSprint>> RecordAsync(
        int seconds = 600, int target = 10, int words = 500)
        => _sut.RecordAsync(seconds, target, words, new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc));

    [Fact]
    public async Task AFreshProjectHasNoSprints()
    {
        await BookAsync();

        Assert.Empty(_sut.History());
        Assert.Equal(0, _sut.Summary().Count);
    }

    [Fact]
    public async Task ASprintIsRecorded()
    {
        await BookAsync();

        var history = await RecordAsync();

        Assert.Single(history);
        Assert.Equal(500, history[0].Words);
        Assert.Equal(600, history[0].Seconds);
    }

    [Fact]
    public async Task HistoryIsNewestFirst()
    {
        await BookAsync();
        await RecordAsync(words: 100);
        await RecordAsync(words: 200);

        Assert.Equal(200, _sut.History()[0].Words);
    }

    [Fact]
    public async Task PaceIsDerivedFromTheSprint()
    {
        await BookAsync();

        var history = await RecordAsync(seconds: 600, words: 500);

        Assert.Equal(50, history[0].WordsPerMinute);
    }

    [Fact]
    public async Task ASprintTooShortToDivideByReportsNoPace()
    {
        // A five-second sprint that produced one word is not a 12 wpm pace.
        await BookAsync();

        var history = await RecordAsync(seconds: 5, words: 1);

        Assert.Equal(0, history[0].WordsPerMinute);
    }

    [Fact]
    public async Task ATimerStartedAndImmediatelyStoppedIsNotASitting()
    {
        // Keeping it would drag every average down.
        await BookAsync();

        Assert.Empty(await RecordAsync(seconds: 3, words: 0));
    }

    [Fact]
    public async Task ASprintOfNoTimeIsDropped()
    {
        await BookAsync();

        Assert.Empty(await RecordAsync(seconds: 0, words: 100));
    }

    [Fact]
    public async Task ADeletionPassRecordsAsZeroRatherThanNegative()
    {
        await BookAsync();

        var history = await RecordAsync(words: -400);

        Assert.Equal(0, history[0].Words);
    }

    [Fact]
    public async Task SprintsSurviveAReload()
    {
        await BookAsync();
        await RecordAsync();
        var root = _projects.ProjectRoot!;

        var reopened = new ProjectService(new FileService());
        await reopened.LoadProjectAsync(root);

        Assert.Single(new SprintService(reopened).History());
    }

    [Fact]
    public async Task TheHistoryDoesNotGrowWithoutBound()
    {
        await BookAsync();
        for (var i = 0; i < SprintService.HistoryLimit + 10; i++) await RecordAsync(words: i + 1);

        Assert.Equal(SprintService.HistoryLimit, _sut.History().Count);
        // The oldest go, not the newest.
        Assert.Equal(SprintService.HistoryLimit + 10, _sut.History()[0].Words);
    }

    [Fact]
    public async Task ClearingEmptiesTheHistory()
    {
        await BookAsync();
        await RecordAsync();

        await _sut.ClearAsync();

        Assert.Empty(_sut.History());
    }

    [Fact]
    public async Task ClearingAnEmptyHistoryIsHarmless()
    {
        await BookAsync();

        await _sut.ClearAsync();

        Assert.Empty(_sut.History());
    }

    // ── The summary ──

    [Fact]
    public async Task TheSummaryAddsUpEverySprint()
    {
        await BookAsync();
        await RecordAsync(seconds: 600, words: 500);
        await RecordAsync(seconds: 600, words: 700);

        var summary = _sut.Summary();

        Assert.Equal(2, summary.Count);
        Assert.Equal(1200, summary.TotalWords);
        Assert.Equal(1200, summary.TotalSeconds);
        Assert.Equal(700, summary.BestWords);
    }

    [Fact]
    public async Task TheAveragePaceWeightsByTimeRatherThanBySprint()
    {
        // A two-minute sprint should not count as much as an hour.
        await BookAsync();
        await RecordAsync(seconds: 120, words: 400);   // 200 wpm
        await RecordAsync(seconds: 3600, words: 3000); // 50 wpm

        // Time-weighted: 3400 words over 3720 seconds is about 55, not the
        // 125 an unweighted mean of the two rates would give.
        Assert.Equal(55, _sut.Summary().AverageWordsPerMinute);
    }

    [Fact]
    public async Task RecordingWithNoProjectOpenDoesNothing()
    {
        Assert.Empty(await RecordAsync());
    }
}
