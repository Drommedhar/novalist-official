using Novalist.Core.Services;
using Xunit;
using NarrationSegment = Novalist.Core.Services.NarrationSegment;
using VoiceDirection = Novalist.Core.Services.VoiceDirection;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Covers the figures shown before a render starts, and the record of how fast
/// this machine actually is.
///
/// The estimate exists because the honest answer to "how long will this take"
/// is between four minutes and nine hours depending on the machine, and a
/// writer who starts an overnight job thinking it is a coffee break has been
/// misled by us rather than by their hardware.
/// </summary>
public class NarrationEstimateTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "nl-speed-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static NarrationSegment Segment(string text)
        => new(
            0, NarrationSegmentKind.Narration, "k", "k", text, null,
            DialogueConfidence.None, [],
            new VoiceDirection("neutral", new Dictionary<string, double>(), DirectionSource.None),
            0, text.Length);

    private static NarrationRenderChapter Chapter(string guid, params string[] lines)
        => new(guid, guid, [new NarrationRenderScene("s", [.. lines.Select(Segment)])]);

    // ─── the estimate ───────────────────────────────────────────────

    [Fact]
    public void ItCountsWhatIsThere()
    {
        var estimate = NarrationEstimator.Estimate([
            Chapter("a", "one two three", "four five"),
            Chapter("b", "six")
        ]);

        Assert.Equal(2, estimate.Chapters);
        Assert.Equal(2, estimate.Scenes);
        Assert.Equal(3, estimate.Segments);
        Assert.Equal(6, estimate.Words);
    }

    [Fact]
    public void ADurationComesFromTheWordCount()
    {
        // 155 words is a minute of audiobook, which is the industry's own figure.
        var words = string.Join(' ', Enumerable.Repeat("word", 155));

        var estimate = NarrationEstimator.Estimate([Chapter("a", words)]);

        Assert.Equal(60_000, estimate.AudioMs, 0);
    }

    [Fact]
    public void WithNoMeasurementFromThisMachine_TheWallClockIsNotGuessedAt()
    {
        var estimate = NarrationEstimator.Estimate([Chapter("a", "hello there")]);

        Assert.Null(estimate.WallClockMs);
        Assert.False(estimate.Measured);
    }

    [Fact]
    public void WithAMeasurement_TheWallClockIsTheAudioTimesIt()
    {
        var words = string.Join(' ', Enumerable.Repeat("word", 155));

        var estimate = NarrationEstimator.Estimate([Chapter("a", words)], null, 20);

        Assert.True(estimate.Measured);
        Assert.Equal(1_200_000, estimate.WallClockMs!.Value, 0);
    }

    [Fact]
    public void ChaptersAlreadyRendered_AreNotCountedIntoTheWaiting()
    {
        var words = string.Join(' ', Enumerable.Repeat("word", 155));

        var estimate = NarrationEstimator.Estimate(
            [Chapter("a", words), Chapter("b", words)], ["a"], 10);

        Assert.Equal(2, estimate.Chapters);
        Assert.Equal(1, estimate.ChaptersToRender);
        // The whole book is two minutes of audio; only one minute is still to be
        // spoken, and the wait is for that minute.
        Assert.Equal(120_000, estimate.AudioMs, 0);
        Assert.Equal(600_000, estimate.WallClockMs!.Value, 0);
    }

    [Fact]
    public void AnEmptyBook_EstimatesNothingRatherThanFailing()
    {
        var estimate = NarrationEstimator.Estimate([]);

        Assert.Equal(0, estimate.Chapters);
        Assert.Equal(0, estimate.AudioMs);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("one", 1)]
    [InlineData("one  two\tthree\nfour", 4)]
    public void WordsAreCountedTheWayAWordsAMinuteFigureCountsThem(string? text, int expected)
        => Assert.Equal(expected, NarrationEstimator.Words(text));

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(70_000, "1:10")]
    [InlineData(3_600_000, "1:00:00")]
    [InlineData(33_845_000, "9:24:05")]
    [InlineData(-500, "0:00")]
    public void ADurationReadsTheWayADurationReads(double milliseconds, string expected)
        => Assert.Equal(expected, NarrationEstimator.Duration(milliseconds));

    // ─── the speed log ──────────────────────────────────────────────

    [Fact]
    public void BeforeAnythingHasBeenRendered_ThereIsNoFactor()
        => Assert.Null(new NarrationSpeedLog(_folder).Factor());

    [Fact]
    public void ARenderIsRemembered_AsWorkPerSecondOfAudio()
    {
        var log = new NarrationSpeedLog(_folder);

        log.Record(audioMs: 60_000, elapsedMs: 1_200_000);

        Assert.Equal(20, log.Factor()!.Value, 3);
    }

    [Fact]
    public void SeveralRendersAreAveraged_SoOneSlowChapterIsNotTheEstimate()
    {
        var log = new NarrationSpeedLog(_folder);

        log.Record(60_000, 600_000);
        log.Record(60_000, 1_200_000);

        Assert.Equal(15, log.Factor()!.Value, 3);
    }

    [Fact]
    public void OnlyTheLastFewRendersCount_SoAMachineThatGotFasterIsBelieved()
    {
        var log = new NarrationSpeedLog(_folder);
        for (var i = 0; i < 5; i++)
            log.Record(60_000, 6_000_000);

        for (var i = 0; i < 5; i++)
            log.Record(60_000, 60_000);

        Assert.Equal(1, log.Factor()!.Value, 3);
    }

    [Fact]
    public void AnAudition_IsTooShortToSayAnythingAboutSpeed()
    {
        // A two-line render is nearly all model loading.
        var log = new NarrationSpeedLog(_folder);

        log.Record(audioMs: 900, elapsedMs: 90_000);

        Assert.Null(log.Factor());
    }

    [Fact]
    public void ARenderThatTookNoTime_IsNotRecorded()
    {
        var log = new NarrationSpeedLog(_folder);

        log.Record(audioMs: 60_000, elapsedMs: 0);

        Assert.Null(log.Factor());
    }

    [Fact]
    public void ARecordNobodyCanRead_IsTreatedAsNoRecord()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, NarrationSpeedLog.FileName), "{ not a list");

        Assert.Null(new NarrationSpeedLog(_folder).Factor());
    }

    [Fact]
    public void ARecordHoldingNonsense_IsIgnoredRatherThanUsed()
    {
        // A zero or a not-a-number in the file would turn every estimate into
        // zero or into nothing at all.
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, NarrationSpeedLog.FileName), "[0, -4, 12]");

        Assert.Equal(12, new NarrationSpeedLog(_folder).Factor()!.Value, 3);
    }

    [Fact]
    public void ARecordThatCannotBeWritten_DoesNotFailTheRenderThatProducedIt()
    {
        // The settings folder is a file. Writing an estimate down is a
        // convenience; losing one must not lose the render.
        var blocked = Path.Combine(_folder, "blocked");
        Directory.CreateDirectory(_folder);
        File.WriteAllText(blocked, "not a folder");
        var log = new NarrationSpeedLog(blocked);

        log.Record(60_000, 600_000);

        Assert.Null(log.Factor());
    }

    [Fact]
    public void WithNoFolderNamed_ItLivesBesideTheOtherSettings()
    {
        // Constructed rather than written to: the assertion is that naming
        // nothing is allowed, not that the real settings folder gets a file.
        var log = new NarrationSpeedLog();

        Assert.NotNull(log);
    }
}
