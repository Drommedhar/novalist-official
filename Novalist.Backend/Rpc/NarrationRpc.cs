using Novalist.Backend.Extensions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Backs the Narration view: a scene laid out as the run of segments a reading
/// is made of, each one cast to a voice and directed by what the prose already
/// says about it.
///
/// Nothing here calls a model. Segmentation and attribution come from the same
/// two services the Dialogue view uses, and the direction comes from the
/// writing language's own speech-verb and emotion lexicons - so a reading can
/// be assembled, reviewed and corrected before any speech engine is installed,
/// and the first release performs it with the voices the operating system
/// already has.
///
/// Speaker corrections deliberately go through <see cref="DialogueIndexService"/>
/// rather than a second store. A line reassigned while listening is the same
/// line reassigned in the Dialogue view, and one of the two views showing a
/// correction the other does not would be worse than neither showing it.
/// </summary>
public sealed class NarrationRpc
{
    private readonly Workspace _workspace;
    private readonly EntityService _entities;
    private readonly VoiceCast _cast;

    public NarrationRpc(Workspace workspace)
    {
        _workspace = workspace;
        _entities = new EntityService(workspace.Projects);
        _cast = new VoiceCast(workspace.Projects, workspace.FileService);
    }

    /// <summary>
    /// The book's cast: everyone who speaks, most talkative first, with the
    /// voice they are read in. The narrator is not in the list because they are
    /// not a character - they come back separately, and they are the fallback
    /// every uncast character resolves to.
    /// </summary>
    [JsonRpcMethod("narration/cast")]
    public async Task<NarrationCastDto> CastAsync()
    {
        var characters = await _entities.LoadCharactersAsync();
        var index = await new DialogueIndexService(_workspace.Projects)
            .BuildAsync(characters, null, WritingLanguage());
        var sheet = await _cast.ReadAsync();

        var members = index.Speakers
            .Select(s => new NarrationCastMemberDto(
                s.CharacterId, s.Name, s.LineCount, sheet.Voices.GetValueOrDefault(s.CharacterId)))
            .ToArray();

        Log.Info(
            $"narration/cast speakers={members.Length} " +
            $"voiced={members.Count(m => m.VoiceId != null)} " +
            $"narrator={sheet.NarratorVoiceId != null} unassigned={index.UnassignedCount}.");

        return new NarrationCastDto(sheet.NarratorVoiceId, members, index.UnassignedCount);
    }

    /// <summary>
    /// The whole book as prose to be read: every chapter in order, every scene
    /// with its own HTML marked up so each segment of the reading is addressable
    /// where it stands.
    ///
    /// The book rather than the open scene, because a reading is not scene-sized.
    /// The Narration view followed whatever the editor had open, and the only way
    /// to move it was through the binder - which puts the editor back in the pane
    /// and takes the writer out of the view they were listening in. Handing over
    /// the book means scrolling is the navigation, exactly as the Manuscript view
    /// already works.
    /// </summary>
    [JsonRpcMethod("narration/book")]
    public async Task<NarrationBookDto> BookAsync()
    {
        var projects = _workspace.Projects;
        var book = projects.ActiveBook;
        if (book == null)
            return new NarrationBookDto([], 0);

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
        var sheet = await _cast.ReadAsync();
        var names = characters.ToDictionary(
            c => c.Id, c => EntityResolveIndex.Compose(c.Name, c.Surname), StringComparer.Ordinal);
        var manifest = projects.ScenesManifest;

        var chapters = new List<NarrationChapterDto>();
        var spoken = 0;
        foreach (var chapter in book.Chapters.OrderBy(c => c.Order))
        {
            var scenes = (manifest?.Chapters.GetValueOrDefault(chapter.Guid) ?? [])
                .Where(s => s.ArchivedAt == null)
                .OrderBy(s => s.Order)
                .ToList();
            if (scenes.Count == 0)
                continue;

            var sceneDtos = new List<NarrationProseSceneDto>();
            foreach (var scene in scenes)
            {
                var html = await projects.ReadSceneContentAsync(chapter, scene);
                // Which voice reads a line now depends on where in the book the
                // line is. Resolved without it, the view showed - and the
                // system-voice reading used - the character's standing voice
                // everywhere, so a voice set over an act was silently ignored by
                // everything except the designed-engine render.
                var where = new NarrationPlacement(
                    chapter.Act, chapter.Guid, chapter.Title, scene.Title);
                var segments = NarrationScript.Build(
                    html, candidates, dialogueLanguage, directionLanguage,
                    scene.DialogueSpeakers, scene.DialogueDirections,
                    scene.AnalysisOverrides?.Emotion, scene.AnalysisOverrides?.Intensity,
                    utteranceLanguage);

                spoken += segments.Count(s => s.Kind == NarrationSegmentKind.Dialogue);
                sceneDtos.Add(new NarrationProseSceneDto(
                    chapter.Guid,
                    scene.Id,
                    scene.Title,
                    scene.AnalysisOverrides?.Emotion,
                    scene.AnalysisOverrides?.Intensity,
                    NarrationProse.Annotate(html, segments),
                    [.. segments.Select(s => ToDto(s, names, sheet, where))]));
            }

            chapters.Add(new NarrationChapterDto(
                chapter.Guid, chapter.Title, chapter.Act, [.. sceneDtos]));
        }

        Log.Info(
            $"narration/book chapters={chapters.Count} " +
            $"scenes={chapters.Sum(c => c.Scenes.Length)} " +
            $"segments={chapters.Sum(c => c.Scenes.Sum(s => s.Segments.Length))} spoken={spoken}.");

        return new NarrationBookDto([.. chapters], spoken);
    }

