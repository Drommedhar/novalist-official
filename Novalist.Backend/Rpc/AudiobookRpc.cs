using Novalist.Backend.Extensions;
using Novalist.Core.Services;
using Novalist.Sdk.Hooks;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Rendering the book to audio, and packaging what comes out.
///
/// Kept apart from <see cref="VoiceEngineRpc"/> because it is a different shape
/// of work. Playback renders a dozen lines and answers; this renders forty
/// thousand and runs for hours, so it starts a job and answers immediately, and
/// everything else is asked of the job rather than waited for.
///
/// What is rendered comes from the same compile every other export uses, so the
/// chapter selection, the front and back matter and the compile-time
/// replacements are honoured here without being re-implemented - a book whose
/// exported text says one thing and whose audiobook says another would be worse
/// than having no audiobook.
/// </summary>
public sealed partial class AudiobookRpc : IDisposable
{
    /// <summary>Where chapter audio lives, under the project.</summary>
    public const string RenderFolder = "render";

    private readonly Workspace _workspace;
    private readonly EntityService _entities;
    private readonly VoiceCast _cast;
    private readonly VoiceStore _voices;
    private readonly NarrationSpeedLog _speed;
    private readonly AudiobookPackager _packager;

    private readonly object _gate = new();
    private CancellationTokenSource? _running;
    private AudiobookJobState _state = AudiobookJobState.Idle;

    public AudiobookRpc(
        Workspace workspace, AudiobookPackager? packager = null, NarrationSpeedLog? speed = null)
    {
        _workspace = workspace;
        _entities = new EntityService(workspace.Projects);
        _cast = new VoiceCast(workspace.Projects, workspace.FileService);
        _voices = new VoiceStore(workspace.Projects, workspace.FileService);
        _speed = speed ?? new NarrationSpeedLog(workspace.SettingsDirectory);
        _packager = packager ?? new AudiobookPackager();
    }

    /// <summary>
    /// What rendering the selection would cost, before anything starts.
    ///
    /// The wall clock is the figure that matters and the one we can least afford
    /// to invent: it comes from what this machine did last time, and is left out
    /// entirely when this machine has never finished a render.
    /// </summary>
    [JsonRpcMethod("audiobook/estimate")]
    public async Task<AudiobookEstimateDto> EstimateAsync(string[]? selectedChapterGuids = null)
    {
        var chapters = await ChaptersAsync(selectedChapterGuids);
        var folder = RenderRoot();
        var already = folder == null
            ? []
            : NarrationRenderJob.RenderedIn(folder).Keys.ToArray();

        var estimate = NarrationEstimator.Estimate(chapters, already, _speed.Factor());
        var engine = await ReadyAsync();

        Log.Info(
            $"audiobook/estimate chapters={estimate.Chapters} left={estimate.ChaptersToRender} " +
            $"segments={estimate.Segments} words={estimate.Words} measured={estimate.Measured}.");

        return new AudiobookEstimateDto(
            estimate.Chapters,
            estimate.ChaptersToRender,
            estimate.Scenes,
            estimate.Segments,
            estimate.Words,
            estimate.AudioMs,
            estimate.WallClockMs,
            estimate.Measured,
            engine?.EngineId,
            engine?.EngineName);
    }

