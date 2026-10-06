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

    /// <summary>Transient audio for the active reading. Never logged or cached
    /// as a complete passage; the render result supplies the finished clip.</summary>
    public static Action<NarrationAudioChunkDto>? AudioChunk { get; set; }

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
                    html,
                    candidates,
                    new NarrationLanguageContext(dialogueLanguage, directionLanguage, utteranceLanguage),
                    new SceneNarrationSettings()
                    {
                        SpeakerOverrides = scene.DialogueSpeakers,
                        DirectionOverrides = scene.DialogueDirections,
                        Emotion = scene.AnalysisOverrides?.Emotion,
                        Intensity = scene.AnalysisOverrides?.Intensity
                    }))
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