    /// <summary>
    /// Casts a character, or the narrator when <paramref name="characterId"/> is
    /// blank. A blank <paramref name="voiceId"/> un-casts them, sending their
    /// lines back to the narrator.
    /// </summary>
    [JsonRpcMethod("narration/setVoice")]
    public async Task<bool> SetVoiceAsync(string? characterId, string? voiceId)
    {
        if (_workspace.Projects.ProjectRoot == null)
            return false;

        await _cast.SetVoiceAsync(
            string.IsNullOrWhiteSpace(characterId) ? null : characterId, voiceId);
        Log.Info($"narration/setVoice narrator={string.IsNullOrWhiteSpace(characterId)} " +
                 $"assigned={!string.IsNullOrWhiteSpace(voiceId)}.");
        return true;
    }

    /// <summary>
    /// The voices that only apply over part of the book.
    ///
    /// Separate from the cast because they are a different statement: the cast
    /// says who somebody is, and these say who they are <em>here</em>.
    /// </summary>
    [JsonRpcMethod("narration/voiceScopes")]
    public async Task<VoiceScopeDto[]> VoiceScopesAsync()
    {
        var sheet = await _cast.ReadAsync();
        Log.Info($"narration/voiceScopes count={sheet.Overrides.Count}.");
        return
        [
            .. sheet.Overrides.Select(o => new VoiceScopeDto(
                o.CharacterId ?? string.Empty, o.Act, o.Chapter, o.Scene, o.VoiceId))
        ];
    }

    /// <summary>
    /// Casts somebody for one stretch of the book: an act, a chapter, or a
    /// single scene.
    ///
    /// A character is not one voice for four hundred pages. They age, they are
    /// injured, they are disguised, they are remembered as a child in a chapter
    /// set thirty years earlier - and the only way to say so used to be editing
    /// the cast file by hand.
    /// </summary>
    /// <param name="characterId">Blank for the narrator.</param>
    /// <param name="voiceId">Blank clears the scope, which sends those lines
    /// back to the character's standing voice rather than silencing them.</param>
    [JsonRpcMethod("narration/setVoiceScope")]
    public async Task<bool> SetVoiceScopeAsync(
        string? characterId, string? act, string? chapter, string? scene, string? voiceId)
    {
        if (_workspace.Projects.ProjectRoot == null)
            return false;

        var set = await _cast.SetScopeAsync(
            string.IsNullOrWhiteSpace(characterId) ? null : characterId,
            new VoiceScope(act, chapter, scene),
            voiceId);

        // Which act, which chapter and which scene are the writer's own words -
        // a chapter title is prose. Only the shape of the statement is logged.
        Log.Info(
            $"narration/setVoiceScope narrator={string.IsNullOrWhiteSpace(characterId)} " +
            $"act={!string.IsNullOrWhiteSpace(act)} chapter={!string.IsNullOrWhiteSpace(chapter)} " +
            $"scene={!string.IsNullOrWhiteSpace(scene)} " +
            $"assigned={!string.IsNullOrWhiteSpace(voiceId)} ok={set}.");
        return set;
    }