    /// <summary>
    /// Starts rendering, and returns at once.
    ///
    /// Deliberately not awaited: a render is measured in hours, and a call that
    /// did not come back until it finished would hold every other screen behind
    /// it for the whole night.
    /// </summary>
    /// <param name="format">One of <see cref="AudiobookFormat"/>.</param>
    /// <param name="outputPath">The file for M4B; the folder to fill for the
    /// per-chapter formats.</param>
    [JsonRpcMethod("audiobook/start")]
    public async Task<AudiobookStatusDto> StartAsync(
        string format,
        string outputPath,
        string[]? selectedChapterGuids = null,
        double rate = 1.0,
        bool fromScratch = false)
    {
        lock (_gate)
        {
            if (_running != null)
                return Status();
        }

        // The project first. Which engine to use is decided by what the book is
        // cast in, so with no book open there is no engine either - and
        // answering "no engine" to somebody who simply has no project open
        // sends them looking for a speech extension they already have.
        var folder = RenderRoot();
        var engine = folder == null ? null : await ReadyAsync();
        if (engine == null || folder == null)
        {
            _state = _state with
            {
                Phase = "failed",
                Error = folder == null ? "no-project" : "no-engine"
            };
            Log.Warn($"audiobook/start refused reason={_state.Error}.");
            return Status();
        }

        var wanted = Enum.TryParse<AudiobookFormat>(format, out var parsed)
            ? parsed
            : AudiobookFormat.M4b;
        var chapters = await ChaptersAsync(selectedChapterGuids);
        var sheet = await _cast.ReadAsync();
        var audio = await _voices.ReadAudioForAsync(
            [.. chapters.SelectMany(c => NarrationRender.VoicesNeeded(c.Segments, sheet))
                .Distinct(StringComparer.Ordinal)]);
        var referenceTexts = await _voices.ReadReferenceTextsForAsync(audio.Keys);
        // Clips lines were told to sound like, read once for the whole book
        // rather than per window - a nine-hour render must not go back to disk
        // for the same reference forty thousand times.
        var references = await new NarrationClipCache(_workspace.SettingsDirectory).ReadManyAsync(
            [.. chapters.SelectMany(c => NarrationRender.ClipsNeeded(c.Segments))
                .Distinct(StringComparer.Ordinal)]);

        var cancellation = new CancellationTokenSource();
        lock (_gate)
        {
            _running = cancellation;
            _state = new AudiobookJobState
            {
                Phase = "rendering",
                ChapterCount = chapters.Count,
                SegmentsTotal = chapters.Sum(c => c.Segments.Count)
            };
        }

        var job = new NarrationRenderJob(
            folder, (request, token) => engine.RenderAsync(request, token));
        if (fromScratch)
            job.Reset();

        var language = WritingLanguage();
        var metadata = MetadataFor();
        _ = Task.Run(
            () => RunAsync(
                job, chapters, sheet, audio, referenceTexts, references, engine, wanted, outputPath, rate,
                language, metadata, cancellation),
            CancellationToken.None);

        Log.Info(
            $"audiobook/start chapters={chapters.Count} format={wanted} fromScratch={fromScratch}.");
        return Status();
    }

    /// <summary>How the render is going. Polled, because a job that outlives
    /// the request that started it has nowhere to push to.</summary>
    [JsonRpcMethod("audiobook/status")]
    public AudiobookStatusDto Status()
    {
        lock (_gate)
        {
            return new AudiobookStatusDto(
                _state.Phase,
                _state.ChapterIndex,
                _state.ChapterCount,
                _state.ChapterTitle,
                _state.SegmentsDone,
                _state.SegmentsTotal,
                _state.AudioMs,
                _state.ElapsedMs,
                _state.Missing,
                _state.Files,
                _state.DeliveredFormat,
                _state.Note,
                _state.Error);
        }
    }

    /// <summary>
    /// Stops a render, keeping every chapter that finished.
    ///
    /// Skips the request queue: queued behind the render it is meant to
    /// interrupt, it could not arrive until that render had finished.
    /// </summary>
    [JsonRpcMethod("audiobook/stop")]
    public bool Stop()
    {
        lock (_gate)
        {
            _running?.Cancel();
        }
        Log.Info("audiobook/stop.");
        return true;
    }

    /// <summary>
    /// The active engine, if one is installed and ready to speak - starting one
    /// that is installed but not loaded.
    ///
    /// An export is minutes of rendering the writer asked for by name. Refusing
    /// it because the model had not been loaded yet, when loading it is half a
    /// minute and needs no download, would be an error message in place of the
    /// thing they asked for.
    ///
    /// An engine with a download still outstanding is left alone. That is a
    /// decision about gigabytes and somebody's connection, and it belongs to the
    /// writer rather than to an export button.
    /// </summary>
    private async Task<IVoiceEngineContributor?> ReadyAsync()
    {
        // The engine that made the voices this book is cast in, rather than
        // whichever one answered first. With two engines installed the export
        // went to whoever had finished loading - so a book cast in a real
        // speech engine could be recorded, whole, by the example tone
        // generator, which loads instantly and always won.
        var wanted = await CastEngineAsync();
        if (wanted == null)
            return null;

        var engine = _workspace.ExtensionsHost.VoiceEngines.FirstOrDefault(
            e => string.Equals(e.EngineId, wanted, StringComparison.Ordinal));
        if (engine == null)
        {
            Log.Warn("audiobook engine not installed.");
            return null;
        }

        try
        {
            var status = await engine.GetStatusAsync();
            if (status.IsReady)
                return engine;
            // Installed and merely not loaded. An export is minutes of
            // rendering the writer asked for by name, and refusing it over half
            // a minute of loading would be an error message in place of the
            // thing they wanted. Gigabytes still to fetch are a different
            // decision and stay theirs.
            if (status.IsPreparing || status.DownloadBytes is > 0)
                return null;

            Log.Info("audiobook engine start.");
            await engine.PrepareAsync();
            return (await engine.GetStatusAsync()).IsReady ? engine : null;
        }
        catch (Exception ex)
        {
            Log.Warn($"audiobook engine start failed type={ex.GetType().Name}.");
            return null;
        }
    }

