using System.Runtime.CompilerServices;
using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using Novalist.Sdk;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models.Narration;
using Novalist.Sdk.Services;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Covers rendering the book to audio: the estimate shown before it starts,
/// the job that runs for hours, stopping it, and what comes out at the end.
///
/// The engine is a stub that returns a tenth of a second of silence per line,
/// so a whole book renders in milliseconds and every assertion is about the
/// host's decisions rather than about anybody's speech model. The encoder is
/// faked too, because the case that matters most is the machine that has none.
/// </summary>
public sealed class AudiobookRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly AudiobookRpc _rpc;
    private readonly SilentEngine _engine = new();
    private readonly FakeEncoder _encoder = new();

    public AudiobookRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-book-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "BookNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _workspace.ExtensionsHost.VoiceEngines.Add(_engine);
        _rpc = new AudiobookRpc(
            _workspace,
            new AudiobookPackager(_encoder),
            new NarrationSpeedLog(Path.Combine(_root, "speed")));
    }

    public void Dispose()
    {
        _rpc.Dispose();
        _workspace.ExtensionsHost.VoiceEngines.Clear();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private string Output => Path.Combine(_root, "out");

    private async Task<ChapterData> ChapterAsync(string title, params string[] scenes)
    {
        var chapter = await _workspace.Projects.CreateChapterAsync(title);
        for (var i = 0; i < scenes.Length; i++)
        {
            var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, $"S{i}");
            await _workspace.Projects.WriteSceneContentAsync(chapter, scene, scenes[i]);
        }
        return chapter;
    }

    /// <summary>Casts the narrator, which is what makes any of the prose speakable.</summary>
    private async Task CastNarratorAsync()
    {
        var store = new VoiceStore(_workspace.Projects, _workspace.FileService);
        await store.SaveAsync(
            new DesignedVoice(
                "narrator", "Narrator", string.Empty, SilentEngine.Id, "wav", 24000,
                DateTime.UtcNow.ToString("O")),
            [1, 2, 3]);

        var cast = new VoiceCast(_workspace.Projects, _workspace.FileService);
        var sheet = await cast.ReadAsync();
        sheet.NarratorVoiceId = "narrator";
        await cast.WriteAsync(sheet);
    }

    /// <summary>Waits for the job to stop running, which is what a poll would do.</summary>
    private async Task<AudiobookStatusDto> SettledAsync()
    {
        for (var i = 0; i < 400; i++)
        {
            var status = _rpc.Status();
            if (status.Phase is not ("rendering" or "packaging"))
                return status;
            await Task.Delay(20);
        }
        return _rpc.Status();
    }

    // ─── the estimate ───────────────────────────────────────────────

    [Fact]
    public async Task Estimate_CountsTheBookAndNamesTheEngine()
    {
        await ChapterAsync("One", "<p>One two three four five.</p>");
        // Cast, because which engine would do the work is decided by the voices
        // the book is cast in rather than by whichever engine is loaded.
        await CastNarratorAsync();
        _engine.Ready = true;

        var estimate = await _rpc.EstimateAsync();

        Assert.Equal(1, estimate.Chapters);
        Assert.Equal(1, estimate.Scenes);
        Assert.Equal(5, estimate.Words);
        Assert.Equal(SilentEngine.Id, estimate.EngineId);
        Assert.Equal("Silent", estimate.EngineName);
    }

    [Fact]
    public async Task Estimate_WithNoRenderBehindIt_DoesNotGuessAWallClock()
    {
        await ChapterAsync("One", "<p>Hello there.</p>");

        var estimate = await _rpc.EstimateAsync();

        Assert.Null(estimate.WallClockMs);
        Assert.False(estimate.Measured);
    }

    [Fact]
    public async Task Estimate_WithNoProjectOpen_IsEmptyRatherThanAFault()
    {
        _workspace.CloseProject();

        var estimate = await _rpc.EstimateAsync();

        Assert.Equal(0, estimate.Chapters);
    }

    [Fact]
    public async Task Estimate_WithNoEngineInstalled_StillCountsTheBook()
    {
        await ChapterAsync("One", "<p>Hello there.</p>");
        _workspace.ExtensionsHost.VoiceEngines.Clear();

        var estimate = await _rpc.EstimateAsync();

        Assert.Equal(1, estimate.Chapters);
        Assert.Null(estimate.EngineId);
    }

    [Fact]
    public async Task Estimate_OnlyTheChaptersAskedForAreCounted()
    {
        var first = await ChapterAsync("One", "<p>One two three.</p>");
        await ChapterAsync("Two", "<p>Four five six.</p>");

        var estimate = await _rpc.EstimateAsync([first.Guid]);

        Assert.Equal(1, estimate.Chapters);
    }

    [Fact]
    public async Task Estimate_AfterARender_KnowsWhatIsLeft()
    {
        await ChapterAsync("One", "<p>Hello there.</p>");
        await CastNarratorAsync();
        _engine.Ready = true;
        await _rpc.StartAsync("WavPerChapter", Output);
        await SettledAsync();

        var estimate = await _rpc.EstimateAsync();

        Assert.Equal(1, estimate.Chapters);
        Assert.Equal(0, estimate.ChaptersToRender);
    }

    // ─── rendering ──────────────────────────────────────────────────

    [Fact]
    public async Task Start_RendersTheBookAndDeliversIt()
    {
        await ChapterAsync("One", "<p>Hello there.</p>");
        await CastNarratorAsync();
        _engine.Ready = true;

        await _rpc.StartAsync("WavPerChapter", Output);
        var status = await SettledAsync();

        Assert.Equal("done", status.Phase);
        Assert.Equal("WavPerChapter", status.DeliveredFormat);
        Assert.NotEmpty(status.Files);
        Assert.Null(status.Error);
    }

    [Fact]
    public async Task Start_AnsweredImmediately_RatherThanWhenTheRenderFinishes()
    {
        await ChapterAsync("One", "<p>Hello there.</p>");
        await CastNarratorAsync();
        _engine.Ready = true;
        _engine.Hold = new TaskCompletionSource();

        var status = await _rpc.StartAsync("WavPerChapter", Output);

        Assert.Equal("rendering", status.Phase);
        _engine.Hold.SetResult();
        await SettledAsync();
    }

    [Fact]
    public async Task Start_WithAnEngineStillToDownload_SaysSoRatherThanFetchingGigabytes()
    {
        await ChapterAsync("One", "<p>Hello there.</p>");
        await CastNarratorAsync();
        // Installed, not ready, and eight gigabytes short of being ready. An
        // export button is not permission to spend somebody's connection.
        _engine.DownloadBytes = 8L * 1024 * 1024 * 1024;

        var status = await _rpc.StartAsync("M4b", Path.Combine(Output, "book.m4b"));

        Assert.Equal("failed", status.Phase);
        Assert.Equal("no-engine", status.Error);
        Assert.Equal(0, _engine.Prepared);
    }

    [Fact]
    public async Task Start_WithAnEngineInstalledButNotLoaded_LoadsItRatherThanRefusing()
    {
        await ChapterAsync("One", "<p>Hello there.</p>");
        await CastNarratorAsync();
        // Everything downloaded; the model simply is not in memory, because
        // nothing has needed it since the app started. Refusing an export the
        // writer asked for by name over half a minute of loading would be an
        // error message in place of the thing they wanted.
        _engine.Ready = false;

        var status = await _rpc.StartAsync("M4b", Path.Combine(Output, "book.m4b"));

        Assert.Equal(1, _engine.Prepared);
        Assert.NotEqual("no-engine", status.Error);
    }

    [Fact]
    public async Task Start_WithNoProjectOpen_SaysSo()
    {
        _engine.Ready = true;
        _workspace.CloseProject();

        var status = await _rpc.StartAsync("M4b", Path.Combine(Output, "book.m4b"));

        Assert.Equal("failed", status.Phase);
        Assert.Equal("no-project", status.Error);
    }

    [Fact]
    public async Task Start_WhileOneIsAlreadyRunning_DoesNotStartASecond()
    {
        await ChapterAsync("One", "<p>Hello there.</p>");
        await CastNarratorAsync();
        _engine.Ready = true;
        _engine.Hold = new TaskCompletionSource();
        _engine.Started = new TaskCompletionSource();
        await _rpc.StartAsync("WavPerChapter", Output);
        await _engine.Started.Task;

        var second = await _rpc.StartAsync("WavPerChapter", Output);

        Assert.Equal("rendering", second.Phase);
        Assert.Equal(1, _engine.Renders);
        _engine.Hold.SetResult();
        await SettledAsync();
    }

    [Fact]
    public async Task Start_AnUnknownFormat_FallsBackToTheOneAnAudiobookIs()
    {
        await ChapterAsync("One", "<p>Hello there.</p>");
        await CastNarratorAsync();
        _engine.Ready = true;

        await _rpc.StartAsync("Vinyl", Path.Combine(Output, "book.m4b"));
        var status = await SettledAsync();

        Assert.Equal("M4b", status.DeliveredFormat);
    }

    [Fact]
    public async Task Start_ASecondRunReusesWhatWasAlreadyRendered()
    {
        await ChapterAsync("One", "<p>Hello there.</p>");
        await CastNarratorAsync();
        _engine.Ready = true;
        await _rpc.StartAsync("WavPerChapter", Output);
        await SettledAsync();
        var afterFirst = _engine.Renders;

        await _rpc.StartAsync("WavPerChapter", Output);
        await SettledAsync();

        Assert.Equal(afterFirst, _engine.Renders);
    }

    [Fact]
    public async Task Start_FromScratch_RendersEveryChapterAgain()
    {
        await ChapterAsync("One", "<p>Hello there.</p>");
        await CastNarratorAsync();
        _engine.Ready = true;
        await _rpc.StartAsync("WavPerChapter", Output);
        await SettledAsync();
        var afterFirst = _engine.Renders;

        await _rpc.StartAsync("WavPerChapter", Output, null, 1.0, fromScratch: true);
        await SettledAsync();

        Assert.True(_engine.Renders > afterFirst);
    }

    [Fact]
    public async Task Start_TheChapterSelectionIsHonoured()
    {
        var first = await ChapterAsync("One", "<p>Hello there.</p>");
        await ChapterAsync("Two", "<p>Goodbye then.</p>");
        await CastNarratorAsync();
        _engine.Ready = true;

        await _rpc.StartAsync("WavPerChapter", Output, [first.Guid]);
        var status = await SettledAsync();

        Assert.Equal(1, status.ChapterCount);
    }

    [Fact]
    public async Task Start_CompileTimeReplacementsReachTheReading()
    {
        // The audiobook and the ebook must say the same words.
        var book = _workspace.Projects.ActiveBook!;
        book.ExportReplacements =
        [
            new ExportReplacement { Id = "r", Find = "Aldric", Replace = "Aldrick", Enabled = true }
        ];
        await ChapterAsync("One", "<p>Aldric turned away.</p>");
        await CastNarratorAsync();
        _engine.Ready = true;

        await _rpc.StartAsync("WavPerChapter", Output);
        await SettledAsync();

        Assert.Contains(
            _engine.Spoken, line => line.Contains("Aldrick", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Start_TheDedicationIsReadToo()
    {
        // A recorded book reads its front matter. Leaving it out would make the
        // audiobook the one edition that quietly drops what the writer wrote to
        // open the book with.
        var book = _workspace.Projects.ActiveBook!;
        book.Matter =
        [
            new BookMatterElement
            {
                Kind = BookMatterKind.Dedication,
                Placement = BookMatterPlacement.Front,
                Content = "<p>For everyone who waited.</p>"
            }
        ];
        await ChapterAsync("One", "<p>Hello there.</p>");
        await CastNarratorAsync();
        _engine.Ready = true;

        await _rpc.StartAsync("WavPerChapter", Output);
        var status = await SettledAsync();

        Assert.Equal("done", status.Phase);
        Assert.Contains(
            _engine.Spoken, line => line.Contains("everyone who waited", StringComparison.Ordinal));
        // Its own chapter, so a player's chapter list names it and it can be
        // skipped the way anything else is.
        Assert.Equal(2, status.ChapterCount);
    }

    [Fact]
    public async Task Start_AMatterPageWithNoProseOnIt_IsNotAChapterOfSilence()
    {
        // A page carrying a device or a picture and no words. There is nothing
        // to read, and rendering it would put a chapter of silence in the
        // player's chapter list.
        var book = _workspace.Projects.ActiveBook!;
        book.Matter =
        [
            new BookMatterElement
            {
                Kind = BookMatterKind.HalfTitle,
                Placement = BookMatterPlacement.Front,
                Content = "<figure><img src=\"device.png\" alt=\"\" /></figure>"
            }
        ];
        await ChapterAsync("One", "<p>Hello there.</p>");
        await CastNarratorAsync();
        _engine.Ready = true;

        await _rpc.StartAsync("WavPerChapter", Output);
        var status = await SettledAsync();

        Assert.Equal(1, status.ChapterCount);
    }

    [Fact]
    public async Task Start_ALineWithNoVoiceIsReportedRatherThanSilentlyDropped()
    {
        var chapter = await ChapterAsync("One", "<p>\"Hello there,\" said Mira.</p>");
        await CastNarratorAsync();
        // Mira is cast in a voice this machine does not have - a cast assembled
        // on somebody else's computer. Her line cannot be spoken; the
        // narrator's half of the paragraph can.
        var mira = new CharacterData { Name = "Mira" };
        await new EntityService(_workspace.Projects).SaveCharacterAsync(mira);
        await new VoiceCast(_workspace.Projects, _workspace.FileService)
            .SetVoiceAsync(mira.Id, "a-voice-from-another-machine");
        _engine.Ready = true;
        _ = chapter;

        await _rpc.StartAsync("WavPerChapter", Output);
        var status = await SettledAsync();

        Assert.True(status.Missing > 0);
    }

    [Fact]
    public async Task Start_AnEngineThatThrows_EndsAsFailedRatherThanHanging()
    {
        await ChapterAsync("One", "<p>Hello there.</p>");
        await CastNarratorAsync();
        _engine.Ready = true;
        _engine.Throw = true;

        await _rpc.StartAsync("WavPerChapter", Output);
        var status = await SettledAsync();

        Assert.Equal("stopped", status.Phase);
        Assert.Equal(nameof(InvalidOperationException), status.Error);
    }

    // ─── stopping ───────────────────────────────────────────────────

    [Fact]
    public async Task Stop_EndsTheRender()
    {
        await ChapterAsync("One", "<p>Hello there.</p>");
        await CastNarratorAsync();
        _engine.Ready = true;
        _engine.Hold = new TaskCompletionSource();
        await _rpc.StartAsync("WavPerChapter", Output);

        Assert.True(_rpc.Stop());
        _engine.Hold.SetResult();
        var status = await SettledAsync();

        Assert.NotEqual("rendering", status.Phase);
    }

    [Fact]
    public void Stop_WithNothingRunning_IsNotAFault()
        => Assert.True(_rpc.Stop());

    [Fact]
    public void Status_BeforeAnythingHasHappened_IsIdle()
        => Assert.Equal("idle", _rpc.Status().Phase);

    [Fact]
    public async Task Disposing_StopsARenderInFlight()
    {
        await ChapterAsync("One", "<p>Hello there.</p>");
        await CastNarratorAsync();
        _engine.Ready = true;
        _engine.Hold = new TaskCompletionSource();
        await _rpc.StartAsync("WavPerChapter", Output);

        _rpc.Dispose();
        _engine.Hold.SetResult();

        Assert.NotNull(_rpc.Status());
    }

    // ─── packaging ──────────────────────────────────────────────────

    [Fact]
    public async Task WithNoEncoder_TheChaptersAreStillDelivered()
    {
        await ChapterAsync("One", "<p>Hello there.</p>");
        await CastNarratorAsync();
        _engine.Ready = true;
        _encoder.Available = false;

        await _rpc.StartAsync("M4b", Path.Combine(Output, "book.m4b"));
        var status = await SettledAsync();

        Assert.Equal("no-encoder", status.Note);
        Assert.Equal("WavPerChapter", status.DeliveredFormat);
        Assert.NotEmpty(status.Files);
    }

    [Fact]
    public async Task APackagerThatThrows_EndsAsFailedRatherThanLeavingTheJobRunningForEver()
    {
        await ChapterAsync("One", "<p>Hello there.</p>");
        await CastNarratorAsync();
        _engine.Ready = true;
        _encoder.Throw = true;

        await _rpc.StartAsync("M4b", Path.Combine(Output, "book.m4b"));
        var status = await SettledAsync();

        Assert.Equal("failed", status.Phase);
        Assert.Equal(nameof(InvalidOperationException), status.Error);
    }

    [Fact]
    public async Task Start_WithAnEngineWhoseModelWillNotLoad_SaysSoRatherThanHanging()
    {
        await ChapterAsync("One", "<p>Hello there.</p>");
        await CastNarratorAsync();
        _engine.ThrowOnPrepare = true;

        var status = await _rpc.StartAsync("M4b", Path.Combine(Output, "book.m4b"));

        Assert.Equal("failed", status.Phase);
        Assert.Equal("no-engine", status.Error);
    }

    [Fact]
    public async Task Start_WithAnEngineThatStartsAndStillIsNotReady_DoesNotRenderSilence()
    {
        await ChapterAsync("One", "<p>Hello there.</p>");
        await CastNarratorAsync();
        // Prepare returns, and the engine is no readier for it. Treating "the
        // call came back" as "it works" would send a chapter to a model that is
        // not loaded and write a silent audiobook.
        _engine.RefuseToBecomeReady = true;

        var status = await _rpc.StartAsync("M4b", Path.Combine(Output, "book.m4b"));

        Assert.Equal("no-engine", status.Error);
    }

    [Fact]
    public async Task Start_RecordsAVoiceTheWriterSetOverOneChapter()
    {
        // The export is the one place a wrong voice is baked into a file and
        // published. It compiled the book without a position, so every scoped
        // voice was silently ignored and the audiobook disagreed with what the
        // writer had been listening to.
        var chapter = await ChapterAsync("One", "<p>Hello there.</p>");
        await CastNarratorAsync();
        await new VoiceStore(_workspace.Projects, _workspace.FileService).SaveAsync(
            new DesignedVoice(
                "the-boy", "The boy", string.Empty, SilentEngine.Id, "wav", 24000,
                DateTime.UtcNow.ToString("O")),
            [4, 5, 6]);
        await new VoiceCast(_workspace.Projects, _workspace.FileService)
            .SetScopeAsync(null, new VoiceScope(null, chapter.Guid, null), "the-boy");
        _engine.Ready = true;

        await _rpc.StartAsync("M4b", Path.Combine(Output, "book.m4b"));
        await SettledAsync();

        Assert.Contains("the-boy", _engine.VoicesUsed);
    }

    [Fact]
    public async Task AnEngineThatIsAskedItsStatusAndThrows_IsNotTheOneChosen()
    {
        await ChapterAsync("One", "<p>Hello there.</p>");
        _workspace.ExtensionsHost.VoiceEngines.Insert(0, new BrokenEngine());
        // Cast in the broken engine's voice, so it is genuinely the one that
        // would have to speak.
        await new VoiceStore(_workspace.Projects, _workspace.FileService).SaveAsync(
            new DesignedVoice(
                "broken", "Broken", string.Empty, "com.example.broken", "wav", 24000,
                DateTime.UtcNow.ToString("O")),
            [1, 2, 3]);
        await new VoiceCast(_workspace.Projects, _workspace.FileService)
            .SetVoiceAsync(null, "broken");

        var estimate = await _rpc.EstimateAsync();

        Assert.Null(estimate.EngineId);
    }

    [Fact]
    public async Task Start_CastInAnEngineThisMachineDoesNotHave_SaysSoRatherThanUsingAnother()
    {
        // A project assembled somewhere else, or an extension since removed.
        // Recording the book in whatever engine happens to be installed would
        // be an audiobook in the wrong voice that nothing reported.
        await ChapterAsync("One", "<p>Hello there.</p>");
        await new VoiceStore(_workspace.Projects, _workspace.FileService).SaveAsync(
            new DesignedVoice(
                "elsewhere", "Elsewhere", string.Empty, "com.example.gone", "wav", 24000,
                DateTime.UtcNow.ToString("O")),
            [1, 2, 3]);
        await new VoiceCast(_workspace.Projects, _workspace.FileService)
            .SetVoiceAsync(null, "elsewhere");
        _engine.Ready = true;

        var status = await _rpc.StartAsync("M4b", Path.Combine(Output, "book.m4b"));

        Assert.Equal("no-engine", status.Error);
    }

    [Fact]
    public async Task Start_WithNoNarratorCast_TakesTheEngineMostOfTheCastBelongsTo()
    {
        // A book part-way through being cast: characters have voices and the
        // narrator does not yet. The export still has to pick an engine, and
        // the one that made most of the voices is the one that gets most of the
        // book right.
        await ChapterAsync("One", "<p>\"Hello there,\" said Mira.</p>");
        var mira = new CharacterData { Name = "Mira" };
        await new EntityService(_workspace.Projects).SaveCharacterAsync(mira);
        await new VoiceStore(_workspace.Projects, _workspace.FileService).SaveAsync(
            new DesignedVoice(
                "hers", "Hers", string.Empty, SilentEngine.Id, "wav", 24000,
                DateTime.UtcNow.ToString("O")),
            [1, 2, 3]);
        await new VoiceCast(_workspace.Projects, _workspace.FileService)
            .SetVoiceAsync(mira.Id, "hers");
        _engine.Ready = true;

        Assert.Equal(SilentEngine.Id, (await _rpc.EstimateAsync()).EngineId);
    }

    // ─── the stubs ──────────────────────────────────────────────────

    /// <summary>Speaks every line as a tenth of a second of silence.</summary>
    private sealed class SilentEngine : IExtension, IVoiceEngineContributor
    {
        public const string Id = "com.example.silent";

        public bool Ready { get; set; }
        public bool Throw { get; set; }

        /// <summary>Gigabytes still to fetch, which is what makes an engine one
        /// nobody may start on the writer's behalf.</summary>
        public long? DownloadBytes { get; set; }

        public int Prepared { get; private set; }

        /// <summary>Held so a test can catch a render while it is genuinely in
        /// flight rather than after it has finished.</summary>
        public TaskCompletionSource? Hold { get; set; }

        /// <summary>Signalled once a render is genuinely under way. Without it
        /// a test can look before the background task has been scheduled, which
        /// is a race in the test rather than in the product.</summary>
        public TaskCompletionSource? Started { get; set; }

        public int Renders { get; private set; }
        public List<string> Spoken { get; } = [];

        string IExtension.Id => Id;
        public string DisplayName => "Silent";
        public string Description => "Speaks silence.";
        public string Version => "1.0";
        public string Author => "Tests";
        public void Initialize(IHostServices host) { }
        public void Shutdown() { }

        public string EngineId => Id;
        public string EngineName => "Silent";
        public VoiceEngineFeatures Features => VoiceEngineFeatures.EmotionVector;

        public Task<VoiceEngineStatus> GetStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new VoiceEngineStatus
            {
                IsReady = Ready,
                DownloadBytes = DownloadBytes
            });

        /// <summary>An engine whose model will not load - a driver that has
        /// gone, weights somebody deleted.</summary>
        public bool ThrowOnPrepare { get; set; }

        /// <summary>An engine that returns from being prepared and is still not
        /// ready, which is what a cancelled download looks like.</summary>
        public bool RefuseToBecomeReady { get; set; }

        /// <summary>Which voice each line was actually sent in - the thing an
        /// export bakes into a file somebody then publishes.</summary>
        public List<string> VoicesUsed { get; } = [];

        public Task PrepareAsync(
            IProgress<VoiceEnginePrepare>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Prepared++;
            if (ThrowOnPrepare)
                throw new InvalidOperationException("no");
            Ready = !RefuseToBecomeReady;
            return Task.CompletedTask;
        }

        public Task<VoiceDesignResult> DesignVoiceAsync(
            VoiceBrief brief, CancellationToken cancellationToken = default)
            => Task.FromResult(new VoiceDesignResult
            {
                VoiceId = brief.VoiceId,
                ReferenceAudio = [1, 2, 3],
                SampleRate = 24000
            });

        public async IAsyncEnumerable<NarrationClip> RenderAsync(
            NarrationRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Renders++;
            Started?.TrySetResult();
            if (Hold is { } wait)
            {
                Hold = null;
                await wait.Task;
            }
            if (Throw)
                throw new InvalidOperationException("no");

            var shape = new WaveFormat(24000, 1, 16);
            foreach (var segment in request.Segments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Spoken.Add(segment.Text);
                VoicesUsed.Add(segment.VoiceId);
                yield return new NarrationClip
                {
                    Key = segment.Key,
                    Audio = WaveAudio.Write(shape, WaveAudio.Silence(shape, 100)),
                    AudioFormat = "wav",
                    SampleRate = 24000,
                    DurationMs = 100
                };
            }
        }

        public Task ForgetVoiceAsync(string voiceId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>An engine that cannot even say whether it is ready.</summary>
    private sealed class BrokenEngine : IExtension, IVoiceEngineContributor
    {
        string IExtension.Id => "com.example.broken";
        public string DisplayName => "Broken";
        public string Description => "Throws.";
        public string Version => "1.0";
        public string Author => "Tests";
        public void Initialize(IHostServices host) { }
        public void Shutdown() { }

        public string EngineId => "com.example.broken";
        public string EngineName => "Broken";
        public VoiceEngineFeatures Features => VoiceEngineFeatures.None;

        public Task<VoiceEngineStatus> GetStatusAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("no");

        public Task PrepareAsync(
            IProgress<VoiceEnginePrepare>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<VoiceDesignResult> DesignVoiceAsync(
            VoiceBrief brief, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("no");

        public async IAsyncEnumerable<NarrationClip> RenderAsync(
            NarrationRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task ForgetVoiceAsync(string voiceId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>An encoder that is there, or is not.</summary>
    private sealed class FakeEncoder : IMediaEncoder
    {
        public bool Available { get; set; } = true;
        public bool Throw { get; set; }

        public Task<bool> AvailableAsync(CancellationToken cancellationToken = default)
            => Throw
                ? throw new InvalidOperationException("no")
                : Task.FromResult(Available);

        public Task<(int ExitCode, string Output)> RunAsync(
            IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
            => Task.FromResult((0, string.Empty));
    }
}
