using Novalist.Backend.Extensions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class NarrationRpc
{
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
                    html,
                    candidates,
                    new NarrationLanguageContext(dialogueLanguage, directionLanguage, utteranceLanguage),
                    new SceneNarrationSettings()
                    {
                        SpeakerOverrides = scene.DialogueSpeakers,
                        DirectionOverrides = scene.DialogueDirections,
                        Emotion = scene.AnalysisOverrides?.Emotion,
                        Intensity = scene.AnalysisOverrides?.Intensity
                    });

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
