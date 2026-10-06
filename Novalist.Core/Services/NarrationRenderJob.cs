using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Novalist.Core.Utilities;
using Novalist.Sdk.Models.Narration;

namespace Novalist.Core.Services;

/// <summary>One scene of a rendered chapter, already cast and directed.</summary>
/// <param name="Where">Where in the book this scene sits, so a voice the writer
/// set for part of it is resolved rather than the character's standing one.
/// Null where the caller has no position to give - front matter has none.</param>
public sealed record NarrationRenderScene(
    string SceneId,
    IReadOnlyList<NarrationSegment> Segments,
    NarrationPlacement? Where = null);

/// <summary>One chapter of the book, in reading order.</summary>
public sealed record NarrationRenderChapter(
    string Guid, string Title, IReadOnlyList<NarrationRenderScene> Scenes)
{
    /// <summary>Every segment of the chapter, scenes in order.</summary>
    public IReadOnlyList<NarrationSegment> Segments => [.. Scenes.SelectMany(s => s.Segments)];
}

/// <summary>What a chapter came out as.</summary>
/// <param name="File">File name inside the render folder, not a path - the
/// folder is the caller's, and naming it twice is how the two drift apart.</param>
/// <param name="Missing">Segments the engine could not speak. A chapter with
/// gaps in it is still a chapter, but the number has to reach the writer.</param>
/// <param name="Reused">True when this chapter was already rendered and was not
/// spoken again. Shown, because "finished in four seconds" is alarming until it
/// says which chapters it actually did.</param>
public sealed record NarrationChapterAudio(
    string Guid, string Title, string File, double DurationMs, int Missing, bool Reused);

/// <summary>How far a book render has got.</summary>
public sealed record NarrationRenderProgress(
    int ChapterIndex,
    int ChapterCount,
    string ChapterTitle,
    int SegmentsDone,
    int SegmentsTotal,
    double AudioMs,
    double ElapsedMs);

/// <summary>The end of a render, finished or stopped.</summary>
public sealed record NarrationRenderOutcome(
    IReadOnlyList<NarrationChapterAudio> Chapters,
    bool Completed,
    double AudioMs,
    double ElapsedMs,
    string? Error);

/// <summary>Knobs a render has, with the defaults a reading actually wants.</summary>
public sealed class NarrationRenderSettings
{
    /// <summary>Reading pace, 1 being the engine's own.</summary>
    public double Rate { get; init; } = 1.0;

    /// <summary>
    /// Silence between two segments of the same scene.
    ///
    /// Speech engines return each clip trimmed to the words. Laid end to end
    /// with nothing between them, a dialogue tag runs into the next line as one
    /// breathless sentence.
    /// </summary>
    public int SegmentGapMs { get; init; } = 140;

    /// <summary>Silence between scenes. Longer, because a scene break is a
    /// change of place or time and the ear has to be told.</summary>
    public int SceneGapMs { get; init; } = 900;

    /// <summary>Segments sent to the engine at once. Bounded, so stopping takes
    /// effect within a few lines rather than at the end of the chapter.</summary>
    public int Window { get; init; } = 12;

    /// <summary>
    /// How much of each clip's edge is ramped, for engines that render every
    /// line in isolation.
    ///
    /// Short enough to be inaudible as a fade and long enough to remove the
    /// click a hard join makes. Engines that carry prosody across a request are
    /// left alone - they have already made the joins continuous.
    /// </summary>
    public int JoinFadeMs { get; init; } = 15;

    /// <summary>
    /// The longest run of one voice's consecutive sentences to read in a single
    /// breath, in characters. Zero reads every sentence on its own.
    ///
    /// A cloning model starts each call afresh from the reference clip with no
    /// memory of the sentence before it, so pitch, pace and energy reset at
    /// every full stop and a stitched paragraph sounds like four readings rather
    /// than one narrator. A recording is listened to end to end and has nothing
    /// to gain from the fine split the live reading needs.
    ///
    /// Six hundred, measured rather than guessed. Read against the same
    /// sentences spoken separately, a run of six hundred characters comes back
    /// with 98% of the audio; seven hundred with 88%; eight hundred with 83%,
    /// which is prose going missing rather than speech tightening. The model's
    /// documented capacity is minutes of audio, but through a reference clip it
    /// stops delivering well before that, and a line cut off mid-word is silent
    /// - nothing reports it, and it is found by listening.
    /// </summary>
    public int JoinCharacters { get; init; } = 600;
}

