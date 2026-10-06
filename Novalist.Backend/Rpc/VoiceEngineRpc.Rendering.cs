using Novalist.Backend.Extensions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models.Narration;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class VoiceEngineRpc
{
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
        int from, int count, double rate = 1.0, bool rebuild = false, string? streamId = null)
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
                    window,
                    new NarrationRenderContext(sheet, mine, engine.Features, WritingLanguage())
                    {
                        Clips = references,
                        VoiceReferenceTexts = referenceTexts
                    },
                    rate,
                    at => placed[at].Where);
                if (request.Segments.Count == 0)
                    continue;

                spoke ??= engine.EngineId;

                if (!await RenderEngineClipsAsync(engine, request, mine, clips,
                    (rebuild, streamId, window[0].Key), cancellation.Token)) continue;
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

        return await FinishRenderAsync(window, clips, spoke, (from, byEngine.Length, segments.Count));
    }

    private async Task<NarrationRenderDto> FinishRenderAsync(
        IReadOnlyList<Core.Services.NarrationSegment> window, List<NarrationClipDto> clips,
        string? spoke, (int From, int Engines, int Segments) totals)
    {
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var at = 0; at < window.Count; at++)
            order.TryAdd(window[at].Key, at);

        // Clips outlive a reading now, so the folder has to be bounded. What
        // this window is about to play is named as worth keeping however old it
        // is: a writer listening to one chapter all afternoon is playing clips
        // made hours ago, and evicting those would make the cache useless to
        // exactly the person it is for.
        _clips.Trim(MaxCacheBytes, [.. clips.Select(c => c.Clip).OfType<string>()]);

        Log.Info(
            $"narration/render from={totals.From} asked={window.Count} clips={clips.Count} " +
            $"engines={totals.Engines} failed={clips.Count(c => c.Error != null)} " +
            $"cacheBytes={_clips.Size()}.");
        return new NarrationRenderDto(
            spoke ?? await AnyEngineAsync(),
            [.. clips.OrderBy(c => order.GetValueOrDefault(c.Key, int.MaxValue))],
            totals.Segments);
    }

    private async Task<bool> RenderEngineClipsAsync(IVoiceEngineContributor engine,
        Sdk.Models.Narration.NarrationRequest request, IReadOnlyDictionary<string, byte[]> voices,
        List<NarrationClipDto> clips, (bool Rebuild, string? StreamId, string FirstKey) options,
        CancellationToken cancellation)
    {
        var (recipes, fresh) = PendingClips(engine.EngineId, request, voices, clips, options.Rebuild);
        if (fresh.Count == 0) return false;
        var outstanding = new List<string>(fresh.Select(f => f.Key));
        SayMaking(outstanding);
        var sequence = 0;
        var asking = new Sdk.Models.Narration.NarrationRequest
        {
            // Later passages must not overtake the first while a window is still being sorted.
            AudioChunk = string.IsNullOrEmpty(options.StreamId) ? null : chunk =>
            {
                if (!cancellation.IsCancellationRequested && chunk.Key == options.FirstKey)
                    AudioChunk?.Invoke(new NarrationAudioChunkDto(
                        options.StreamId, chunk.Key, sequence++, Convert.ToBase64String(chunk.Audio), chunk.SampleRate));
            },
            Segments = fresh,
            Voices = request.Voices,
            VoiceReferenceTexts = request.VoiceReferenceTexts,
            Language = request.Language,
            Rate = request.Rate
        };
        await foreach (var clip in engine.RenderAsync(asking, cancellation))
        {
            if (cancellation.IsCancellationRequested) break;
            if (clip.Error != null)
            {
                clips.Add(new NarrationClipDto(clip.Key, null, 0, clip.Error));
                continue;
            }
            clips.Add(new NarrationClipDto(
                clip.Key,
                await _clips.WriteAsAsync(
                    recipes.GetValueOrDefault(clip.Key) ?? NarrationClipCache.NameFor(clip.Audio, clip.AudioFormat),
                    clip.Audio),
                clip.DurationMs, null));
            outstanding.Remove(clip.Key);
            SayMaking(outstanding);
        }
        SayMaking([]);
        return true;
    }

    private (Dictionary<string, string> Recipes, List<Sdk.Models.Narration.NarrationSegment> Fresh) PendingClips(
        string engineId, Sdk.Models.Narration.NarrationRequest request,
        IReadOnlyDictionary<string, byte[]> voices, List<NarrationClipDto> clips, bool rebuild)
    {
        var recipes = new Dictionary<string, string>(StringComparer.Ordinal);
        var fresh = new List<Sdk.Models.Narration.NarrationSegment>(request.Segments.Count);
        foreach (var segment in request.Segments)
        {
            var name = NarrationRecipe.For(
                segment, engineId, request.Language, request.Rate,
                voices.GetValueOrDefault(segment.VoiceId),
                request.VoiceReferenceTexts.GetValueOrDefault(segment.VoiceId)) + ".wav";
            recipes[segment.Key] = name;
            if (rebuild || !_clips.Has(name))
                fresh.Add(segment);
            else
                clips.Add(new NarrationClipDto(segment.Key, name, 0, null));
        }
        return (recipes, fresh);
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
            [segment],
            new NarrationRenderContext(sheet, audio, engine.Features, WritingLanguage())
            {
                Clips = references,
                VoiceReferenceTexts = referenceTexts
            });
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
            new NarrationLanguageContext(
                DialogueAttributor.BuildLanguage(lexicon),
                EmotionDirector.BuildLanguage(lexicon),
                UtteranceLanguage.From(lexicon)),
            new SceneNarrationSettings()
            {
                SpeakerOverrides = scene.DialogueSpeakers,
                DirectionOverrides = scene.DialogueDirections,
                Emotion = scene.AnalysisOverrides?.Emotion,
                Intensity = scene.AnalysisOverrides?.Intensity
            });
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
}