    /// <summary>
    /// Directs one segment. An empty <paramref name="emotionKey"/> asks for the
    /// line to be read plainly, which is stored - it is a decision, and letting
    /// it fall back to the scene's emotion would quietly undo it. Pass null to
    /// drop the direction and hand the segment back to the prose.
    /// </summary>
    /// <param name="vector">The eight dimensions, pushed by hand, for a
    /// delivery none of the sixteen names covers. Null leaves the name to
    /// decide them.</param>
    /// <param name="referenceClip">A clip already rendered the way the writer
    /// wanted, for engines that can perform a line in the manner of one.</param>
    [JsonRpcMethod("narration/setDirection")]
    public Task<bool> SetDirectionAsync(
        string chapterGuid,
        string sceneId,
        string segmentKey,
        string? emotionKey,
        Dictionary<string, double>? vector = null,
        string? referenceClip = null)
        => SetDirectionsAsync(
            chapterGuid, sceneId, [segmentKey], emotionKey, vector, referenceClip);

    /// <summary>
    /// The same direction on a run of lines at once.
    ///
    /// A whole argument, a whole eulogy. Directing thirty lines one at a time is
    /// thirty chances to set one of them differently by accident, and the reason
    /// to direct a run by hand at all is that it is one performance.
    /// </summary>
    [JsonRpcMethod("narration/setDirections")]
    public async Task<bool> SetDirectionsAsync(
        string chapterGuid,
        string sceneId,
        string[] segmentKeys,
        string? emotionKey,
        Dictionary<string, double>? vector = null,
        string? referenceClip = null)
    {
        var located = Locate(chapterGuid, sceneId);
        if (located == null || segmentKeys.Length == 0)
            return false;

        var scene = located.Value.Scene;
        var cleared = emotionKey == null && vector == null && referenceClip == null;
        var changed = false;

        foreach (var segmentKey in segmentKeys)
        {
            if (cleared)
            {
                // Back to whatever the prose says, which is a different thing
                // from being directed to read plainly.
                if (scene.DialogueDirections?.Remove(segmentKey) == true)
                    changed = true;
                continue;
            }

            scene.DialogueDirections ??= new Dictionary<string, string>(StringComparer.Ordinal);
            scene.DialogueDirections[segmentKey] =
                DirectionCodec.Encode(emotionKey, vector, referenceClip);
            changed = true;
        }

        if (scene.DialogueDirections is { Count: 0 })
            scene.DialogueDirections = null;
        if (!changed)
            return false;

        await _workspace.Projects.SaveScenesAsync();
        Log.Info(
            $"narration/setDirections lines={segmentKeys.Length} cleared={cleared} " +
            $"byHand={vector != null} byClip={referenceClip != null}.");
        return true;
    }

    /// <summary>
    /// A character's standing register - what is added to every line they speak.
    ///
    /// A blank <paramref name="characterId"/> is the narrator. A null
    /// <paramref name="vector"/> clears it.
    /// </summary>
    [JsonRpcMethod("narration/setRegister")]
    public async Task<bool> SetRegisterAsync(
        string? characterId, Dictionary<string, double>? vector)
    {
        var sheet = await _cast.ReadAsync();
        var cleaned = vector?
            .Where(p => EmotionDirector.Dimensions.Contains(p.Key) && p.Value != 0)
            .ToDictionary(p => p.Key, p => Math.Clamp(p.Value, -1, 1), StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(characterId))
            sheet.NarratorRegister = cleaned is { Count: > 0 } ? cleaned : null;
        else if (cleaned is { Count: > 0 })
            sheet.Registers[characterId] = cleaned;
        else
            sheet.Registers.Remove(characterId);

        await _cast.WriteAsync(sheet);
        Log.Info(
            $"narration/setRegister narrator={string.IsNullOrWhiteSpace(characterId)} " +
            $"cleared={cleaned is not { Count: > 0 }}.");
        return true;
    }