/// <summary>
/// Renders a whole book to one audio file per chapter.
///
/// Three properties matter more than the rendering itself, because a book is
/// long enough that all three will be needed:
///
/// <list type="bullet">
/// <item><b>Resumable.</b> A chapter already rendered from the same words, the
/// same cast and the same directions is not rendered again. The manifest keeps
/// a fingerprint per chapter, so editing chapter nine re-renders chapter nine
/// and nothing else - the difference between a five-minute correction and an
/// overnight one.</item>
/// <item><b>Cancellable.</b> Stopping stops within a window rather than at the
/// end of the book, and what was finished stays finished.</item>
/// <item><b>Reportable.</b> Progress is per segment, not per chapter, because a
/// bar that moves once every forty minutes is a bar that has stopped.</item>
/// </list>
///
/// It knows nothing about which engine is speaking - it is handed a delegate.
/// That is what lets the whole job be tested against a fake returning a tenth of
/// a second of tone per line.
/// </summary>
public sealed partial class NarrationRenderJob
{
    /// <summary>What the job needs from an engine, and all it needs.</summary>
    public delegate IAsyncEnumerable<NarrationClip> RenderDelegate(
        NarrationRequest request, CancellationToken cancellationToken);

    /// <summary>The manifest's name inside the render folder.</summary>
    public const string ManifestName = "chapters.json";

    /// <summary>Separates the fields of one segment's fingerprint. A control
    /// character, so no word of prose can forge a field boundary.</summary>
    private const char Unit = '';

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _folder;
    private readonly RenderDelegate _render;
    private readonly Func<double> _clock;

    /// <param name="folder">Where chapter audio and the manifest are written.</param>
    /// <param name="render">The engine, as a function.</param>
    /// <param name="clock">Milliseconds since anything, for the elapsed figures.
    /// Injected so a test can assert on them without waiting.</param>
    public NarrationRenderJob(string folder, RenderDelegate render, Func<double>? clock = null)
    {
        _folder = folder;
        _render = render;
        _clock = clock ?? (() => Environment.TickCount64);
    }

    /// <summary>Where the chapter files are written.</summary>
    public string Folder => _folder;

    /// <summary>
    /// Renders every chapter that is not already rendered.
    /// </summary>
    /// <param name="chapters">The book, compiled and in order.</param>
    /// <param name="context">Cast and engine resources retained for every chapter in this run.</param>
    public async Task<NarrationRenderOutcome> RunAsync(
        IReadOnlyList<NarrationRenderChapter> chapters,
        NarrationRenderContext context,
        NarrationRenderSettings? settings = null,
        IProgress<NarrationRenderProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        settings ??= new NarrationRenderSettings();
        Directory.CreateDirectory(_folder);

        var manifest = ReadManifest();
        var done = new List<NarrationChapterAudio>();
        var tracking = new JobProgress(progress, chapters.Count, chapters.Sum(c => c.Segments.Count), _clock);
        string? error = null;

        for (var index = 0; index < chapters.Count; index++)
        {
            var chapter = chapters[index];
            if (cancellationToken.IsCancellationRequested)
                break;

            var stamp = Fingerprint(chapter, context.Cast, context.Features, context.Language, settings);
            var name = FileNameFor(index, chapter);

            // Already rendered, from exactly these words. The expensive branch
            // is the one not taken.
            if (manifest.TryGetValue(chapter.Guid, out var was)
                && was.Stamp == stamp
                && File.Exists(Path.Combine(_folder, was.File)))
            {
                done.Add(new NarrationChapterAudio(
                    chapter.Guid, chapter.Title, was.File, was.DurationMs, was.Missing, true));
                tracking.Cached(index, chapter.Title, chapter.Segments.Count, was.DurationMs);
                continue;
            }

            tracking.Report(index, chapter.Title);

            NarrationChapterAudio rendered;
            try
            {
                rendered = await RenderChapterAsync(
                    chapter, name, context, settings,
                    spoken => tracking.Spoke(index, chapter.Title, spoken),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Named by type, never by message: an engine's message can carry
                // the line it choked on, and the line is the manuscript.
                error = ex.GetType().Name;
                break;
            }

            done.Add(rendered);
            manifest[chapter.Guid] = new ManifestEntry(
                stamp, rendered.File, rendered.DurationMs, rendered.Missing);
            WriteManifest(manifest);
        }

        return new NarrationRenderOutcome(
            done,
            done.Count == chapters.Count && error == null,
            tracking.AudioMs,
            tracking.Elapsed,
            error);
    }

    private sealed class JobProgress
    {
        private readonly IProgress<NarrationRenderProgress>? progress;
        private readonly int chapters;
        private readonly int totalSegments;
        private readonly Func<double> clock;
        private readonly double started;
        private int segmentsDone;

        public JobProgress(IProgress<NarrationRenderProgress>? progress, int chapters,
            int totalSegments, Func<double> clock)
        {
            this.progress = progress;
            this.chapters = chapters;
            this.totalSegments = totalSegments;
            this.clock = clock;
            started = clock();
        }

        public double AudioMs { get; private set; }
        public double Elapsed => clock() - started;

        public void Cached(int index, string title, int segments, double audioMs)
        {
            segmentsDone += segments;
            AudioMs += audioMs;
            Report(index, title);
        }

        public void Spoke(int index, string title, double audioMs)
        {
            segmentsDone++;
            AudioMs += audioMs;
            Report(index, title);
        }

        public void Report(int index, string title)
            => progress?.Report(new NarrationRenderProgress(
                index + 1, chapters, title, segmentsDone, totalSegments, AudioMs, Elapsed));
    }
}
