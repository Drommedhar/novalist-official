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
public sealed class NarrationRenderJob
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
    /// <param name="sheet">Who is read in which voice.</param>
    /// <param name="voices">Reference audio per voice id.</param>
    /// <param name="features">What the engine can be told.</param>
    /// <param name="language">The book's language, BCP-47.</param>
    /// <param name="clips">Emotion-reference clips by name, for the lines that
    /// point at one.</param>
    public async Task<NarrationRenderOutcome> RunAsync(
        IReadOnlyList<NarrationRenderChapter> chapters,
        VoiceCastSheet sheet,
        IReadOnlyDictionary<string, byte[]> voices,
        VoiceEngineFeatures features,
        string language,
        NarrationRenderSettings? settings = null,
        IProgress<NarrationRenderProgress>? progress = null,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, byte[]>? clips = null)
    {
        settings ??= new NarrationRenderSettings();
        Directory.CreateDirectory(_folder);

        var manifest = ReadManifest();
        var done = new List<NarrationChapterAudio>();
        var started = _clock();
        var totalSegments = chapters.Sum(c => c.Segments.Count);
        var segmentsDone = 0;
        var audioMs = 0d;
        string? error = null;

        for (var index = 0; index < chapters.Count; index++)
        {
            var chapter = chapters[index];
            if (cancellationToken.IsCancellationRequested)
                break;

            var stamp = Fingerprint(chapter, sheet, features, language, settings);
            var name = FileNameFor(index, chapter);

            // Already rendered, from exactly these words. The expensive branch
            // is the one not taken.
            if (manifest.TryGetValue(chapter.Guid, out var was)
                && was.Stamp == stamp
                && File.Exists(Path.Combine(_folder, was.File)))
            {
                done.Add(new NarrationChapterAudio(
                    chapter.Guid, chapter.Title, was.File, was.DurationMs, was.Missing, true));
                segmentsDone += chapter.Segments.Count;
                audioMs += was.DurationMs;
                progress?.Report(new NarrationRenderProgress(
                    index + 1, chapters.Count, chapter.Title,
                    segmentsDone, totalSegments, audioMs, _clock() - started));
                continue;
            }

            progress?.Report(new NarrationRenderProgress(
                index + 1, chapters.Count, chapter.Title,
                segmentsDone, totalSegments, audioMs, _clock() - started));

            NarrationChapterAudio rendered;
            try
            {
                rendered = await RenderChapterAsync(
                    chapter, name, sheet, voices, features, language, settings, clips,
                    spoken =>
                    {
                        segmentsDone++;
                        audioMs += spoken;
                        progress?.Report(new NarrationRenderProgress(
                            index + 1, chapters.Count, chapter.Title,
                            segmentsDone, totalSegments, audioMs, _clock() - started));
                    },
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
            audioMs,
            _clock() - started,
            error);
    }

    /// <summary>Which chapters are on disk already, by guid, so an estimate can
    /// say how much of the work is actually left.</summary>
    public IReadOnlyDictionary<string, double> Rendered() => RenderedIn(_folder);

    /// <summary>
    /// The same, for a caller that only wants to look.
    ///
    /// Static because reading the manifest needs no engine, and a caller forced
    /// to construct a job just to ask would have to invent one.
    /// </summary>
    public static IReadOnlyDictionary<string, double> RenderedIn(string folder)
        => ReadManifest(folder)
            .ToDictionary(p => p.Key, p => p.Value.DurationMs, StringComparer.Ordinal);

    /// <summary>Forgets every rendered chapter, so the next run does all of
    /// them. What "render again from scratch" means.</summary>
    public void Reset()
    {
        if (!Directory.Exists(_folder))
            return;
        foreach (var file in Directory.EnumerateFiles(_folder))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private async Task<NarrationChapterAudio> RenderChapterAsync(
        NarrationRenderChapter chapter,
        string name,
        VoiceCastSheet sheet,
        IReadOnlyDictionary<string, byte[]> voices,
        VoiceEngineFeatures features,
        string language,
        NarrationRenderSettings settings,
        IReadOnlyDictionary<string, byte[]>? clips,
        Action<double> spoke,
        CancellationToken cancellationToken)
    {
        var samples = new MemoryStream();
        WaveFormat? format = null;
        var missing = 0;
        // The silence owed before the next clip. Held rather than written,
        // because the format is not known until the first clip arrives - and
        // because a scene break followed by a line break would otherwise write
        // both, making the pause between scenes longer than the setting says.
        var gapMs = 0;

        foreach (var scene in chapter.Scenes)
        {
            if (samples.Length > 0)
                gapMs = settings.SceneGapMs;

            for (var at = 0; at < scene.Segments.Count; at += settings.Window)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var window = scene.Segments.Skip(at).Take(settings.Window).ToArray();
                // Consecutive sentences one voice says in one breath, read in
                // one breath. A recording is listened to end to end, and a model
                // that starts each call afresh resets its pitch and pace at
                // every full stop otherwise.
                var joined = NarrationRender.Joined(window, settings.JoinCharacters);
                var covers = joined.ToDictionary(
                    j => j.Segment.Key, j => j.Covers, StringComparer.Ordinal);

                var request = NarrationRender.Build(
                    [.. joined.Select(j => j.Segment)], sheet, voices, features, language,
                    settings.Rate, clips, _ => scene.Where);

                // Segments the cast could not place - no voice, or a voice this
                // machine does not have - never reach the engine. Counted in
                // lines rather than in calls, so the total the writer sees is
                // the whole chapter however the lines were grouped.
                var asked = request.Segments.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
                foreach (var join in joined.Where(j => !asked.Contains(j.Segment.Key)))
                {
                    missing += join.Covers;
                    for (var i = 0; i < join.Covers; i++)
                        spoke(0);
                }
                if (request.Segments.Count == 0)
                    continue;

                var heard = 0;
                await foreach (var clip in _render(request, cancellationToken)
                    .WithCancellation(cancellationToken))
                {
                    heard++;
                    var stands = covers.GetValueOrDefault(clip.Key, 1);
                    var read = clip.Error == null ? WaveAudio.Read(clip.Audio) : null;
                    if (read == null || read.Samples.Length == 0)
                    {
                        missing += stands;
                        for (var i = 0; i < stands; i++)
                            spoke(0);
                        continue;
                    }

                    format ??= read.Format;
                    // A clip of another shape cannot be laid beside the rest
                    // without resampling, and a chapter that changes pitch
                    // halfway through is worse than one with a gap in it.
                    if (read.Format != format)
                    {
                        missing += stands;
                        for (var i = 0; i < stands; i++)
                            spoke(0);
                        continue;
                    }

                    if (samples.Length > 0)
                        samples.Write(WaveAudio.Silence(read.Format, gapMs));
                    samples.Write(
                        features.HasFlag(VoiceEngineFeatures.ContinuousContext)
                            ? read.Samples
                            : WaveAudio.Fade(read.Samples, read.Format, settings.JoinFadeMs));
                    gapMs = settings.SegmentGapMs;
                    // Once per line the call covered, not once per call. The
                    // bar counts lines, and a joined run that moved it by one
                    // would leave it short of the end by however many sentences
                    // were read in one breath.
                    for (var i = 0; i < stands; i++)
                        spoke(read.DurationMs / stands);
                }

                // Fewer clips came back than calls went out. Counted rather than
                // assumed: an engine that quietly drops a line is a failure only
                // ever noticed while listening.
                for (var i = heard; i < request.Segments.Count; i++)
                {
                    var stands = covers.GetValueOrDefault(request.Segments[i].Key, 1);
                    missing += stands;
                    for (var n = 0; n < stands; n++)
                        spoke(0);
                }
            }
        }

        var shape = format ?? new WaveFormat(24000, 1, 16);
        var bytes = samples.ToArray();
        await File.WriteAllBytesAsync(
            Path.Combine(_folder, name), WaveAudio.Write(shape, bytes), cancellationToken);

        return new NarrationChapterAudio(
            chapter.Guid, chapter.Title, name, shape.DurationMs(bytes.LongLength), missing, false);
    }

    /// <summary>
    /// Everything that would change the audio, as one string.
    ///
    /// The words, who says them, how, at what pace, and what the engine can be
    /// told - a cast change over unchanged prose is still a different reading.
    /// What is deliberately absent is anything positional: inserting a chapter
    /// must not invalidate the ones after it.
    /// </summary>
    internal static string Fingerprint(
        NarrationRenderChapter chapter,
        VoiceCastSheet sheet,
        VoiceEngineFeatures features,
        string language,
        NarrationRenderSettings settings)
    {
        var builder = new StringBuilder();
        builder.Append(language).Append(Unit)
            .Append(settings.Rate.ToString("F3", CultureInfo.InvariantCulture)).Append(Unit)
            .Append(settings.SegmentGapMs).Append(Unit)
            .Append(settings.SceneGapMs).Append(Unit)
            // The fade is audible at every join of the chapter, so changing
            // it changes the audio.
            .Append(settings.JoinFadeMs).Append(Unit)
            .Append(settings.JoinCharacters).Append(Unit)
            .Append((int)features).Append('\n');

        // By scene rather than over the flattened chapter, because which voice
        // a line resolves to now depends on where the line is.
        foreach (var scene in chapter.Scenes)
        {
            foreach (var segment in scene.Segments)
            {
                builder.Append(segment.Key).Append(Unit)
                    .Append(VoiceCast.Resolve(sheet, segment.SpeakerId, scene.Where) ?? "-")
                    .Append(Unit)
                    .Append(segment.Direction.Key).Append(Unit)
                    .Append(segment.Direction.ReferenceClip ?? "-").Append(Unit);
                // The line's own direction with the speaker's standing register
                // already added, which is what will actually be performed.
                // Hashing the line's own numbers instead meant a writer who made
                // a character warmer or more clipped and re-rendered was told
                // every chapter was unchanged, and heard the old delivery back.
                foreach (var (dimension, value) in EmotionDirector
                    .WithRegister(segment.Direction.Vector, sheet.RegisterFor(segment.SpeakerId))
                    .OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    builder.Append(dimension).Append('=')
                        .Append(value.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
                }
                builder.Append(Unit).Append(segment.Text).Append('\n');
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))[..32];
    }

    /// <summary>
    /// A chapter's file name: its position, so a directory listing is in reading
    /// order, and part of its guid, so two chapters sharing a title do not write
    /// to the same file.
    /// </summary>
    internal static string FileNameFor(int index, NarrationRenderChapter chapter)
    {
        var id = new string([.. chapter.Guid.Where(char.IsLetterOrDigit).Take(8)]);
        return $"chapter-{index + 1:D3}-{(id.Length == 0 ? "x" : id)}.wav";
    }

    private Dictionary<string, ManifestEntry> ReadManifest() => ReadManifest(_folder);

    private static Dictionary<string, ManifestEntry> ReadManifest(string folder)
    {
        var path = Path.Combine(folder, ManifestName);
        if (!File.Exists(path))
            return new Dictionary<string, ManifestEntry>(StringComparer.Ordinal);
        try
        {
            var read = JsonSerializer.Deserialize<Dictionary<string, ManifestEntry>>(
                File.ReadAllText(path));
            return read == null
                ? new Dictionary<string, ManifestEntry>(StringComparer.Ordinal)
                : new Dictionary<string, ManifestEntry>(read, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A manifest that cannot be read means rendering again, which is
            // slow but correct. Refusing to render at all would not be.
            return new Dictionary<string, ManifestEntry>(StringComparer.Ordinal);
        }
    }

    private void WriteManifest(Dictionary<string, ManifestEntry> manifest)
    {
        try
        {
            File.WriteAllText(
                Path.Combine(_folder, ManifestName), JsonSerializer.Serialize(manifest, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>One chapter's line in the manifest.</summary>
    internal sealed record ManifestEntry(string Stamp, string File, double DurationMs, int Missing);
}
