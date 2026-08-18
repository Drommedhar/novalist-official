using System.Globalization;
using System.Text.Json;

namespace Novalist.Core.Services;

/// <summary>
/// What rendering a book would cost, before it starts.
/// </summary>
/// <param name="Chapters">Chapters in the selection.</param>
/// <param name="ChaptersToRender">How many of them are not already rendered.
/// The number that decides whether this is a coffee or a night.</param>
/// <param name="Scenes">Scenes in the selection.</param>
/// <param name="Segments">Lines and narration runs the engine will be asked for.</param>
/// <param name="Words">Words in the selection.</param>
/// <param name="AudioMs">How long the finished reading will be.</param>
/// <param name="WallClockMs">How long the machine will take, or null when this
/// machine has never rendered anything and there is nothing to base it on.</param>
/// <param name="Measured">True when the wall clock comes from this machine's own
/// past renders rather than from an assumption.</param>
public sealed record NarrationEstimate(
    int Chapters,
    int ChaptersToRender,
    int Scenes,
    int Segments,
    int Words,
    double AudioMs,
    double? WallClockMs,
    bool Measured);

/// <summary>
/// How fast this machine speaks, remembered between runs.
///
/// The only honest source for "how long will this take" is what this machine
/// did last time. The spread between a laptop on its processor and a desktop
/// with a graphics card is two orders of magnitude, so a figure from anywhere
/// else - a benchmark, an average, our own hardware - would be a number that
/// looks authoritative and is wrong by a factor of a hundred.
///
/// Kept as a ratio of work to audio: 20 means twenty seconds of computing per
/// second of speech. Averaged over the last few renders rather than only the
/// most recent, so one chapter that happened to load a model does not become
/// the estimate for the whole book.
/// </summary>
public sealed class NarrationSpeedLog
{
    /// <summary>The file, in the settings folder beside the clip cache.</summary>
    public const string FileName = "narration-speed.json";

    /// <summary>How many past renders are averaged.</summary>
    private const int Remembered = 5;

    /// <summary>
    /// Renders shorter than this are not recorded at all.
    ///
    /// A two-line audition is nearly all model loading and says nothing about
    /// how fast a chapter goes.
    /// </summary>
    private const double ShortestWorthKeepingMs = 5000;

    private readonly string _path;

    public NarrationSpeedLog(string? settingsDirectory = null)
    {
        var root = string.IsNullOrWhiteSpace(settingsDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Novalist")
            : settingsDirectory;
        _path = Path.Combine(root, FileName);
    }

    /// <summary>Seconds of work per second of audio, or null when this machine
    /// has never finished a render.</summary>
    public double? Factor()
    {
        var samples = Read();
        return samples.Count == 0 ? null : samples.Average();
    }

    /// <summary>Records what a render actually cost.</summary>
    public void Record(double audioMs, double elapsedMs)
    {
        if (audioMs < ShortestWorthKeepingMs || elapsedMs <= 0)
            return;

        var samples = Read();
        samples.Add(elapsedMs / audioMs);
        if (samples.Count > Remembered)
            samples.RemoveRange(0, samples.Count - Remembered);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(samples));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An estimate is a convenience. Failing to write one down must not
            // fail the render that produced it.
        }
    }

    private List<double> Read()
    {
        try
        {
            if (!File.Exists(_path))
                return [];
            var read = JsonSerializer.Deserialize<List<double>>(File.ReadAllText(_path));
            return read?.Where(v => v > 0 && double.IsFinite(v)).ToList() ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}

/// <summary>
/// Works out what a render will cost, from the book and from this machine.
/// </summary>
public static class NarrationEstimator
{
    /// <summary>
    /// Words a minute, for turning a word count into a duration.
    ///
    /// The audiobook industry's own figure - publishers commission at 150 to 160
    /// words a minute and a finished hour is reckoned at 9,000 words. It is used
    /// only for the estimate; the real duration comes from the audio.
    /// </summary>
    public const double WordsPerMinute = 155;

    /// <summary>
    /// Estimates a render.
    /// </summary>
    /// <param name="chapters">The selection, in reading order.</param>
    /// <param name="alreadyRendered">Chapter guids the render folder already
    /// holds audio for, so the wall clock covers the work that is left rather
    /// than the work in total.</param>
    /// <param name="factor">Seconds of work per second of audio on this machine,
    /// from <see cref="NarrationSpeedLog"/>. Null when unknown.</param>
    public static NarrationEstimate Estimate(
        IReadOnlyList<NarrationRenderChapter> chapters,
        IReadOnlyCollection<string>? alreadyRendered = null,
        double? factor = null)
    {
        var rendered = alreadyRendered == null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(alreadyRendered, StringComparer.Ordinal);

        var scenes = 0;
        var segments = 0;
        var words = 0;
        var wordsLeft = 0;
        var toRender = 0;

        foreach (var chapter in chapters)
        {
            var outstanding = !rendered.Contains(chapter.Guid);
            if (outstanding)
                toRender++;

            scenes += chapter.Scenes.Count;
            foreach (var segment in chapter.Segments)
            {
                segments++;
                var count = Words(segment.Text);
                words += count;
                if (outstanding)
                    wordsLeft += count;
            }
        }

        var audioMs = words / WordsPerMinute * 60_000;
        var leftMs = wordsLeft / WordsPerMinute * 60_000;

        return new NarrationEstimate(
            chapters.Count,
            toRender,
            scenes,
            segments,
            words,
            audioMs,
            factor is { } known ? leftMs * known : null,
            factor.HasValue);
    }

    /// <summary>Words in a stretch of prose. Whitespace-separated, which is what
    /// a words-a-minute figure counts.</summary>
    internal static int Words(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>A duration as a person reads it - "9 h 24 min", "3 min 10 s".
    /// Formatted here rather than in the interface because the estimate and the
    /// progress have to agree on how long an hour is.</summary>
    public static string Duration(double milliseconds)
    {
        var span = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        if (span.TotalHours >= 1)
        {
            return string.Create(
                CultureInfo.InvariantCulture, $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}");
        }
        return string.Create(CultureInfo.InvariantCulture, $"{span.Minutes}:{span.Seconds:D2}");
    }
}
