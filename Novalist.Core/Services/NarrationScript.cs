using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Novalist.Core.Models;
using Novalist.Core.Utilities;

namespace Novalist.Core.Services;

/// <summary>Whether a segment is somebody speaking or the prose around it.</summary>
public enum NarrationSegmentKind
{
    /// <summary>Everything outside the quote marks, including the dialogue tag.
    /// Read by the book's narrator.</summary>
    Narration,

    /// <summary>The words inside one pair of quote marks.</summary>
    Dialogue
}

/// <summary>
/// One stretch of a scene as it will be read aloud.
/// </summary>
/// <param name="Key">Stable identity inside the scene, so a direction or a
/// speaker the writer set survives an edit to a neighbouring paragraph.
/// Dialogue segments carry the Dialogue view's own line key, which is what lets
/// a correction made here show up there.</param>
/// <param name="SpeakerId">The character speaking, or null for the narrator.
/// Null on a dialogue segment means nobody could be worked out - the line is
/// still read, by the narrator, rather than skipped.</param>
/// <param name="TextStart">Where this segment begins in the scene's plain text.
/// Kept so the prose can be marked up where it stands: a reading shown as the
/// writer's own paragraphs, rather than as a list of extracted rows, needs every
/// segment addressable in the scene it came from.</param>
/// <param name="TextEnd">Exclusive end of the segment in the scene's plain
/// text.</param>
public sealed record NarrationSegment(
    int Index,
    NarrationSegmentKind Kind,
    string Key,
    string Text,
    string? SpeakerId,
    DialogueConfidence Confidence,
    IReadOnlyList<DialogueCandidate> Candidates,
    VoiceDirection Direction,
    int TextStart,
    int TextEnd);

/// <summary>One scene, cast and directed, ready to be spoken.</summary>
public sealed record NarrationSceneScript(
    string ChapterGuid,
    string SceneId,
    string ChapterTitle,
    string SceneTitle,
    string? SceneEmotion,
    int? SceneIntensity,
    IReadOnlyList<NarrationSegment> Segments);

/// <summary>
/// Turns a scene into the ordered run of segments a reading is made of.
///
/// The quoted passages come from <see cref="DialogueScanner"/> and their
/// speakers from <see cref="DialogueAttributor"/> - the same two the Dialogue
/// view uses, so there is one idea in the app of who says what and correcting
/// it in either place corrects it in both. What is new here is everything
/// *between* the quotes: the scanner already reports each span's position in
/// the scene's plain text, so the gaps are a subtraction.
///
/// The gaps matter more than they sound. In
/// <c>"Get out," she said, not turning round.</c> the quoted half is the
/// character and the tag is the narrator, and reading the tag in the
/// character's voice is the single most obvious way a performed reading gives
/// itself away as a machine.
/// </summary>
public static class NarrationScript
{
    /// <summary>Marks a narration segment's key, so it can never collide with a
    /// dialogue line key from the same scene.</summary>
    private const string NarrationKeyPrefix = "n:";

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Builds one scene's script.
    /// </summary>
    /// <param name="html">The scene's content.</param>
    /// <param name="candidates">The cast, precompiled by
    /// <see cref="DialogueAttributor.BuildCandidates"/>.</param>
    /// <param name="speakerOverrides">The writer's own speaker assignments -
    /// <see cref="SceneData.DialogueSpeakers"/>.</param>
    /// <param name="directionOverrides">The writer's own directions -
    /// <see cref="SceneData.DialogueDirections"/>.</param>
    public static IReadOnlyList<NarrationSegment> Build(
        string? html,
        IReadOnlyList<DialogueSpeakerCandidate> candidates,
        DialogueLanguage language,
        DirectionLanguage directions,
        IReadOnlyDictionary<string, string>? speakerOverrides,
        IReadOnlyDictionary<string, string>? directionOverrides,
        string? sceneEmotion,
        int? sceneIntensity)
    {
        var (text, spans) = DialogueScanner.ScanScene(html);
        if (text.Length == 0)
            return [];

        var attributions = spans.Count == 0
            ? []
            : DialogueAttributor.Attribute(spans, text, candidates, language, speakerOverrides);

        var segments = new List<NarrationSegment>(spans.Count * 2 + 1);
        // Narration runs are keyed by their own words, the same way dialogue is,
        // so two identical stretches of prose in one scene stay distinguishable.
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        var cursor = 0;

        void AddNarration(int start, int end)
        {
            if (end <= start)
                return;

            var raw = Clean(text[start..end]);
            if (raw.Length == 0)
                return;

            var key = NarrationKey(raw, ordinals);
            segments.Add(new NarrationSegment(
                segments.Count,
                NarrationSegmentKind.Narration,
                key,
                raw,
                SpeakerId: null,
                DialogueConfidence.None,
                [],
                // Narration is never tagged by a speech verb of its own - the
                // verb in "she snapped" directs the line it introduces, not the
                // introducing. So the scene is the only evidence, held back so
                // the prose is coloured rather than acted.
                EmotionDirector.Resolve(
                    Override(directionOverrides, key),
                    contextAfter: null,
                    contextBefore: null,
                    sceneEmotion,
                    sceneIntensity,
                    directions,
                    EmotionDirector.NarrationMagnitude),
                start,
                end));
        }

        for (var i = 0; i < spans.Count; i++)
        {
            var span = spans[i];
            AddNarration(cursor, span.TextStart);
            cursor = span.TextEnd;

            var attribution = attributions[i];
            segments.Add(new NarrationSegment(
                segments.Count,
                NarrationSegmentKind.Dialogue,
                span.LineKey,
                Clean(span.Text),
                attribution.CharacterId,
                attribution.Confidence,
                attribution.Candidates,
                EmotionDirector.Resolve(
                    Override(directionOverrides, span.LineKey),
                    span.ContextAfter,
                    span.ContextBefore,
                    sceneEmotion,
                    sceneIntensity,
                    directions),
                span.TextStart,
                span.TextEnd));
        }

        AddNarration(cursor, text.Length);
        return segments;
    }

    /// <summary>The writer's direction for a segment, or null where they set
    /// none. A stored blank is not nothing - it is the writer saying "read this
    /// plainly" - so it is passed through rather than treated as absent.</summary>
    private static string? Override(IReadOnlyDictionary<string, string>? overrides, string key)
        => overrides != null && overrides.TryGetValue(key, out var value) ? value : null;

    /// <summary>
    /// A stretch of the scene's plain text as one speakable line: the newlines
    /// the projection wrote for block tags collapsed back into spaces, because a
    /// paragraph break inside a narration run is a pause, not a new segment.
    /// </summary>
    private static string Clean(string raw) => Whitespace.Replace(raw, " ").Trim();

    /// <summary>Identity for a narration run, on the same content-hash scheme
    /// dialogue lines use so neither is positional and both survive an edit
    /// elsewhere in the scene.</summary>
    private static string NarrationKey(string text, Dictionary<string, int> ordinals)
    {
        var normalized = DialogueScanner.Normalize(text);
        var ordinal = ordinals.GetValueOrDefault(normalized);
        ordinals[normalized] = ordinal + 1;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return $"{NarrationKeyPrefix}{Convert.ToHexString(hash, 0, 4).ToLowerInvariant()}:{ordinal}";
    }
}