    /// <summary>
    /// The engine that made the voices this book is cast in, or null where it is
    /// cast in none.
    ///
    /// The narrator's engine leads, because the narrator speaks most of a novel
    /// and is the voice a book cast half-way through still has. Failing that,
    /// whichever engine made the most of the voices in use. A book cast across
    /// two engines cannot be exported in one pass either way - the job renders
    /// through one engine - so this picks the one that gets most of the book
    /// right and the rest is reported per line rather than spoken wrongly.
    /// </summary>
    private async Task<string?> CastEngineAsync()
    {
        var sheet = await _cast.ReadAsync();
        var voices = await _voices.ListAsync();
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var voice in voices)
        {
            if (!string.IsNullOrEmpty(voice.EngineId))
                owner[voice.VoiceId] = voice.EngineId;
        }

        if (sheet.NarratorVoiceId is { Length: > 0 } narrator
            && owner.TryGetValue(narrator, out var theirs))
        {
            return theirs;
        }

        return VoiceCast.AllVoices(sheet)
            .Select(v => owner.GetValueOrDefault(v))
            .OfType<string>()
            .Where(e => !string.IsNullOrEmpty(e))
            .GroupBy(e => e, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .FirstOrDefault()?.Key;
    }

    private string WritingLanguage()
    {
        var overrides = _workspace.Projects.ProjectRoot == null
            ? null
            : _workspace.Projects.ProjectSettings.Overrides;
        return overrides?.AutoReplacementLanguage
               ?? _workspace.Settings.Settings.AutoReplacementLanguage
               ?? "en";
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _running?.Cancel();
            _running?.Dispose();
            _running = null;
        }
    }

    /// <summary>
    /// Delivers progress on the thread that reported it.
    ///
    /// <see cref="Progress{T}"/> posts to whatever synchronization context it
    /// was built on, and a backend that has none silently never delivers - the
    /// bar stands still for the whole render and every report is lost.
    /// </summary>
    private sealed class Inline(Action<NarrationRenderProgress> report)
        : IProgress<NarrationRenderProgress>
    {
        public void Report(NarrationRenderProgress value) => report(value);
    }

    /// <summary>What the job has got to, held between polls.</summary>
    private sealed record AudiobookJobState
    {
        public static readonly AudiobookJobState Idle = new();

        public string Phase { get; init; } = "idle";
        public int ChapterIndex { get; init; }
        public int ChapterCount { get; init; }
        public string ChapterTitle { get; init; } = string.Empty;
        public int SegmentsDone { get; init; }
        public int SegmentsTotal { get; init; }
        public double AudioMs { get; init; }
        public double ElapsedMs { get; init; }
        public int Missing { get; init; }
        public string[] Files { get; init; } = [];
        public string? DeliveredFormat { get; init; }
        public string? Note { get; init; }
        public string? Error { get; init; }
    }
}

/// <summary>What a render would cost, before it starts.</summary>
/// <param name="WallClockMs">Null when this machine has never finished a render
/// and there is nothing honest to say.</param>
public sealed record AudiobookEstimateDto(
    int Chapters,
    int ChaptersToRender,
    int Scenes,
    int Segments,
    int Words,
    double AudioMs,
    double? WallClockMs,
    bool Measured,
    string? EngineId,
    string? EngineName);

/// <summary>How the render is going, or how it ended.</summary>
/// <param name="Phase">idle, rendering, packaging, done, stopped or failed.</param>
/// <param name="DeliveredFormat">What was actually written, which is not always
/// what was asked for.</param>
/// <param name="Note">Why, when it differs.</param>
public sealed record AudiobookStatusDto(
    string Phase,
    int ChapterIndex,
    int ChapterCount,
    string ChapterTitle,
    int SegmentsDone,
    int SegmentsTotal,
    double AudioMs,
    double ElapsedMs,
    int Missing,
    string[] Files,
    string? DeliveredFormat,
    string? Note,
    string? Error);