    /// <summary>The standing registers, so the cast rail can show which
    /// characters carry one. The narrator's is under the empty key.</summary>
    [JsonRpcMethod("narration/registers")]
    public async Task<Dictionary<string, Dictionary<string, double>>> RegistersAsync()
    {
        var sheet = await _cast.ReadAsync();
        var all = new Dictionary<string, Dictionary<string, double>>(
            sheet.Registers, StringComparer.Ordinal);
        if (sheet.NarratorRegister is { Count: > 0 } narrator)
            all[string.Empty] = narrator;
        return all;
    }

    /// <summary>The dimensions a line can be directed in, so the editor draws
    /// the sliders the engine actually takes rather than a second copy of the
    /// list that can drift from it.</summary>
    [JsonRpcMethod("narration/dimensions")]
    public string[] Dimensions() => [.. EmotionDirector.Dimensions];

    /// <summary>
    /// The emotion keys the writing language declares, in the order it declares
    /// them, for the direction picker. Empty where the language ships no
    /// analysis lexicon - which is the signal to offer no picker rather than an
    /// English one.
    /// </summary>
    [JsonRpcMethod("narration/emotions")]
    public string[] Emotions()
    {
        var lexicon = SceneAnalysisLexicon.For(WritingLanguage());
        return lexicon == null ? [] : [.. lexicon.EmotionKeys];
    }

    /// <summary>One segment as the renderer reads it, with the narrator fallback
    /// already applied to its voice and every id already resolved to a name.
    /// Shared by the single scene and the whole book so the two can never drift
    /// into describing the same segment differently.</summary>
    /// <param name="where">Where in the book this line sits, so a voice the
    /// writer set over part of it resolves here as it will at playback. Null
    /// only where the caller genuinely has no position - and every caller that
    /// reads the book has one.</param>
    private static NarrationSegmentDto ToDto(
        NarrationSegment segment,
        IReadOnlyDictionary<string, string> names,
        VoiceCastSheet sheet,
        NarrationPlacement? where = null)
        => new(
            segment.Index,
            segment.Kind.ToString(),
            segment.Key,
            segment.LineKey,
            segment.Text,
            segment.SpeakerId,
            segment.SpeakerId != null ? names.GetValueOrDefault(segment.SpeakerId) : null,
            segment.Confidence.ToString(),
            [.. segment.Candidates.Select(c => new NarrationCandidateDto(
                c.CharacterId, names.GetValueOrDefault(c.CharacterId) ?? c.CharacterId, c.Percent))],
            segment.Direction.Key,
            segment.Direction.Source.ToString(),
            segment.Direction.Evidence,
            VoiceCast.Resolve(sheet, segment.SpeakerId, where),
            // With the speaker's standing register already added, because that
            // is what will be performed - sliders showing the line's own numbers
            // while the character is read at others is a lie the writer could
            // only catch by ear.
            new Dictionary<string, double>(
                EmotionDirector.WithRegister(
                    segment.Direction.Vector, sheet.RegisterFor(segment.SpeakerId)),
                StringComparer.Ordinal),
            segment.Direction.ReferenceClip);

    private (ChapterData Chapter, SceneData Scene)? Locate(string chapterGuid, string sceneId)
    {
        try
        {
            return _workspace.ResolveScene(chapterGuid, sceneId);
        }
        catch (InvalidOperationException)
        {
            // The chapter or scene is gone - the view is looking at something
            // that has been deleted underneath it, which is a null, not a fault.
            return null;
        }
    }

