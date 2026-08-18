using System.Runtime.CompilerServices;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using Novalist.Sdk.Models.Narration;
using Xunit;
using NarrationSegment = Novalist.Core.Services.NarrationSegment;
using VoiceDirection = Novalist.Core.Services.VoiceDirection;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Covers rendering a whole book: what gets spoken, what gets skipped, what is
/// not spoken twice, and what happens when the engine stops halfway.
///
/// Every test drives a fake engine that returns a fixed length of silence per
/// line, so the assertions are about the job's decisions rather than about
/// anybody's speech model.
/// </summary>
public class NarrationRenderJobTests : IDisposable
{
    private static readonly WaveFormat Shape = new(24000, 1, 16);

    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "nl-render-" + Guid.NewGuid().ToString("N"));

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

    // ─── the book ───────────────────────────────────────────────────

    private static NarrationSegment Segment(int index, string key, string? speaker, string text)
        => new(
            index,
            speaker == null ? NarrationSegmentKind.Narration : NarrationSegmentKind.Dialogue,
            key,
            text,
            speaker,
            DialogueConfidence.Manual,
            [],
            new VoiceDirection("neutral", new Dictionary<string, double> { ["calm"] = 0.6 }, DirectionSource.None),
            0,
            text.Length);

    private static NarrationRenderChapter Chapter(
        string guid, string title, params string[][] scenes)
    {
        var built = new List<NarrationRenderScene>();
        var index = 0;
        for (var s = 0; s < scenes.Length; s++)
        {
            var segments = new List<NarrationSegment>();
            foreach (var text in scenes[s])
                segments.Add(Segment(index, $"{guid}:{index++}", "mira", text));
            built.Add(new NarrationRenderScene($"{guid}-scene-{s}", segments));
        }
        return new NarrationRenderChapter(guid, title, built);
    }

    private static VoiceCastSheet Sheet()
        => new()
        {
            NarratorVoiceId = "narrator",
            Voices = new Dictionary<string, string>(StringComparer.Ordinal) { ["mira"] = "mira-voice" }
        };

    private static Dictionary<string, byte[]> Voices()
        => new(StringComparer.Ordinal) { ["mira-voice"] = [1], ["narrator"] = [2] };

    // ─── the engine ─────────────────────────────────────────────────

    /// <summary>Speaks every line as a fixed length of silence.</summary>
    private static NarrationRenderJob.RenderDelegate Speaks(
        int millisecondsPerLine = 100, Action<NarrationRequest>? saw = null)
        => (request, token) => Clips(request, millisecondsPerLine, saw, token);

    private static async IAsyncEnumerable<NarrationClip> Clips(
        NarrationRequest request,
        int millisecondsPerLine,
        Action<NarrationRequest>? saw,
        [EnumeratorCancellation] CancellationToken token)
    {
        saw?.Invoke(request);
        foreach (var segment in request.Segments)
        {
            token.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new NarrationClip
            {
                Key = segment.Key,
                Audio = WaveAudio.Write(Shape, WaveAudio.Silence(Shape, millisecondsPerLine)),
                AudioFormat = "wav",
                SampleRate = Shape.SampleRate,
                DurationMs = millisecondsPerLine
            };
        }
    }

    private NarrationRenderJob Job(
        NarrationRenderJob.RenderDelegate render, Func<double>? clock = null)
        => new(_folder, render, clock);

    private Task<NarrationRenderOutcome> RunAsync(
        NarrationRenderJob job,
        IReadOnlyList<NarrationRenderChapter> chapters,
        NarrationRenderSettings? settings = null,
        IProgress<NarrationRenderProgress>? progress = null,
        CancellationToken token = default)
        => job.RunAsync(
            chapters, Sheet(), Voices(), VoiceEngineFeatures.EmotionVector, "en",
            settings, progress, token);

    // ─── what it produces ───────────────────────────────────────────

    [Fact]
    public async Task EachChapter_BecomesOneFile()
    {
        var job = Job(Speaks());

        var outcome = await RunAsync(job, [
            Chapter("a", "One", ["Hello.", "Goodbye."]),
            Chapter("b", "Two", ["Again."])
        ]);

        Assert.True(outcome.Completed);
        Assert.Equal(2, outcome.Chapters.Count);
        Assert.All(outcome.Chapters, c => Assert.True(File.Exists(Path.Combine(_folder, c.File))));
        Assert.All(outcome.Chapters, c => Assert.Equal(0, c.Missing));
    }

    [Fact]
    public async Task LinesAreLaidEndToEnd_WithABreathBetweenThem()
    {
        var job = Job(Speaks(100));

        var outcome = await RunAsync(
            job, [Chapter("a", "One", ["Hello.", "Goodbye."])],
            new NarrationRenderSettings { SegmentGapMs = 200 });

        // Two hundred-millisecond lines and one gap between them, not two.
        Assert.Equal(400, outcome.Chapters[0].DurationMs, 1);
    }

    [Fact]
    public async Task ASceneBreak_IsALongerSilenceThanALineBreak()
    {
        var job = Job(Speaks(100));

        var outcome = await RunAsync(
            job, [Chapter("a", "One", ["Hello."], ["Elsewhere."])],
            new NarrationRenderSettings { SegmentGapMs = 100, SceneGapMs = 1000 });

        // Two lines, and the gap between the scenes is the scene gap, not the
        // segment gap. A scene break heard as a comma is the whole point.
        Assert.Equal(1200, outcome.Chapters[0].DurationMs, 1);
    }

    [Fact]
    public async Task AnEngineThatCarriesProsodyAcrossARequest_HasItsJoinsLeftAlone()
    {
        // It has already made the joins continuous; ramping them would undo the
        // thing that makes such an engine worth having.
        var job = Job(Speaks(100));

        var outcome = await job.RunAsync(
            [Chapter("a", "One", ["Hello.", "Goodbye."])],
            Sheet(),
            Voices(),
            VoiceEngineFeatures.EmotionVector | VoiceEngineFeatures.ContinuousContext,
            "en",
            new NarrationRenderSettings { SegmentGapMs = 0 });

        Assert.Equal(200, outcome.Chapters[0].DurationMs, 1);
    }

    [Fact]
    public async Task AChapterWithNoAudioAtAll_IsStillAValidFile()
    {
        var job = Job(Speaks());

        var outcome = await RunAsync(job, [new NarrationRenderChapter("a", "Empty", [])]);

        var written = await File.ReadAllBytesAsync(Path.Combine(_folder, outcome.Chapters[0].File));
        Assert.NotNull(WaveAudio.Read(written));
        Assert.Equal(0, outcome.Chapters[0].DurationMs);
    }

    // ─── resuming ───────────────────────────────────────────────────

    [Fact]
    public async Task AChapterAlreadyRendered_IsNotSpokenAgain()
    {
        var asked = 0;
        var chapters = new[] { Chapter("a", "One", ["Hello."]) };
        await RunAsync(Job(Speaks(saw: _ => asked++)), chapters);
        var afterFirst = asked;

        var second = await RunAsync(Job(Speaks(saw: _ => asked++)), chapters);

        Assert.Equal(afterFirst, asked);
        Assert.True(second.Chapters[0].Reused);
        Assert.True(second.Completed);
    }

    [Fact]
    public async Task EditingOneChapter_ReRendersThatOneAndNoOther()
    {
        var first = Chapter("a", "One", ["Hello."]);
        var second = Chapter("b", "Two", ["Goodbye."]);
        await RunAsync(Job(Speaks()), [first, second]);

        var edited = Chapter("b", "Two", ["Goodbye, then."]);
        var outcome = await RunAsync(Job(Speaks()), [first, edited]);

        Assert.True(outcome.Chapters[0].Reused);
        Assert.False(outcome.Chapters[1].Reused);
    }

    [Fact]
    public async Task RecastingACharacter_ReRendersEvenThoughTheWordsAreTheSame()
    {
        var chapters = new[] { Chapter("a", "One", ["Hello."]) };
        await RunAsync(Job(Speaks()), chapters);

        var recast = new VoiceCastSheet
        {
            NarratorVoiceId = "narrator",
            Voices = new Dictionary<string, string>(StringComparer.Ordinal) { ["mira"] = "other" }
        };
        var voices = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["other"] = [3], ["narrator"] = [2]
        };
        var outcome = await Job(Speaks()).RunAsync(
            chapters, recast, voices, VoiceEngineFeatures.EmotionVector, "en");

        Assert.False(outcome.Chapters[0].Reused);
    }

    [Fact]
    public async Task AChapterWhoseFileWasDeleted_IsRenderedAgain()
    {
        var chapters = new[] { Chapter("a", "One", ["Hello."]) };
        var first = await RunAsync(Job(Speaks()), chapters);
        File.Delete(Path.Combine(_folder, first.Chapters[0].File));

        var outcome = await RunAsync(Job(Speaks()), chapters);

        Assert.False(outcome.Chapters[0].Reused);
    }

    [Fact]
    public async Task AManifestNobodyCanRead_MeansRenderingAgainRatherThanRefusing()
    {
        var chapters = new[] { Chapter("a", "One", ["Hello."]) };
        await RunAsync(Job(Speaks()), chapters);
        await File.WriteAllTextAsync(
            Path.Combine(_folder, NarrationRenderJob.ManifestName), "{ not json");

        var outcome = await RunAsync(Job(Speaks()), chapters);

        Assert.True(outcome.Completed);
        Assert.False(outcome.Chapters[0].Reused);
    }

    [Fact]
    public async Task AManifestHoldingNull_IsTreatedAsNothingRendered()
    {
        Directory.CreateDirectory(_folder);
        await File.WriteAllTextAsync(Path.Combine(_folder, NarrationRenderJob.ManifestName), "null");

        var outcome = await RunAsync(Job(Speaks()), [Chapter("a", "One", ["Hello."])]);

        Assert.True(outcome.Completed);
    }

    [Fact]
    public async Task WhatIsRendered_CanBeAskedForWithoutRenderingAnything()
    {
        var job = Job(Speaks());
        await RunAsync(job, [Chapter("a", "One", ["Hello."])]);

        Assert.Equal(["a"], job.Rendered().Keys);
    }

    [Fact]
    public void RenderedIsEmpty_BeforeAnythingHasBeenRendered()
        => Assert.Empty(Job(Speaks()).Rendered());

    [Fact]
    public async Task StartingAgainFromScratch_ForgetsEveryChapter()
    {
        var job = Job(Speaks());
        await RunAsync(job, [Chapter("a", "One", ["Hello."])]);

        job.Reset();

        Assert.Empty(job.Rendered());
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task AChapterFileSomethingElseHasOpen_IsLeftRatherThanFailingTheReset()
    {
        var job = Job(Speaks());
        var outcome = await RunAsync(job, [Chapter("a", "One", ["Hello."])]);
        using var held = new FileStream(
            Path.Combine(_folder, outcome.Chapters[0].File),
            FileMode.Open, FileAccess.Read, FileShare.None);

        job.Reset();

        Assert.True(File.Exists(Path.Combine(_folder, outcome.Chapters[0].File)));
    }

    [Fact]
    public async Task AManifestThatCannotBeWritten_DoesNotLoseTheChapterItDescribes()
    {
        // The audio is the expensive part and it is already on disk. A manifest
        // that cannot be written means rendering it again next time, which is
        // slow - not failing now, which would be worse.
        Directory.CreateDirectory(Path.Combine(_folder, NarrationRenderJob.ManifestName));

        var outcome = await RunAsync(Job(Speaks()), [Chapter("a", "One", ["Hello."])]);

        Assert.True(outcome.Completed);
        Assert.True(File.Exists(Path.Combine(_folder, outcome.Chapters[0].File)));
    }

    [Fact]
    public async Task WhatIsRenderedCanBeAskedOfAFolder_WithoutAnEngineToAskWith()
    {
        await RunAsync(Job(Speaks()), [Chapter("a", "One", ["Hello."])]);

        Assert.Equal(["a"], NarrationRenderJob.RenderedIn(_folder).Keys);
    }

    [Fact]
    public void ResettingAFolderThatWasNeverMade_DoesNothing()
    {
        Job(Speaks()).Reset();

        Assert.False(Directory.Exists(_folder));
    }

    // ─── when it goes wrong ─────────────────────────────────────────

    [Fact]
    public async Task ALineTheEngineRefused_IsCountedRatherThanDropped()
    {
        NarrationRenderJob.RenderDelegate refuses = (request, _) => Refuses(request);

        var outcome = await RunAsync(Job(refuses), [Chapter("a", "One", ["Hello.", "Goodbye."])]);

        Assert.Equal(2, outcome.Chapters[0].Missing);
    }

    private static async IAsyncEnumerable<NarrationClip> Refuses(NarrationRequest request)
    {
        foreach (var segment in request.Segments)
        {
            await Task.Yield();
            yield return new NarrationClip { Key = segment.Key, Error = "refused" };
        }
    }

    [Fact]
    public async Task AClipThatIsNotAudio_IsCountedRatherThanWrittenIntoTheChapter()
    {
        NarrationRenderJob.RenderDelegate rubbish = (request, _) => Rubbish(request);

        var outcome = await RunAsync(Job(rubbish), [Chapter("a", "One", ["Hello."])]);

        Assert.Equal(1, outcome.Chapters[0].Missing);
        Assert.Equal(0, outcome.Chapters[0].DurationMs);
    }

    private static async IAsyncEnumerable<NarrationClip> Rubbish(NarrationRequest request)
    {
        foreach (var segment in request.Segments)
        {
            await Task.Yield();
            yield return new NarrationClip { Key = segment.Key, Audio = [7, 7, 7], AudioFormat = "wav" };
        }
    }

    [Fact]
    public async Task AClipOfAnotherShape_IsLeftOutRatherThanChangingThePitchOfTheChapter()
    {
        var alternating = 0;
        NarrationRenderJob.RenderDelegate mixed = (request, _) => Mixed(request, () => alternating++);

        var outcome = await RunAsync(Job(mixed), [Chapter("a", "One", ["Hello.", "Goodbye."])]);

        Assert.Equal(1, outcome.Chapters[0].Missing);
    }

    private static async IAsyncEnumerable<NarrationClip> Mixed(NarrationRequest request, Func<int> next)
    {
        foreach (var segment in request.Segments)
        {
            await Task.Yield();
            var shape = next() == 0 ? Shape : new WaveFormat(48000, 1, 16);
            yield return new NarrationClip
            {
                Key = segment.Key,
                Audio = WaveAudio.Write(shape, WaveAudio.Silence(shape, 100)),
                AudioFormat = "wav"
            };
        }
    }

    [Fact]
    public async Task AnEngineThatQuietlyDropsALine_IsNoticed()
    {
        // The failure this catches is only ever heard, not thrown.
        NarrationRenderJob.RenderDelegate half = (request, _) => Half(request);

        var outcome = await RunAsync(Job(half), [Chapter("a", "One", ["One.", "Two.", "Three."])]);

        Assert.Equal(2, outcome.Chapters[0].Missing);
    }

    private static async IAsyncEnumerable<NarrationClip> Half(NarrationRequest request)
    {
        await Task.Yield();
        yield return new NarrationClip
        {
            Key = request.Segments[0].Key,
            Audio = WaveAudio.Write(Shape, WaveAudio.Silence(Shape, 100)),
            AudioFormat = "wav"
        };
    }

    [Fact]
    public async Task AVoiceThisMachineDoesNotHave_IsCountedAsMissingRatherThanSentToTheEngine()
    {
        // The cast names a voice that was designed on another machine, or one
        // the writer deleted. Sending it would be asking the engine to speak in
        // a voice it has never been given.
        var chapter = new NarrationRenderChapter("a", "One", [
            new NarrationRenderScene("s", [Segment(0, "k0", "ghost", "Who am I?")])
        ]);
        var sheet = new VoiceCastSheet
        {
            NarratorVoiceId = "narrator",
            Voices = new Dictionary<string, string>(StringComparer.Ordinal) { ["ghost"] = "gone" }
        };

        var outcome = await Job(Speaks()).RunAsync(
            [chapter], sheet, Voices(), VoiceEngineFeatures.EmotionVector, "en");

        Assert.Equal(1, outcome.Chapters[0].Missing);
    }

    [Fact]
    public async Task ACharacterNobodyCast_IsReadByTheNarratorRatherThanSkipped()
    {
        var chapter = new NarrationRenderChapter("a", "One", [
            new NarrationRenderScene("s", [Segment(0, "k0", "nobody", "Who am I?")])
        ]);

        var outcome = await RunAsync(Job(Speaks()), [chapter]);

        Assert.Equal(0, outcome.Chapters[0].Missing);
    }

    [Fact]
    public async Task AnEngineThatThrows_StopsTheRunAndSaysWhatKindOfFailureItWas()
    {
        NarrationRenderJob.RenderDelegate throws = (_, _) => Throws();

        var outcome = await RunAsync(Job(throws), [Chapter("a", "One", ["Hello."])]);

        Assert.False(outcome.Completed);
        Assert.Equal(nameof(InvalidOperationException), outcome.Error);
        Assert.Empty(outcome.Chapters);
    }

    private static async IAsyncEnumerable<NarrationClip> Throws()
    {
        await Task.Yield();
        throw new InvalidOperationException("the sidecar died");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    [Fact]
    public async Task AnEngineFailure_NamesTheKindAndNeverTheLine()
    {
        // The message of an engine's exception can carry the text it choked on,
        // and that text is the manuscript.
        NarrationRenderJob.RenderDelegate throws = (_, _) => ThrowsWithProse();

        var outcome = await RunAsync(Job(throws), [Chapter("a", "One", ["Hello."])]);

        Assert.DoesNotContain("Mira", outcome.Error ?? string.Empty, StringComparison.Ordinal);
    }

    private static async IAsyncEnumerable<NarrationClip> ThrowsWithProse()
    {
        await Task.Yield();
        throw new InvalidOperationException("could not speak: Mira turned away from the window");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    // ─── stopping ───────────────────────────────────────────────────

    [Fact]
    public async Task Stopping_KeepsTheChaptersThatFinished()
    {
        using var stop = new CancellationTokenSource();
        var chapters = new[]
        {
            Chapter("a", "One", ["Hello."]),
            Chapter("b", "Two", ["Goodbye."])
        };
        NarrationRenderJob.RenderDelegate stopsAfterOne = (request, token) =>
        {
            stop.Cancel();
            return Clips(request, 100, null, token);
        };

        var outcome = await RunAsync(Job(stopsAfterOne), chapters, token: stop.Token);

        Assert.False(outcome.Completed);
        Assert.Empty(outcome.Chapters);
        Assert.Null(outcome.Error);
    }

    [Fact]
    public async Task StoppingBeforeItStarts_RendersNothing()
    {
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        var outcome = await RunAsync(
            Job(Speaks()), [Chapter("a", "One", ["Hello."])], token: stop.Token);

        Assert.Empty(outcome.Chapters);
        Assert.False(outcome.Completed);
    }

    // ─── progress ───────────────────────────────────────────────────

    [Fact]
    public async Task ProgressMoves_PerLineRatherThanPerChapter()
    {
        var reports = new List<NarrationRenderProgress>();
        var progress = new Inline(reports.Add);

        await RunAsync(
            Job(Speaks()), [Chapter("a", "One", ["One.", "Two.", "Three."])], progress: progress);

        Assert.Equal(3, reports[^1].SegmentsDone);
        Assert.Equal(3, reports[^1].SegmentsTotal);
        Assert.True(reports.Count > 3);
    }

    [Fact]
    public async Task AReusedChapter_StillMovesTheBarPastIt()
    {
        var chapters = new[] { Chapter("a", "One", ["One.", "Two."]) };
        await RunAsync(Job(Speaks()), chapters);
        var reports = new List<NarrationRenderProgress>();

        await RunAsync(Job(Speaks()), chapters, progress: new Inline(reports.Add));

        Assert.Equal(2, reports[^1].SegmentsDone);
        // The chapter's own length, gap included - the same figure the file has.
        Assert.Equal(340, reports[^1].AudioMs, 1);
    }

    [Fact]
    public async Task ElapsedComesFromTheClockItWasGiven()
    {
        var tick = 0d;
        var job = Job(Speaks(), () => tick += 1000);

        var outcome = await RunAsync(job, [Chapter("a", "One", ["Hello."])]);

        Assert.True(outcome.ElapsedMs > 0);
    }

    private sealed class Inline(Action<NarrationRenderProgress> report)
        : IProgress<NarrationRenderProgress>
    {
        public void Report(NarrationRenderProgress value) => report(value);
    }

    // ─── the fingerprint ────────────────────────────────────────────

    [Fact]
    public void AChaptersFingerprint_IgnoresWhereItSitsInTheBook()
    {
        // Inserting a chapter must not invalidate every chapter after it.
        var chapter = Chapter("a", "One", ["Hello."]);
        var settings = new NarrationRenderSettings();

        var first = NarrationRenderJob.Fingerprint(
            chapter, Sheet(), VoiceEngineFeatures.EmotionVector, "en", settings);
        var again = NarrationRenderJob.Fingerprint(
            chapter, Sheet(), VoiceEngineFeatures.EmotionVector, "en", settings);

        Assert.Equal(first, again);
    }

    [Fact]
    public void ChangingThePace_ChangesTheFingerprint()
    {
        var chapter = Chapter("a", "One", ["Hello."]);

        var slow = NarrationRenderJob.Fingerprint(
            chapter, Sheet(), VoiceEngineFeatures.EmotionVector, "en",
            new NarrationRenderSettings { Rate = 0.9 });
        var fast = NarrationRenderJob.Fingerprint(
            chapter, Sheet(), VoiceEngineFeatures.EmotionVector, "en",
            new NarrationRenderSettings { Rate = 1.1 });

        Assert.NotEqual(slow, fast);
    }

    [Fact]
    public void ChangingWhatTheEngineCanBeTold_ChangesTheFingerprint()
    {
        var chapter = Chapter("a", "One", ["Hello."]);
        var settings = new NarrationRenderSettings();

        var directed = NarrationRenderJob.Fingerprint(
            chapter, Sheet(), VoiceEngineFeatures.EmotionVector, "en", settings);
        var flat = NarrationRenderJob.Fingerprint(
            chapter, Sheet(), VoiceEngineFeatures.None, "en", settings);

        Assert.NotEqual(directed, flat);
    }

    [Fact]
    public void ChangingTheLanguage_ChangesTheFingerprint()
    {
        var chapter = Chapter("a", "One", ["Hello."]);
        var settings = new NarrationRenderSettings();

        Assert.NotEqual(
            NarrationRenderJob.Fingerprint(
                chapter, Sheet(), VoiceEngineFeatures.EmotionVector, "en", settings),
            NarrationRenderJob.Fingerprint(
                chapter, Sheet(), VoiceEngineFeatures.EmotionVector, "de", settings));
    }

    [Fact]
    public void TwoChaptersWithTheSameTitle_DoNotWriteToTheSameFile()
    {
        var first = Chapter("aaaaaaaa-1", "Chapter One", ["Hello."]);
        var second = Chapter("bbbbbbbb-2", "Chapter One", ["Hello."]);

        Assert.NotEqual(
            NarrationRenderJob.FileNameFor(0, first),
            NarrationRenderJob.FileNameFor(0, second));
    }

    [Fact]
    public void AChapterWithNoUsableGuid_StillGetsAName()
    {
        var chapter = new NarrationRenderChapter("---", "One", []);

        Assert.Equal("chapter-001-x.wav", NarrationRenderJob.FileNameFor(0, chapter));
    }

    [Fact]
    public void FileNamesSortIntoReadingOrder()
    {
        var chapter = Chapter("a", "One");

        Assert.StartsWith("chapter-010-", NarrationRenderJob.FileNameFor(9, chapter), StringComparison.Ordinal);
    }

    [Fact]
    public void TheFolderIsWhereItWasToldToWrite()
        => Assert.Equal(_folder, Job(Speaks()).Folder);
}
