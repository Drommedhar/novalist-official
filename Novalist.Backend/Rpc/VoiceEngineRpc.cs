using Novalist.Backend.Extensions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models.Narration;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Backs the designed half of narration: which speech engines are installed,
/// getting one ready, and designing a character a voice of their own.
///
/// Novalist loads no model itself. Everything here goes through
/// <see cref="IVoiceEngineContributor"/>, which an extension supplies - the same
/// arrangement the Wiki's article generator and the grammar checker already use,
/// and the reason the core app carries no AI dependency.
///
/// The brief a voice is designed from is built here, from the Codex entry and
/// the character's own lines, and it describes the <b>instrument</b> only. The
/// emotion belongs to the per-line direction and is applied at render time to a
/// fixed identity, so a character can be furious in one chapter and grieving in
/// the next without being two voices.
/// </summary>
public sealed class VoiceEngineRpc
{
    private readonly Workspace _workspace;
    private readonly EntityService _entities;
    private readonly VoiceStore _voices;
    private readonly VoiceCast _cast;
    private readonly NarrationClipCache _clips;

    /// <summary>Cancels whatever render is in flight. Replaced per render, so a
    /// second Play cancels the first rather than racing it.</summary>
    private CancellationTokenSource? _rendering;

    public VoiceEngineRpc(Workspace workspace, NarrationClipCache? clips = null)
    {
        _workspace = workspace;
        _entities = new EntityService(workspace.Projects);
        _voices = new VoiceStore(workspace.Projects, workspace.FileService);
        _cast = new VoiceCast(workspace.Projects, workspace.FileService);
        _clips = clips ?? new NarrationClipCache(workspace.SettingsDirectory);
    }

    /// <summary>
    /// The speech engines installed, with what each can do and whether it is
    /// ready. Empty when none is installed, which is the signal to keep offering
    /// the system voices rather than an empty picker.
    /// </summary>
    [JsonRpcMethod("voiceEngines/list")]
    public async Task<VoiceEngineDto[]> ListAsync()
    {
        var engines = new List<VoiceEngineDto>();
        foreach (var engine in _workspace.ExtensionsHost.VoiceEngines)
        {
            // An engine that throws while being asked how it is is reported as
            // not ready rather than taking the view down with it.
            VoiceEngineStatus status;
            try
            {
                status = await engine.GetStatusAsync();
            }
            catch (Exception ex)
            {
                status = new VoiceEngineStatus { Error = ex.GetType().Name };
            }

            engines.Add(new VoiceEngineDto(
                engine.EngineId,
                engine.EngineName,
                (int)engine.Features,
                status.IsReady,
                status.IsPreparing,
                status.Error,
                status.Detail,
                status.DownloadBytes));
        }

        Log.Info($"voiceEngines/list count={engines.Count} ready={engines.Count(e => e.IsReady)}.");
        return [.. engines];
    }

    /// <summary>
    /// Gets an engine ready - the download, the environment, the first model
    /// load. Returns its status afterwards, so the caller learns what happened
    /// rather than only that the call returned.
    /// </summary>
    [JsonRpcMethod("voiceEngines/prepare")]
    public async Task<VoiceEngineDto?> PrepareAsync(string engineId)
    {
        var engine = Find(engineId);
        if (engine == null)
            return null;

        string? failure = null;
        try
        {
            // The engine reports its own progress to the writer through the
            // host's busy dialog - it is the only party that knows whether it is
            // downloading, unpacking or loading. Nothing useful can be returned
            // from here until it finishes, which is exactly why this call is not
            // allowed to hold the request queue.
            await engine.PrepareAsync();
        }
        catch (Exception ex)
        {
            // Only the type is logged: an engine's own words can quote a path,
            // and the diagnostic log must never carry one.
            Log.Warn($"voiceEngines/prepare failed type={ex.GetType().Name}.");
            failure = ex.GetType().Name;
        }

        // Its own status first, because that is where an engine puts the reason
        // in words the writer can act on - reporting the exception type instead
        // left "InvalidOperationException" on screen in place of "install
        // Python". But an engine that throws and then says nothing about itself
        // must not leave the writer with no reason at all, so the type stands in
        // where there is nothing better.
        var status = (await ListAsync()).FirstOrDefault(e => e.EngineId == engineId);
        if (status == null || failure == null || !string.IsNullOrWhiteSpace(status.Error))
            return status;
        return status with { Error = failure };
    }