    /// <summary>The project's writing language, resolved exactly as the
    /// Inspector's scene analysis and the Dialogue view resolve it, so all
    /// three read the same lexicon.</summary>
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

/// <summary>One character in the cast, with how much they speak and the voice
/// they are read in. <see cref="VoiceId"/> is null when they are uncast, which
/// means the narrator reads their lines.</summary>
public sealed record NarrationCastMemberDto(
    string CharacterId, string Name, int LineCount, string? VoiceId);

/// <summary>The book's cast sheet.</summary>
public sealed record NarrationCastDto(
    string? NarratorVoiceId, NarrationCastMemberDto[] Members, int UnassignedCount);

/// <summary>Another character a line might belong to, with their share of the
/// evidence, carried through from the Dialogue view's own attribution.</summary>
public sealed record NarrationCandidateDto(string CharacterId, string Name, int Percent);

/// <summary>A voice that only applies over part of the book.</summary>
/// <param name="CharacterId">Empty for the narrator.</param>
/// <param name="Chapter">The chapter's guid where the app wrote it, or its title
/// where a writer did.</param>
public sealed record VoiceScopeDto(
    string CharacterId, string? Act, string? Chapter, string? Scene, string VoiceId);

/// <summary>
/// One stretch of the scene as it will be read.
/// </summary>
/// <param name="Kind">"Narration" or "Dialogue".</param>
/// <param name="Key">Stable identity inside the scene, used to direct this
/// segment or - for dialogue - to reassign its speaker.</param>
/// <param name="SpeakerId">Null for narration, and for a spoken line nobody
/// could be found for; either way the narrator reads it.</param>
/// <param name="Confidence">The <c>DialogueConfidence</c> name, localized by the
/// renderer exactly as the Dialogue view localizes it.</param>
/// <param name="DirectionSource">"Writer", "Verb", "Scene" or "None" - how the
/// direction was arrived at, so a guess never reads as a decision.</param>
/// <param name="DirectionEvidence">The speech verb behind a "Verb" direction, so
/// the view can say "angry, because you wrote snapped".</param>
/// <param name="VoiceId">The voice this segment resolves to, with the narrator
/// fallback already applied.</param>
public sealed record NarrationSegmentDto(
    int Index,
    string Kind,
    string Key,
    /// <summary>The dialogue line this utterance belongs to. A speech of three
    /// sentences is three segments sharing one of these, and a speaker or a
    /// direction the writer sets belongs to the line rather than to the breath -
    /// so this, not <c>Key</c>, is what a correction is addressed to.</summary>
    string LineKey,
    string Text,
    string? SpeakerId,
    string? SpeakerName,
    string Confidence,
    NarrationCandidateDto[] Candidates,
    string DirectionKey,
    string DirectionSource,
    string? DirectionEvidence,
    string? VoiceId,
    /// <summary>The eight dimensions this line will actually be performed at,
    /// so the direction editor opens on what is set rather than on zero.</summary>
    Dictionary<string, double> DirectionVector,
    /// <summary>The clip this line points at, when the writer said "like that".</summary>
    string? DirectionClip);


/// <summary>One scene as prose to be read: the writer's own HTML with a marker
/// round every segment, and the segments themselves.</summary>
/// <param name="Html">The scene's content, marked up by
/// <see cref="NarrationProse"/>. Read-only in the frame - narration is a place
/// to listen to the prose, not another place to edit it.</param>
public sealed record NarrationProseSceneDto(
    string ChapterGuid,
    string SceneId,
    string SceneTitle,
    string? SceneEmotion,
    int? SceneIntensity,
    string Html,
    NarrationSegmentDto[] Segments);

/// <summary>One chapter of the reading.</summary>
public sealed record NarrationChapterDto(
    string Guid, string Title, string Act, NarrationProseSceneDto[] Scenes);

/// <summary>The whole book, ready to be read aloud.</summary>
/// <param name="SpokenCount">How many segments are somebody speaking, for the
/// view to say how much of the book is dialogue without counting it again.</param>
public sealed record NarrationBookDto(NarrationChapterDto[] Chapters, int SpokenCount);
