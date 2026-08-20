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
        var engines = await StatusesAsync();
        // Stamped after the fact, because the statuses were read a moment before
        // these were started and would otherwise report the state this call
        // just changed - which left the cast rail saying "not ready" with
        // nothing on it moving, and no reason for anything to ask again.
        var started = StartWhatIsAlreadyInstalled(engines);
        return
        [
            .. engines.Select(e => started.Contains(e.Status.EngineId)
                ? e.Status with { IsPreparing = true }
                : e.Status)
        ];
    }

    /// <summary>
    /// Starts any engine that has everything it needs and is only waiting to be
    /// asked.
    ///
    /// An engine's model is loaded into a process that dies with the app, so
    /// "prepared" never survived a restart - and the button that fixed that sits
    /// on a rail the writer has no reason to look at once the download is done.
    /// The result was an application that had a speech engine installed,
    /// reported it as not ready every morning, and read the book in the
    /// operating system's voice until somebody found the button again.
    ///
    /// Only what is already downloaded. An engine with gigabytes still to fetch
    /// is a decision the writer makes, on a metered connection they may be
    /// paying for, and starting that unasked would be indefensible - which is
    /// exactly what <see cref="VoiceEngineStatus.DownloadBytes"/> distinguishes.
    ///
    /// Once per engine per session. A model that fails to load fails the same
    /// way every time, and retrying it on every refresh would spend a process
    /// start per poll to learn the same thing.
    /// </summary>
    /// <returns>The engines this call started, so their statuses can say so.</returns>
    private HashSet<string> StartWhatIsAlreadyInstalled(IReadOnlyList<EngineStatus> engines)
    {
        var wanted = new List<IVoiceEngineContributor>();
        lock (_gate)
        {
            foreach (var (engine, status) in engines)
            {
                if (status.IsReady || status.IsPreparing || status.DownloadBytes is > 0)
                    continue;
                if (!_started.Add(status.EngineId))
                    continue;
                wanted.Add(engine);
            }
        }

        foreach (var engine in wanted)
        {
            Log.Info($"voiceEngines auto-start id={engine.EngineId}.");
            // Not awaited. Loading a model is tens of seconds and this is a
            // status call - a list that blocked on it would freeze the cast rail
            // for the whole load, which is the thing being fixed rather than a
            // cheaper version of it. Started outside the lock, because an
            // engine's own first moments are not this object's to hold.
            var starting = StartAsync(engine);
            lock (_gate)
            {
                _starting[engine.EngineId] = starting;
            }
        }
        return [.. wanted.Select(e => e.EngineId)];
    }

    /// <summary>Whether this engine has a start of ours still running.</summary>
    private bool Starting(string engineId)
    {
        lock (_gate)
        {
            return _starting.TryGetValue(engineId, out var task) && !task.IsCompleted;
        }
    }

    private static async Task StartAsync(IVoiceEngineContributor engine)
    {
        try
        {
            await engine.PrepareAsync();
        }
        catch (Exception ex)
        {
            // Nobody asked for this, so nobody is waiting on a reason. The
            // engine's own status carries it for the next list.
            Log.Warn($"voiceEngines auto-start failed type={ex.GetType().Name}.");
        }
    }

    /// <summary>Engines already started without being asked.</summary>
    private readonly HashSet<string> _started = new(StringComparer.Ordinal);

    /// <summary>Starts in flight, by engine id, so a reading that arrives during
    /// one waits for it rather than falling back to the operating system's
    /// voice - and so a status can say that one is under way.</summary>
    private readonly Dictionary<string, Task> _starting = new(StringComparer.Ordinal);

    /// <summary>
    /// Guards the two above.
    ///
    /// Preparing an engine, designing a voice and hearing a line are all exempt
    /// from the backend's one-at-a-time gate - they are model loads, and queuing
    /// the application behind one is what that exemption exists to prevent. So
    /// two of these calls genuinely do run at once, and both of them touch this.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>An engine and what it says about itself, kept together so a
    /// caller acting on a status does not have to look the engine up again by
    /// its id.</summary>
    private sealed record EngineStatus(IVoiceEngineContributor Engine, VoiceEngineDto Status);

    private async Task<List<EngineStatus>> StatusesAsync()
    {
        var engines = new List<EngineStatus>();
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

            engines.Add(new EngineStatus(engine, new VoiceEngineDto(
                engine.EngineId,
                engine.EngineName,
                (int)engine.Features,
                status.IsReady,
                // Or one we started ourselves. An engine loading a model because
                // the app opened is preparing in every sense the interface cares
                // about, and an engine that does not say so about itself would
                // otherwise leave the cast rail reporting "not ready" until the
                // writer happened to reopen the view.
                status.IsPreparing || Starting(engine.EngineId),
                status.Error,
                status.Detail,
                status.DownloadBytes)));
        }

        Log.Info(
            $"voiceEngines/list count={engines.Count} " +
            $"ready={engines.Count(e => e.Status.IsReady)}.");
        return engines;
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
        // Statuses rather than the list: this engine has just been prepared by
        // hand, and an auto-start fired off the back of reading its result would
        // be a second load of the model it either just loaded or just failed to.
        lock (_gate)
        {
            _started.Add(engineId);
        }
        var status = (await StatusesAsync())
            .Select(e => e.Status)
            .FirstOrDefault(e => e.EngineId == engineId);
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
        string engineId,
        string characterId,
        string description,
        bool consent = false,
        string? act = null,
        string? chapter = null,
        string? scene = null,
        int? seed = null)
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
        // A voice designed for one stretch of the book gets an id of its own, so
        // asking for an older Mira in Act Three does not overwrite how she
        // sounded in Act One. Designing a standing voice is the same call with
        // nowhere named, and mints the id it always did.
        var where = new VoiceScope(act, chapter, scene);
        var voiceId = VoiceCast.ScopedVoiceId(characterId, engine.EngineId, where);

        VoiceDesignResult designed;
        try
        {
            designed = await engine.DesignVoiceAsync(new VoiceBrief
            {
                VoiceId = voiceId,
                DisplayName = brief.Name,
                Description = wanted.Length > 0 ? wanted : brief.Description,
                SampleLines = brief.SampleLines,
                Language = WritingLanguage(),
                // Null asks the engine for a fresh draw, which is what "I did
                // not like that one, try again" has to mean. A number asks for
                // one particular voice back.
                Seed = seed is >= 0 ? seed : null
            });
        }
        catch (Exception ex)
        {
            // The type goes to the log, because an exception's message can name
            // a path and the diagnostic log is a thing writers send us. The
            // message goes to the screen in front of the person whose machine
            // it is, because "InvalidOperationException" is not a reason and
            // left them with nothing to act on and nothing to tell us.
            Log.Warn($"voiceEngines/design failed type={ex.GetType().Name}.");
            return VoiceDesignDto.Failed(Reason(ex));
        }

        var stored = new DesignedVoice(
            designed.VoiceId,
            brief.Name,
            wanted,
            engine.EngineId,
            designed.AudioFormat,
            designed.SampleRate,
            DateTime.UtcNow.ToString("O"),
            designed.Seed,
            designed.ReferenceText);

        return await OfferAsync(stored, designed, characterId, where);
    }

    /// <summary>
    /// Holds a designed voice for the writer to hear before it becomes
    /// anybody's.
    ///
    /// Voice design is not reliable per attempt - the same description asked
    /// for twice gives two voices, and one of them may not be the voice that
    /// was asked for at all. Storing the first result and casting it made a
    /// miss into the character's voice until somebody noticed. So it is offered
    /// instead: rendered to the clip cache, played, and kept only if it is
    /// right.
    /// </summary>
    private async Task<VoiceDesignDto> OfferAsync(
        DesignedVoice stored,
        VoiceDesignResult designed,
        string? characterId,
        VoiceScope? where = null)
    {
        var clip = await _clips.WriteAsync(designed.ReferenceAudio, designed.AudioFormat);
        _candidate = new VoiceCandidate(stored, designed.ReferenceAudio, characterId, where);

        Log.Info(
            $"voiceEngines/design offered bytes={designed.ReferenceAudio.Length} " +
            $"rate={designed.SampleRate} narrator={characterId == null} " +
            $"scoped={where?.IsSomewhere == true} seeded={stored.Seed != null}.");
        return new VoiceDesignDto(stored.VoiceId, stored.Description, null, clip, stored.Seed);
    }

    /// <summary>
    /// Keeps the voice that was offered: stores the audio and casts whoever it
    /// was designed for.
    /// </summary>
    [JsonRpcMethod("voiceEngines/keepVoice")]
    public async Task<bool> KeepVoiceAsync()
    {
        if (_candidate is not { } candidate)
            return false;

        await _voices.SaveAsync(candidate.Stored, candidate.Audio);
        // Designing a voice for somebody and not casting them in it would leave
        // the writer one more step to discover - and a voice designed for one
        // stretch of the book must be cast over that stretch rather than become
        // how the character sounds everywhere, which is the opposite of what
        // was asked for.
        var scoped = candidate.Where is { IsSomewhere: true };
        if (scoped)
        {
            await _cast.SetScopeAsync(
                candidate.CharacterId, candidate.Where!, candidate.Stored.VoiceId);
        }
        else
        {
            await _cast.SetVoiceAsync(candidate.CharacterId, candidate.Stored.VoiceId);
        }
        _candidate = null;

        Log.Info($"voiceEngines/keepVoice ok scoped={scoped}.");
        return true;
    }

    /// <summary>
    /// Throws the offered voice away. Nothing was stored, so this only forgets
    /// - but it is a call rather than a timeout, because the writer closing the
    /// dialog is a decision.
    /// </summary>
    [JsonRpcMethod("voiceEngines/discardVoice")]
    public bool DiscardVoice()
    {
        var had = _candidate != null;
        _candidate = null;
        Log.Info($"voiceEngines/discardVoice had={had}.");
        return had;
    }

    /// <summary>A voice designed and not yet kept.</summary>
    /// <param name="CharacterId">Who it was designed for; null for the narrator.</param>
    private sealed record VoiceCandidate(
        DesignedVoice Stored, byte[] Audio, string? CharacterId, VoiceScope? Where = null);

    private VoiceCandidate? _candidate;

    /// <summary>
    /// What to put in front of the writer when a design fails.
    ///
    /// The engine's own words where it gave any - the Speech extension reports
    /// codes such as "sidecar-exited-while-designing" - and the exception type
    /// only when it did not, which is the case for a fault that never reached
    /// the engine at all.
    /// </summary>
    private static string Reason(Exception ex)
        => string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;

    /// <summary>Every voice this book has been given.</summary>
    [JsonRpcMethod("voiceEngines/voices")]
    public async Task<DesignedVoiceDto[]> VoicesAsync()
        => [.. (await _voices.ListAsync()).Select(v => new DesignedVoiceDto(
            v.VoiceId, v.DisplayName, v.Description, v.EngineId, v.DesignedAt, v.Seed))];

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

        // And the stretches it was cast over. A scope left pointing at a voice
        // that no longer exists is worse than a stale standing cast: it wins
        // over the character's real voice, so those chapters fall silently back
        // to the narrator while the rest of the book is right.
        var stale = (await _cast.ReadAsync()).Overrides
            .Where(o => string.Equals(o.VoiceId, voiceId, StringComparison.Ordinal))
            .ToArray();
        foreach (var scope in stale)
        {
            await _cast.SetScopeAsync(
                string.IsNullOrEmpty(scope.CharacterId) ? null : scope.CharacterId,
                new VoiceScope(scope.Act, scope.Chapter, scope.Scene),
                null);
        }

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
    /// <param name="rebuild">Makes every line again even where one identical to
    /// it is already on disk. For the writer who does not like what an engine
    /// gave them: design is not reproducible, and asking twice is the only way
    /// to get a second answer.</param>
    [JsonRpcMethod("narration/render")]
    public async Task<NarrationRenderDto> RenderAsync(
        int from, int count, double rate = 1.0, bool rebuild = false)
    {
        var segments = await SegmentsAsync();
        var placed = segments
            .Skip(Math.Max(0, from))
            .Take(Math.Clamp(count, 1, MaxRenderWindow))
            .ToArray();
        if (placed.Length == 0)
            return new NarrationRenderDto(await AnyEngineAsync(), [], segments.Count);

        var window = placed.Select(p => p.Segment).ToArray();
        var sheet = await _cast.ReadAsync();
        var audio = await _voices.ReadAudioForAsync(NarrationRender.VoicesNeeded(window, sheet));
        var referenceTexts = await _voices.ReadReferenceTextsForAsync(audio.Keys);
        // The clips any of these lines were told to sound like. Almost always
        // none, so almost always a call that reads nothing.
        var references = await _clips.ReadManyAsync(NarrationRender.ClipsNeeded(window));

        // The voices in play, grouped by the engine that made each of them. A
        // book is almost always cast entirely in one engine's voices, so this is
        // almost always one group - but which engine it is has to be decided by
        // the voice rather than by whichever engine finished loading first.
        var owners = await OwnersAsync();
        var byEngine = audio
            .GroupBy(pair => owners.GetValueOrDefault(pair.Key, string.Empty), StringComparer.Ordinal)
            .Where(group => group.Key.Length > 0)
            .ToArray();

        // A second Play cancels the first rather than racing it.
        _rendering?.Cancel();
        var cancellation = new CancellationTokenSource();
        _rendering = cancellation;

        var clips = new List<NarrationClipDto>();
        string? spoke = null;
        try
        {
            foreach (var group in byEngine)
            {
                if (await ReadyAsync(group.Key) is not { } engine)
                    continue;

                // Only this engine's voices. A line cast in another engine's
                // voice is left for that engine's turn rather than handed to
                // this one, which would speak somebody else's character in its
                // own idea of their voice.
                var mine = group.ToDictionary(
                    pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                var request = NarrationRender.Build(
                    window, sheet, mine, engine.Features, WritingLanguage(), rate, references,
                    at => placed[at].Where, referenceTexts);
                if (request.Segments.Count == 0)
                    continue;

                spoke ??= engine.EngineId;

                // What has already been made. A line is keyed by everything
                // that decides how it sounds - the words, the voice's own audio,
                // the direction, the pace, the engine - so a line nobody has
                // touched since it was last spoken is already on disk, and
                // asking for it again is a look at the filesystem instead of
                // seconds inside a model.
                var recipes = new Dictionary<string, string>(StringComparer.Ordinal);
                var fresh = new List<Sdk.Models.Narration.NarrationSegment>(request.Segments.Count);
                foreach (var segment in request.Segments)
                {
                    var name = NarrationRecipe.For(
                        segment, engine.EngineId, request.Language, request.Rate,
                        mine.GetValueOrDefault(segment.VoiceId),
                        request.VoiceReferenceTexts.GetValueOrDefault(segment.VoiceId)) + ".wav";
                    recipes[segment.Key] = name;

                    if (rebuild || !_clips.Has(name))
                    {
                        fresh.Add(segment);
                        continue;
                    }
                    clips.Add(new NarrationClipDto(segment.Key, name, 0, null));
                }

                if (fresh.Count == 0)
                    continue;

                // Which line is being made, one at a time, as it happens.
                //
                // The page used to hatch the whole batch that had been asked
                // for - twelve sentences of somebody's prose marked as "being
                // made" when eleven of them had not been started. What a writer
                // wants to see is the line the model is on.
                //
                // Engines yield in order, but nothing in the contract says they
                // must, so this tracks what is outstanding rather than counting.
                var outstanding = new List<string>(fresh.Select(f => f.Key));
                SayMaking(outstanding);

                // Only the lines that still have to be made. An engine handed
                // a window it has already spoken would spend a minute
                // reproducing what is on disk beside it.
                var asking = new Sdk.Models.Narration.NarrationRequest
                {
                    Segments = fresh,
                    Voices = request.Voices,
                    VoiceReferenceTexts = request.VoiceReferenceTexts,
                    Language = request.Language,
                    Rate = request.Rate
                };
                await foreach (var clip in engine.RenderAsync(asking, cancellation.Token))
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
                        await _clips.WriteAsAsync(
                            recipes.GetValueOrDefault(clip.Key)
                            ?? NarrationClipCache.NameFor(clip.Audio, clip.AudioFormat),
                            clip.Audio),
                        clip.DurationMs,
                        null));

                    outstanding.Remove(clip.Key);
                    SayMaking(outstanding);
                }
                SayMaking([]);
                if (cancellation.IsCancellationRequested)
                    break;
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

        // Back into reading order. Two engines produce two runs of clips, and the
        // interface plays them in the order they arrive - so unsorted, a book
        // cast across two engines would be read in two passes.
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var at = 0; at < window.Length; at++)
            order.TryAdd(window[at].Key, at);

        // Clips outlive a reading now, so the folder has to be bounded. What
        // this window is about to play is named as worth keeping however old it
        // is: a writer listening to one chapter all afternoon is playing clips
        // made hours ago, and evicting those would make the cache useless to
        // exactly the person it is for.
        _clips.Trim(MaxCacheBytes, [.. clips.Select(c => c.Clip).Where(c => c != null)!]);

        Log.Info(
            $"narration/render from={from} asked={window.Length} clips={clips.Count} " +
            $"engines={byEngine.Length} failed={clips.Count(c => c.Error != null)} " +
            $"cacheBytes={_clips.Size()}.");
        return new NarrationRenderDto(
            spoke ?? await AnyEngineAsync(),
            [.. clips.OrderBy(c => order.GetValueOrDefault(c.Key, int.MaxValue))],
            segments.Count);
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
        var wanted = Normalise(text);
        if (wanted.Length == 0)
            return new NarrationClipDto(string.Empty, null, 0, "empty");

        var segments = await SegmentsForAsync(chapterGuid, sceneId);
        // The segment the selection sits inside, rather than one equal to it: a
        // writer selects a phrase far more often than a whole line, and the line
        // is what has a voice and a direction.
        var segment = segments.FirstOrDefault(s => Normalise(s.Text).Contains(wanted, StringComparison.Ordinal))
            ?? segments.FirstOrDefault(s => wanted.Contains(Normalise(s.Text), StringComparison.Ordinal));
        if (segment == null)
            return new NarrationClipDto(string.Empty, null, 0, "not-in-scene");

        var sheet = await _cast.ReadAsync();
        // The engine that made this speaker's voice, not whichever one answered
        // first. A line auditioned in another engine's idea of the character is
        // answering a different question from the one the writer asked.
        var voiceId = VoiceCast.Resolve(sheet, segment.SpeakerId);
        if (voiceId == null)
            return new NarrationClipDto(segment.Key, null, 0, "no-voice");
        if (await ReadyAsync((await OwnersAsync()).GetValueOrDefault(voiceId)) is not { } engine)
            return new NarrationClipDto(segment.Key, null, 0, "no-engine");

        var audio = await _voices.ReadAudioForAsync(
            NarrationRender.VoicesNeeded([segment], sheet));
        var referenceTexts = await _voices.ReadReferenceTextsForAsync(audio.Keys);
        var references = await _clips.ReadManyAsync(NarrationRender.ClipsNeeded([segment]));
        var request = NarrationRender.Build(
            [segment], sheet, audio, engine.Features, WritingLanguage(), 1.0, references,
            voiceReferenceTexts: referenceTexts);
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
            scene.AnalysisOverrides?.Intensity,
            UtteranceLanguage.From(lexicon));
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
        // The clips stay. Emptying the cache here was why listening to a
        // paragraph twice cost twice, and why correcting one line in a scene
        // paid for the whole scene again - the writer stops, fixes a word, and
        // presses Play, which is the single commonest thing to do in this view.
        // They go when the project closes, or when the writer asks for the
        // reading to be made again.
        Log.Info("narration/renderStop.");
        return true;
    }

    /// <summary>
    /// Throws away the rendered reading, so the next Play makes it again.
    ///
    /// Design is not reproducible and neither is delivery: the same line asked
    /// for twice comes back differently. A writer who does not like what they
    /// heard has no other way to get a second answer, and without this the
    /// reuse that makes the reading fast would also make it fixed.
    /// </summary>
    [JsonRpcMethod("narration/renderAgain")]
    public bool RenderAgain()
    {
        _rendering?.Cancel();
        _clips.Clear();
        Log.Info("narration/renderAgain.");
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
    public async Task<VoiceDesignDto> DesignNarratorAsync(
        string engineId, string description, int? seed = null)
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
            : NarrationRender.NarratorBrief(book, lexicon);
        var voiceId = $"narrator-{engine.EngineId}";

        VoiceDesignResult designed;
        try
        {
            designed = await engine.DesignVoiceAsync(new VoiceBrief
            {
                VoiceId = voiceId,
                DisplayName = book.Name,
                Description = wanted,
                Language = WritingLanguage(),
                Seed = seed is >= 0 ? seed : null
            });
        }
        catch (Exception ex)
        {
            // The type goes to the log, because an exception's message can name
            // a path and the diagnostic log is a thing writers send us. The
            // message goes to the screen in front of the person whose machine
            // it is, because "InvalidOperationException" is not a reason and
            // left them with nothing to act on and nothing to tell us.
            Log.Warn($"narration/designNarrator failed type={ex.GetType().Name}.");
            return VoiceDesignDto.Failed(Reason(ex));
        }

        var stored = new DesignedVoice(
            designed.VoiceId, book.Name, wanted, engine.EngineId,
            designed.AudioFormat, designed.SampleRate, DateTime.UtcNow.ToString("O"),
            designed.Seed, designed.ReferenceText);

        return await OfferAsync(stored, designed, characterId: null);
    }

    /// <summary>What the narrator's brief would say, for the dialog to show
    /// before anything is designed.</summary>
    [JsonRpcMethod("narration/narratorBrief")]
    public string NarratorBrief()
        => NarrationRender.NarratorBrief(
            _workspace.Projects.ActiveBook, SceneAnalysisLexicon.For(WritingLanguage()));

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
        if (engine.Features.HasFlag(VoiceEngineFeatures.EmotionInferred))
            keys = [EmotionDirector.NeutralKey];
        var referenceTexts = await _voices.ReadReferenceTextsForAsync([voiceId]);
        var request = new NarrationRequest
        {
            Language = WritingLanguage(),
            Voices = new Dictionary<string, byte[]>(StringComparer.Ordinal) { [voiceId] = audio },
            VoiceReferenceTexts = referenceTexts,
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

    /// <summary>
    /// How much rendered speech is kept.
    ///
    /// A whole novel is hours of audio and far more than this, so what survives
    /// is the part being worked on - which is what a writer listening to the
    /// same chapter all afternoon actually needs. Two gigabytes is a few hours
    /// of 48 kHz speech and a rounding error next to the model that made it.
    /// </summary>
    private const long MaxCacheBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// Any engine at all that is ready, for the one thing that is not about a
    /// particular voice: telling the interface whether to fall back to the
    /// operating system's voices. Null means it should.
    /// </summary>
    private async Task<string?> AnyEngineAsync()
    {
        await SettledAsync();
        foreach (var engine in _workspace.ExtensionsHost.VoiceEngines)
        {
            try
            {
                if ((await engine.GetStatusAsync()).IsReady)
                    return engine.EngineId;
            }
            catch (Exception ex)
            {
                Log.Warn($"voiceEngines status type={ex.GetType().Name}.");
            }
        }
        return null;
    }

    /// <summary>
    /// Says which line an engine is working on now, and which have been made.
    ///
    /// A render window is asked for in one call and answered in one call, so
    /// without this the page learns nothing for the whole of it - which for a
    /// dozen long sentences is a minute of a wall of hatching, and no way to
    /// tell it apart from a reading that has stopped.
    /// </summary>
    public static Action<NarrationMakingDto>? Making { get; set; }

    private static void SayMaking(IReadOnlyList<string> outstanding)
        => Making?.Invoke(new NarrationMakingDto(outstanding.Count > 0 ? outstanding[0] : null));

    /// <summary>
    /// Which engine designed each of these voices.
    ///
    /// A voice records the engine that made it, and that is the only engine that
    /// can speak in it: a reference clip is one model's idea of a speaker, and
    /// handing it to another model gets that model's idea of the same thing.
    /// </summary>
    private async Task<Dictionary<string, string>> OwnersAsync()
    {
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var voice in await _voices.ListAsync())
        {
            if (!string.IsNullOrEmpty(voice.EngineId))
                owners[voice.VoiceId] = voice.EngineId;
        }
        return owners;
    }

    /// <summary>
    /// The engine that made a voice, ready to speak, or null where it is not
    /// installed or cannot be got ready.
    ///
    /// By the voice rather than by whichever engine answered first. That was the
    /// bug: with two engines installed the reading went to whichever had
    /// finished loading, so a writer with a real speech engine and the example
    /// tone generator heard their whole book as a sine wave - and nothing said
    /// why, because the reading was working exactly as it had been told to.
    /// </summary>
    private async Task<IVoiceEngineContributor?> ReadyAsync(string? engineId)
    {
        await SettledAsync();
        if (Find(engineId) is not { } engine)
            return null;

        try
        {
            var status = await engine.GetStatusAsync();
            if (status.IsReady)
                return engine;
            // Installed and merely not loaded. Starting it is the same decision
            // the cast rail makes when it is opened; gigabytes still to fetch
            // are not, and are left for the writer to agree to.
            if (status.IsPreparing || status.DownloadBytes is > 0)
                return null;

            lock (_gate)
            {
                if (!_started.Add(engine.EngineId))
                    return null;
            }
            await StartAsync(engine);
            return (await engine.GetStatusAsync()).IsReady ? engine : null;
        }
        catch (Exception ex)
        {
            Log.Warn($"voiceEngines status type={ex.GetType().Name}.");
            return null;
        }
    }

    /// <summary>
    /// Waits for any start already under way.
    ///
    /// The cast rail starts an installed engine the moment it is looked at, and
    /// a writer who presses Play a second later would otherwise be read to in
    /// the operating system's voice by a book that was about to have its own -
    /// which sounds like the designed voices were never applied.
    /// </summary>
    private async Task SettledAsync()
    {
        // Copied under the lock: a second call adding to it during the wait
        // would otherwise be enumerating a list somebody else is writing.
        KeyValuePair<string, Task>[] waiting;
        lock (_gate)
        {
            waiting = [.. _starting];
        }
        if (waiting.Length > 0)
        {
            await Task.WhenAll(waiting.Select(w => w.Value));
            lock (_gate)
            {
                foreach (var (id, task) in waiting)
                {
                    if (_starting.TryGetValue(id, out var current) && current == task)
                        _starting.Remove(id);
                }
            }
        }

    }

    /// <summary>The book's segments in reading order - the same run, in the same
    /// order, that narration/book returns, so an index means the same thing on
    /// both sides.</summary>
    private async Task<IReadOnlyList<PlacedSegment>> SegmentsAsync()
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
        // What tells a sentence ending from a full stop that is merely a full
        // stop. Without it every point is an ending, and "10 a.m. sharp." is
        // three things for a model to say rather than one.
        var utteranceLanguage = UtteranceLanguage.From(lexicon);
        var manifest = _workspace.Projects.ScenesManifest;

        var all = new List<PlacedSegment>();
        foreach (var chapter in book.Chapters.OrderBy(c => c.Order))
        {
            var scenes = (manifest?.Chapters.GetValueOrDefault(chapter.Guid) ?? [])
                .Where(s => s.ArchivedAt == null)
                .OrderBy(s => s.Order);
            foreach (var scene in scenes)
            {
                var html = await _workspace.Projects.ReadSceneContentAsync(chapter, scene);
                // Kept beside each segment rather than thrown away with the
                // loop, because which voice a line is read in now depends on
                // where in the book the line is.
                var where = new NarrationPlacement(
                    chapter.Act, chapter.Guid, chapter.Title, scene.Title);
                foreach (var segment in NarrationScript.Build(
                    html, candidates, dialogueLanguage, directionLanguage,
                    scene.DialogueSpeakers, scene.DialogueDirections,
                    scene.AnalysisOverrides?.Emotion, scene.AnalysisOverrides?.Intensity,
                    utteranceLanguage))
                {
                    all.Add(new PlacedSegment(segment, where));
                }
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
/// <param name="Clip">Where the offered voice can be heard, in the clip cache.
/// Null when the design failed, and on nothing else.</param>
/// <param name="Seed">What this voice was drawn with. Shown beside the offer so
/// a writer who likes it can ask for the same one again - and so one they liked
/// and discarded is not gone for good.</param>
public sealed record VoiceDesignDto(
    string? VoiceId, string Description, string? Error, string? Clip = null, int? Seed = null)
{
    public static VoiceDesignDto Failed(string error) => new(null, string.Empty, error);
}

/// <summary>A voice this book has been given.</summary>
public sealed record DesignedVoiceDto(
    string VoiceId, string DisplayName, string Description, string EngineId, string DesignedAt,
    int? Seed = null);

/// <summary>One audition clip, base64 so it crosses JSON-RPC.</summary>
/// <param name="Key">The emotion it was read with.</param>
public sealed record AuditionClipDto(
    string Key, string Audio, string AudioFormat, int SampleRate, double DurationMs, string? Error);

/// <summary>The line an engine is making right now, or null when it is between
/// lines. Sent as it happens rather than with the window it belongs to.</summary>
public sealed record NarrationMakingDto(string? Key);

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