    /// <summary>
    /// The brief for a character, for the writer to read and edit before
    /// anything is designed.
    ///
    /// Shown first on purpose. A design prompt assembled invisibly is one the
    /// writer cannot correct, and this one is assembled from fields they may not
    /// have thought of as describing a voice.
    /// </summary>
    [JsonRpcMethod("voiceEngines/brief")]
    public async Task<VoiceBriefDto?> BriefAsync(string characterId, bool consent = false)
    {
        // Nothing to build a brief from, and an exception across the wire would
        // reach the writer as a failed call rather than as an empty dialog.
        if (_workspace.Projects.ActiveBook == null)
            return null;

        var character = (await _entities.LoadCharactersAsync())
            .FirstOrDefault(c => string.Equals(c.Id, characterId, StringComparison.Ordinal));
        if (character == null)
            return null;

        var draft = VoiceBriefBuilder.Build(
            character,
            await SampleLinesAsync(characterId),
            SceneAnalysisLexicon.For(WritingLanguage()),
            consent);

        Log.Info(
            $"voiceEngines/brief refusal={draft.Refusal} len={draft.Description.Length} " +
            $"samples={draft.SampleLines.Count}.");

        return new VoiceBriefDto(
            characterId,
            EntityResolveIndex.Compose(character.Name, character.Surname),
            draft.Description,
            [.. draft.SampleLines],
            draft.Refusal.ToString());
    }

