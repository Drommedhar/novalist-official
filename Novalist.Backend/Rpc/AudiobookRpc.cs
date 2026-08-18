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
public sealed class AudiobookRpc : IDisposable
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
        var engine = Ready();

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

        var engine = Ready();
        var folder = RenderRoot();
        if (engine == null || folder == null)
        {
            _state = _state with
            {
                Phase = "failed",
                Error = engine == null ? "no-engine" : "no-project"
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

        _ = Task.Run(
            () => RunAsync(
                job, chapters, sheet, audio, references, engine, wanted, outputPath, rate,
                cancellation),
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

    private async Task RunAsync(
        NarrationRenderJob job,
        IReadOnlyList<NarrationRenderChapter> chapters,
        VoiceCastSheet sheet,
        IReadOnlyDictionary<string, byte[]> voices,
        IReadOnlyDictionary<string, byte[]> references,
        IVoiceEngineContributor engine,
        AudiobookFormat format,
        string outputPath,
        double rate,
        CancellationTokenSource cancellation)
    {
        try
        {
            var outcome = await job.RunAsync(
                chapters,
                sheet,
                voices,
                engine.Features,
                WritingLanguage(),
                new NarrationRenderSettings { Rate = rate },
                new Inline(report =>
                {
                    lock (_gate)
                    {
                        _state = _state with
                        {
                            ChapterIndex = report.ChapterIndex,
                            ChapterCount = report.ChapterCount,
                            ChapterTitle = report.ChapterTitle,
                            SegmentsDone = report.SegmentsDone,
                            SegmentsTotal = report.SegmentsTotal,
                            AudioMs = report.AudioMs,
                            ElapsedMs = report.ElapsedMs
                        };
                    }
                }),
                cancellation.Token,
                references);

            // Only a finished render says anything about how fast this machine
            // is. A stopped one is a partial measurement of an unknown fraction.
            if (outcome.Completed)
                _speed.Record(outcome.AudioMs, outcome.ElapsedMs);

            lock (_gate)
            {
                _state = _state with
                {
                    Phase = "packaging",
                    AudioMs = outcome.AudioMs,
                    ElapsedMs = outcome.ElapsedMs,
                    Missing = outcome.Chapters.Sum(c => c.Missing)
                };
            }

            var result = await _packager.PackageAsync(
                job.Folder, outcome.Chapters, format, outputPath, MetadataFor(), CancellationToken.None);

            lock (_gate)
            {
                _state = _state with
                {
                    Phase = outcome.Completed ? "done" : "stopped",
                    Files = [.. result.Files],
                    DeliveredFormat = result.Format.ToString(),
                    Note = result.Note,
                    Error = outcome.Error
                };
            }

            Log.Info(
                $"audiobook rendered chapters={outcome.Chapters.Count} " +
                $"reused={outcome.Chapters.Count(c => c.Reused)} " +
                $"missing={outcome.Chapters.Sum(c => c.Missing)} " +
                $"completed={outcome.Completed} delivered={result.Format} note={result.Note ?? "-"}.");
        }
        catch (Exception ex)
        {
            // By type. An engine's message can carry the line it choked on.
            Log.Warn($"audiobook failed type={ex.GetType().Name}.");
            lock (_gate)
            {
                _state = _state with { Phase = "failed", Error = ex.GetType().Name };
            }
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_running, cancellation))
                    _running = null;
            }
            cancellation.Dispose();
        }
    }

    /// <summary>
    /// The book as the render job wants it: the export's own compile, with each
    /// scene's cast and directions put back.
    /// </summary>
    private async Task<IReadOnlyList<NarrationRenderChapter>> ChaptersAsync(string[]? selected)
    {
        var projects = _workspace.Projects;
        var book = projects.ActiveBook;
        if (book == null)
            return [];

        var service = new ExportService(projects, _entities);
        var options = new ExportOptions
        {
            Format = ExportFormat.Markdown,
            Title = book.Name,
            SelectedChapterGuids = selected is { Length: > 0 }
                ? [.. selected]
                : [.. book.Chapters.Select(c => c.Guid)],
            Language = ExportService.NormalizeLanguageTag(
                _workspace.Settings.Effective.AutoReplacementLanguage),
            CustomPresets = [.. book.ExportPresets ?? []]
        };
        var compiled = await service.CompileChaptersAsync(options);

        var characters = await _entities.LoadCharactersAsync();
        var lexicon = SceneAnalysisLexicon.For(WritingLanguage());
        var candidates = DialogueAttributor.BuildCandidates(
            characters, lexicon?.WordBoundaries ?? true);
        var dialogueLanguage = DialogueAttributor.BuildLanguage(lexicon);
        var directionLanguage = EmotionDirector.BuildLanguage(lexicon);
        var manifest = projects.ScenesManifest;

        // The pages around the story, spoken like everything else. A recorded
        // book reads its dedication; leaving them out would make the audiobook
        // the one edition that quietly drops what the writer wrote to open it.
        var chapters = new List<NarrationRenderChapter>(compiled.Count + options.Matter.Count);
        chapters.AddRange(Matter(
            options, "Front", candidates, dialogueLanguage, directionLanguage));

        foreach (var chapter in compiled)
        {
            var scenes = new List<NarrationRenderScene>(chapter.Scenes.Count);
            foreach (var scene in chapter.Scenes)
            {
                // The overrides live on the project's scene, and the compiled
                // one is a copy with the replacements already run. Matching by
                // id is what keeps a hand-cast line cast after a compile.
                var source = manifest?.Chapters.GetValueOrDefault(chapter.Guid)
                    ?.FirstOrDefault(s => s.Id == scene.Id);

                scenes.Add(new NarrationRenderScene(
                    scene.Id,
                    NarrationScript.Build(
                        scene.HtmlContent,
                        candidates,
                        dialogueLanguage,
                        directionLanguage,
                        source?.DialogueSpeakers,
                        source?.DialogueDirections,
                        source?.AnalysisOverrides?.Emotion,
                        source?.AnalysisOverrides?.Intensity)));
            }

            chapters.Add(new NarrationRenderChapter(
                string.IsNullOrEmpty(chapter.Guid) ? chapter.Title : chapter.Guid,
                chapter.Heading.Length > 0 ? chapter.Heading : chapter.Title,
                scenes));
        }

        chapters.AddRange(Matter(
            options, "Back", candidates, dialogueLanguage, directionLanguage));
        return chapters;
    }

    /// <summary>
    /// The front or back matter, each page as a chapter of its own.
    ///
    /// Its own chapter rather than folded into the first or last, so a player's
    /// chapter list names it and a listener can skip a copyright page the way
    /// they skip anything else. Read by the narrator: a matter page has no
    /// dialogue to attribute and no scene to take an emotion from.
    /// </summary>
    private static IEnumerable<NarrationRenderChapter> Matter(
        ExportOptions options,
        string placement,
        IReadOnlyList<DialogueSpeakerCandidate> candidates,
        DialogueLanguage dialogueLanguage,
        DirectionLanguage directionLanguage)
    {
        foreach (var page in options.Matter
            .Where(m => string.Equals(m.Placement, placement, StringComparison.Ordinal))
            .OrderBy(m => m.Order))
        {
            var segments = NarrationScript.Build(
                page.HtmlContent, candidates, dialogueLanguage, directionLanguage,
                null, null, null, null);
            if (segments.Count == 0)
                continue;

            yield return new NarrationRenderChapter(
                $"matter:{page.Id}",
                page.Title.Length > 0 ? page.Title : page.Kind,
                [new NarrationRenderScene(page.Id, segments)]);
        }
    }

    private AudiobookMetadata MetadataFor()
    {
        var book = _workspace.Projects.ActiveBook;
        return new AudiobookMetadata
        {
            Title = book?.Name ?? string.Empty,
            Author = book?.Author ?? string.Empty,
            Description = book?.Premise?.Logline ?? string.Empty,
            Language = ExportService.NormalizeLanguageTag(
                _workspace.Settings.Effective.AutoReplacementLanguage),
            Year = DateTime.Now.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CoverPath = _workspace.ActiveCoverAbsolutePath() ?? string.Empty
        };
    }

    /// <summary>Where chapter audio goes, or null with no project open.</summary>
    private string? RenderRoot()
    {
        var root = _workspace.Projects.ProjectRoot;
        return root == null
            ? null
            : Path.Combine(root, ".novalist", "narration", RenderFolder);
    }

    /// <summary>The active engine, if one is installed and ready to speak.</summary>
    private IVoiceEngineContributor? Ready()
    {
        foreach (var engine in _workspace.ExtensionsHost.VoiceEngines)
        {
            try
            {
                if (engine.GetStatusAsync().GetAwaiter().GetResult().IsReady)
                    return engine;
            }
            catch (Exception ex)
            {
                Log.Warn($"audiobook engine status type={ex.GetType().Name}.");
            }
        }
        return null;
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