    /// <summary>
    /// Designs a voice for a character and stores it, then casts them in it.
    ///
    /// <paramref name="description"/> is what the writer approved, which may not
    /// be what the builder proposed - and it goes through the same emotion filter
    /// either way, because a rule the dialog can talk its way around is not a
    /// rule.
    /// </summary>
    [JsonRpcMethod("voiceEngines/design")]
    public async Task<VoiceDesignDto> DesignAsync(
        string engineId, string characterId, string description, bool consent = false)
    {
        // No project, nowhere to keep the audio - and no point asking an engine
        // to spend a minute designing something that cannot be stored.
        if (_workspace.Projects.ActiveBook == null)
            return VoiceDesignDto.Failed("NoProject");

        var engine = Find(engineId);
        if (engine == null)
            return VoiceDesignDto.Failed("NoEngine");
        if (!engine.Features.HasFlag(VoiceEngineFeatures.DesignFromDescription))
            return VoiceDesignDto.Failed("EngineCannotDesign");

        var brief = await BriefAsync(characterId, consent);
        if (brief == null)
            return VoiceDesignDto.Failed("NoCharacter");
        if (brief.Refusal != nameof(VoiceBriefRefusal.None))
            return VoiceDesignDto.Failed(brief.Refusal);

        var lexicon = SceneAnalysisLexicon.For(WritingLanguage());
        var wanted = VoiceBriefBuilder.Strip(description, lexicon);
        var voiceId = $"{characterId}-{engine.EngineId}";

        VoiceDesignResult designed;
        try
        {
            designed = await engine.DesignVoiceAsync(new VoiceBrief
            {
                VoiceId = voiceId,
                DisplayName = brief.Name,
                Description = wanted.Length > 0 ? wanted : brief.Description,
                SampleLines = brief.SampleLines,
                Language = WritingLanguage()
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"voiceEngines/design failed type={ex.GetType().Name}.");
            return VoiceDesignDto.Failed(ex.GetType().Name);
        }

        var stored = new DesignedVoice(
            designed.VoiceId,
            brief.Name,
            wanted,
            engine.EngineId,
            designed.AudioFormat,
            designed.SampleRate,
            DateTime.UtcNow.ToString("O"));

        // The store only refuses when there is no project, which the guard at the
        // top of this method has already answered - so its result is not checked
        // a second time here.
        await _voices.SaveAsync(stored, designed.ReferenceAudio);

        // Designing a voice for somebody and not casting them in it would leave
        // the writer one more step to discover.
        await _cast.SetVoiceAsync(characterId, designed.VoiceId);

        Log.Info(
            $"voiceEngines/design ok bytes={designed.ReferenceAudio.Length} " +
            $"rate={designed.SampleRate}.");
        return new VoiceDesignDto(designed.VoiceId, stored.Description, null);
    }

    /// <summary>Every voice this book has been given.</summary>
    [JsonRpcMethod("voiceEngines/voices")]
    public async Task<DesignedVoiceDto[]> VoicesAsync()
        => [.. (await _voices.ListAsync()).Select(v => new DesignedVoiceDto(
            v.VoiceId, v.DisplayName, v.Description, v.EngineId, v.DesignedAt))];

    /// <summary>
    /// Forgets a designed voice, and un-casts anybody reading in it.
    ///
    /// Leaving the cast pointing at a voice that no longer exists would give the
    /// writer a reading that silently falls back to the narrator with nothing on
    /// screen saying why.
    /// </summary>
    [JsonRpcMethod("voiceEngines/forget")]
    public async Task<bool> ForgetAsync(string voiceId)
    {
        var voice = await _voices.GetAsync(voiceId);
        if (voice == null || !await _voices.DeleteAsync(voiceId))
            return false;

        var sheet = await _cast.ReadAsync();
        foreach (var (characterId, cast) in sheet.Voices.ToArray())
        {
            if (string.Equals(cast, voiceId, StringComparison.Ordinal))
                await _cast.SetVoiceAsync(characterId, null);
        }
        if (string.Equals(sheet.NarratorVoiceId, voiceId, StringComparison.Ordinal))
            await _cast.SetVoiceAsync(null, null);

        var engine = Find(voice.EngineId);
        if (engine != null)
        {
            try
            {
                await engine.ForgetVoiceAsync(voiceId);
            }
            catch (Exception ex)
            {
                // The project has already forgotten it; an engine that will not
                // is a diagnostic, not a failure the writer can act on.
                Log.Warn($"voiceEngines/forget engine refused type={ex.GetType().Name}.");
            }
        }

        Log.Info("voiceEngines/forget ok.");
        return true;
    }

    /// <summary>
    /// Renders a run of the reading through the active engine and returns where
    /// the audio can be fetched from.
    ///
    /// A window rather than the whole book. The backend answers one request at a
    /// time, so a render that took the whole manuscript would hold every other
    /// screen behind it for as long as it ran - and a reading the writer stops
    /// after one paragraph would have paid for all of it. The interface asks for
    /// the next window while the current one plays.
    ///
    /// The clips go to a cache beside the application and come back as names.
    /// Audio does not belong in a JSON message.
    /// </summary>
    /// <param name="from">Index into the book's segments, as narration/book
    /// returns them.</param>
    /// <param name="count">How many segments to render at most.</param>
    [JsonRpcMethod("narration/render")]
    public async Task<NarrationRenderDto> RenderAsync(int from, int count, double rate = 1.0)
    {
        var engine = Ready();
        if (engine == null)
            return new NarrationRenderDto(null, [], 0);

        var segments = await SegmentsAsync();
        var window = segments
            .Skip(Math.Max(0, from))
            .Take(Math.Clamp(count, 1, MaxRenderWindow))
            .ToArray();
        if (window.Length == 0)
            return new NarrationRenderDto(engine.EngineId, [], segments.Count);

        var sheet = await _cast.ReadAsync();
        var audio = await _voices.ReadAudioForAsync(NarrationRender.VoicesNeeded(window, sheet));
        // The clips any of these lines were told to sound like. Almost always
        // none, so almost always a call that reads nothing.
        var references = await _clips.ReadManyAsync(NarrationRender.ClipsNeeded(window));
        var request = NarrationRender.Build(
            window, sheet, audio, engine.Features, WritingLanguage(), rate, references);
        if (request.Segments.Count == 0)
            return new NarrationRenderDto(engine.EngineId, [], segments.Count);

        // A second Play cancels the first rather than racing it.
        _rendering?.Cancel();
        var cancellation = new CancellationTokenSource();
        _rendering = cancellation;

        var clips = new List<NarrationClipDto>();
        try
        {
            await foreach (var clip in engine.RenderAsync(request, cancellation.Token))
            {
                if (cancellation.IsCancellationRequested)
                    break;
                if (clip.Error != null)
                {
                    clips.Add(new NarrationClipDto(clip.Key, null, 0, clip.Error));
                    continue;
                }
                clips.Add(new NarrationClipDto(
                    clip.Key,
                    await _clips.WriteAsync(clip.Audio, clip.AudioFormat),
                    clip.DurationMs,
                    null));
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped on purpose. What was rendered before the stop is still
            // worth returning; the interface simply will not play it.
        }
        catch (Exception ex)
        {
            Log.Warn($"narration/render failed type={ex.GetType().Name}.");
            clips.Add(new NarrationClipDto(string.Empty, null, 0, ex.GetType().Name));
        }
        finally
        {
            if (ReferenceEquals(_rendering, cancellation))
                _rendering = null;
            cancellation.Dispose();
        }

        Log.Info(
            $"narration/render from={from} asked={window.Length} clips={clips.Count} " +
            $"failed={clips.Count(c => c.Error != null)} cacheBytes={_clips.Size()}.");
        return new NarrationRenderDto(engine.EngineId, [.. clips], segments.Count);
    }

    /// <summary>
    /// Speaks one line where the writer is writing it.
    ///
    /// The Narration view is for listening to the book; this is for the moment
    /// in the middle of writing a line when the question is whether it sounds
    /// right in the mouth of the person saying it. Going to another view,
    /// finding the line and pressing play answers that question too late to be
    /// any use.
    ///
    /// The line is looked up in the scene rather than spoken as raw text, so it
    /// arrives cast and directed exactly as the reading would have it - a
    /// preview in the narrator's voice of a line the character speaks would be
    /// answering a different question.
    /// </summary>
    /// <param name="text">The selected prose.</param>
    [JsonRpcMethod("narration/auditionLine")]
    public async Task<NarrationClipDto> AuditionLineAsync(
        string chapterGuid, string sceneId, string text)
    {
        var engine = Ready();
        var wanted = Normalise(text);
        if (engine == null || wanted.Length == 0)
            return new NarrationClipDto(string.Empty, null, 0, engine == null ? "no-engine" : "empty");

        var segments = await SegmentsForAsync(chapterGuid, sceneId);
        // The segment the selection sits inside, rather than one equal to it: a
        // writer selects a phrase far more often than a whole line, and the line
        // is what has a voice and a direction.
        var segment = segments.FirstOrDefault(s => Normalise(s.Text).Contains(wanted, StringComparison.Ordinal))
            ?? segments.FirstOrDefault(s => wanted.Contains(Normalise(s.Text), StringComparison.Ordinal));
        if (segment == null)
            return new NarrationClipDto(string.Empty, null, 0, "not-in-scene");

        var sheet = await _cast.ReadAsync();
        var audio = await _voices.ReadAudioForAsync(
            NarrationRender.VoicesNeeded([segment], sheet));
        var references = await _clips.ReadManyAsync(NarrationRender.ClipsNeeded([segment]));
        var request = NarrationRender.Build(
            [segment], sheet, audio, engine.Features, WritingLanguage(), 1.0, references);
        if (request.Segments.Count == 0)
            return new NarrationClipDto(segment.Key, null, 0, "no-voice");

        try
        {
            await foreach (var clip in engine.RenderAsync(request, CancellationToken.None))
            {
                if (clip.Error != null)
                    return new NarrationClipDto(clip.Key, null, 0, clip.Error);
                Log.Info($"narration/auditionLine ok len={segment.Text.Length}.");
                return new NarrationClipDto(
                    clip.Key,
                    await _clips.WriteAsync(clip.Audio, clip.AudioFormat),
                    clip.DurationMs,
                    null);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"narration/auditionLine failed type={ex.GetType().Name}.");
            return new NarrationClipDto(segment.Key, null, 0, ex.GetType().Name);
        }

        return new NarrationClipDto(segment.Key, null, 0, "sidecar-exited");
    }

    /// <summary>One scene's segments, cast and directed the same way the whole
    /// book's are.</summary>
    private async Task<IReadOnlyList<Core.Services.NarrationSegment>> SegmentsForAsync(
        string chapterGuid, string sceneId)
    {
        ChapterData chapter;
        SceneData scene;
        try
        {
            (chapter, scene) = _workspace.ResolveScene(chapterGuid, sceneId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            // The editor asked about a scene that has been moved or deleted
            // since. Nothing to speak, rather than a fault reaching the writer.
            return [];
        }

        var characters = await _entities.LoadCharactersAsync();
        var lexicon = SceneAnalysisLexicon.For(WritingLanguage());
        var html = await _workspace.Projects.ReadSceneContentAsync(chapter, scene);

        return NarrationScript.Build(
            html,
            DialogueAttributor.BuildCandidates(characters, lexicon?.WordBoundaries ?? true),
            DialogueAttributor.BuildLanguage(lexicon),
            EmotionDirector.BuildLanguage(lexicon),
            scene.DialogueSpeakers,
            scene.DialogueDirections,
            scene.AnalysisOverrides?.Emotion,
            scene.AnalysisOverrides?.Intensity);
    }

    /// <summary>Prose as it can be compared: the editor hands back a selection
    /// whose whitespace is the document's, and the script's is collapsed.</summary>
    private static string Normalise(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    /// <summary>
    /// Stops a render and empties the cache.
    ///
    /// Skips the request queue, like stopping the system voices: queued behind
    /// the render it is meant to interrupt, it could not arrive until that render
    /// had finished, which is to say it would not stop anything.
    /// </summary>
    [JsonRpcMethod("narration/renderStop")]
    public bool RenderStop()
    {
        _rendering?.Cancel();
        _clips.Clear();
        Log.Info("narration/renderStop.");
        return true;
    }

    /// <summary>
    /// Designs the narrator's voice from the book rather than from a Codex
    /// entry.
    ///
    /// The narrator is not a character and has no entry to read. What decides
    /// how a book should be narrated is what kind of book it is and who is
    /// telling it - the declared person and tense, and the logline - all of
    /// which the writer already wrote down.
    /// </summary>
    [JsonRpcMethod("narration/designNarrator")]
    public async Task<VoiceDesignDto> DesignNarratorAsync(string engineId, string description)
    {
        var book = _workspace.Projects.ActiveBook;
        if (book == null)
            return VoiceDesignDto.Failed("NoProject");

        var engine = Find(engineId);
        if (engine == null)
            return VoiceDesignDto.Failed("NoEngine");
        if (!engine.Features.HasFlag(VoiceEngineFeatures.DesignFromDescription))
            return VoiceDesignDto.Failed("EngineCannotDesign");

        var lexicon = SceneAnalysisLexicon.For(WritingLanguage());
        var typed = VoiceBriefBuilder.Strip(description, lexicon);
        var wanted = typed.Length > 0
            ? typed
            : VoiceBriefBuilder.Strip(NarrationRender.NarratorBrief(book), lexicon);
        var voiceId = $"narrator-{engine.EngineId}";

        VoiceDesignResult designed;
        try
        {
            designed = await engine.DesignVoiceAsync(new VoiceBrief
            {
                VoiceId = voiceId,
                DisplayName = book.Name,
                Description = wanted,
                Language = WritingLanguage()
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"narration/designNarrator failed type={ex.GetType().Name}.");
            return VoiceDesignDto.Failed(ex.GetType().Name);
        }

        var stored = new DesignedVoice(
            designed.VoiceId, book.Name, wanted, engine.EngineId,
            designed.AudioFormat, designed.SampleRate, DateTime.UtcNow.ToString("O"));
        await _voices.SaveAsync(stored, designed.ReferenceAudio);
        await _cast.SetVoiceAsync(null, designed.VoiceId);

        Log.Info($"narration/designNarrator ok bytes={designed.ReferenceAudio.Length}.");
        return new VoiceDesignDto(designed.VoiceId, wanted, null);
    }

    /// <summary>What the narrator's brief would say, for the dialog to show
    /// before anything is designed.</summary>
    [JsonRpcMethod("narration/narratorBrief")]
    public string NarratorBrief()
        => VoiceBriefBuilder.Strip(
            NarrationRender.NarratorBrief(_workspace.Projects.ActiveBook),
            SceneAnalysisLexicon.For(WritingLanguage()));

    /// <summary>
    /// Speaks one line in a designed voice at several points on the emotional
    /// range, and returns the clips.
    ///
    /// Three emotions rather than one, because one neutral sample says nothing
    /// about whether the casting works. The claim the whole two-stage design
    /// rests on is that a designed identity survives being performed - and this
    /// is where a writer hears whether it does.
    /// </summary>
    [JsonRpcMethod("voiceEngines/audition")]
    public async Task<AuditionClipDto[]> AuditionAsync(
        string voiceId, string text, string[]? emotions = null)
    {
        var voice = await _voices.GetAsync(voiceId);
        var engine = voice == null ? null : Find(voice.EngineId);
        var audio = await _voices.ReadAudioAsync(voiceId);
        if (voice == null || engine == null || audio == null)
            return [];

        var keys = emotions is { Length: > 0 } ? emotions : DefaultAuditionEmotions();
        var request = new NarrationRequest
        {
            Language = WritingLanguage(),
            Voices = new Dictionary<string, byte[]>(StringComparer.Ordinal) { [voiceId] = audio },
            Segments =
            [
                .. keys.Select(key => new Sdk.Models.Narration.NarrationSegment
                {
                    Key = key,
                    Text = text,
                    VoiceId = voiceId,
                    IsDialogue = true,
                    Direction = Direct(key)
                })
            ]
        };

        var clips = new List<AuditionClipDto>();
        try
        {
            await foreach (var clip in engine.RenderAsync(request))
            {
                clips.Add(new AuditionClipDto(
                    clip.Key,
                    Convert.ToBase64String(clip.Audio),
                    clip.AudioFormat,
                    clip.SampleRate,
                    clip.DurationMs,
                    clip.Error));
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"voiceEngines/audition failed type={ex.GetType().Name}.");
        }

        Log.Info($"voiceEngines/audition clips={clips.Count} asked={keys.Length}.");
        return [.. clips];
    }

    /// <summary>
    /// The three emotions an audition uses when the caller names none: the
    /// neutral one and the two furthest from it the language has. A sample that
    /// only proves a voice can be calm proves nothing about the book.
    /// </summary>
    private string[] DefaultAuditionEmotions()
    {
        var keys = SceneAnalysisLexicon.For(WritingLanguage())?.EmotionKeys ?? [];
        return
        [
            EmotionDirector.NeutralKey,
            .. new[] { "angry", "sorrowful" }.Where(keys.Contains)
        ];
    }

    /// <summary>One emotion key as every kind of direction an engine might take:
    /// the name, the numbers, and a sentence. The engine uses whichever its
    /// feature flags say it understands.</summary>
    private static Sdk.Models.Narration.VoiceDirection Direct(string key)
        => new()
        {
            Key = key,
            Vector = EmotionDirector.Vector(key, null),
            Instruction = $"Read this {key}.",
            Source = nameof(DirectionSource.Writer)
        };

    /// <summary>A few of this character's own lines, for the brief. How somebody
    /// talks describes their voice better than any adjective, and the writer
    /// already wrote it.</summary>
    private async Task<IReadOnlyList<string>> SampleLinesAsync(string characterId)
    {
        var characters = await _entities.LoadCharactersAsync();
        var index = await new DialogueIndexService(_workspace.Projects)
            .BuildAsync(characters, characterId, WritingLanguage());

        return
        [
            .. index.Groups
                .SelectMany(g => g.Scenes)
                .SelectMany(s => s.Lines)
                .Select(l => l.Text)
                .Take(VoiceBriefBuilder.MaxSampleLines)
        ];
    }

    /// <summary>The most segments one render call will take on. Small enough
    /// that stopping is quick and that no other screen waits long behind it.</summary>
    private const int MaxRenderWindow = 24;

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
                Log.Warn($"voiceEngines status type={ex.GetType().Name}.");
            }
        }
        return null;
    }

    /// <summary>The book's segments in reading order - the same run, in the same
    /// order, that narration/book returns, so an index means the same thing on
    /// both sides.</summary>
    private async Task<IReadOnlyList<Core.Services.NarrationSegment>> SegmentsAsync()
    {
        var book = _workspace.Projects.ActiveBook;
        if (book == null)
            return [];

        var characters = await _entities.LoadCharactersAsync();
        var lexicon = SceneAnalysisLexicon.For(WritingLanguage());
        var candidates = DialogueAttributor.BuildCandidates(
            characters, lexicon?.WordBoundaries ?? true);
        var dialogueLanguage = DialogueAttributor.BuildLanguage(lexicon);
        var directionLanguage = EmotionDirector.BuildLanguage(lexicon);
        var manifest = _workspace.Projects.ScenesManifest;

        var all = new List<Core.Services.NarrationSegment>();
        foreach (var chapter in book.Chapters.OrderBy(c => c.Order))
        {
            var scenes = (manifest?.Chapters.GetValueOrDefault(chapter.Guid) ?? [])
                .Where(s => s.ArchivedAt == null)
                .OrderBy(s => s.Order);
            foreach (var scene in scenes)
            {
                var html = await _workspace.Projects.ReadSceneContentAsync(chapter, scene);
                all.AddRange(NarrationScript.Build(
                    html, candidates, dialogueLanguage, directionLanguage,
                    scene.DialogueSpeakers, scene.DialogueDirections,
                    scene.AnalysisOverrides?.Emotion, scene.AnalysisOverrides?.Intensity));
            }
        }
        return all;
    }

    private IVoiceEngineContributor? Find(string? engineId)
        => _workspace.ExtensionsHost.VoiceEngines.FirstOrDefault(
            e => string.Equals(e.EngineId, engineId, StringComparison.Ordinal));

    /// <summary>The project's writing language, resolved exactly as the rest of
    /// narration resolves it.</summary>
    private string WritingLanguage()
    {
        var overrides = _workspace.Projects.ProjectRoot == null
            ? null
            : _workspace.Projects.ProjectSettings.Overrides;
        return overrides?.AutoReplacementLanguage
               ?? _workspace.Settings.Settings.AutoReplacementLanguage
               ?? "en";
    }
}

/// <summary>One installed speech engine.</summary>
/// <param name="Features">The <c>VoiceEngineFeatures</c> flags as an integer, so
/// the renderer can test them without a second copy of the enum.</param>
public sealed record VoiceEngineDto(
    string EngineId,
    string EngineName,
    int Features,
    bool IsReady,
    bool IsPreparing,
    string? Error,
    string Detail,
    long? DownloadBytes);

/// <summary>What a character's voice would be designed from.</summary>
/// <param name="Refusal">"None", or why it cannot be - "WithheldFromAi" when the
/// writer set the entry to never reach a model.</param>
public sealed record VoiceBriefDto(
    string CharacterId,
    string Name,
    string Description,
    string[] SampleLines,
    string Refusal);

/// <summary>The outcome of designing a voice.</summary>
public sealed record VoiceDesignDto(string? VoiceId, string Description, string? Error)
{
    public static VoiceDesignDto Failed(string error) => new(null, string.Empty, error);
}

/// <summary>A voice this book has been given.</summary>
public sealed record DesignedVoiceDto(
    string VoiceId, string DisplayName, string Description, string EngineId, string DesignedAt);

/// <summary>One audition clip, base64 so it crosses JSON-RPC.</summary>
/// <param name="Key">The emotion it was read with.</param>
public sealed record AuditionClipDto(
    string Key, string Audio, string AudioFormat, int SampleRate, double DurationMs, string? Error);

/// <summary>One rendered segment.</summary>
/// <param name="Clip">The name to fetch the audio by, or null when this segment
/// could not be spoken.</param>
public sealed record NarrationClipDto(string Key, string? Clip, double DurationMs, string? Error);

/// <summary>What one render window produced.</summary>
/// <param name="EngineId">Null when no engine is ready, which is the signal to
/// read with the system voices instead.</param>
/// <param name="Total">How many segments the book has, so the interface knows
/// when it has reached the end.</param>
public sealed record NarrationRenderDto(string? EngineId, NarrationClipDto[] Clips, int Total);
