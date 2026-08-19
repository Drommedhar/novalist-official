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
    /// What ends a sentence, in every writing system the app ships an analysis
    /// pack for. The full-width stops matter: a Chinese scene has no full stop
    /// in it at all and would come back as one unbroken utterance.
    /// </summary>
    private static readonly char[] Terminators =
        ['.', '!', '?', '\u2026', '\u3002', '\uFF01', '\uFF1F'];

    /// <summary>
    /// Characters that belong to the sentence they follow - a closing quote or
    /// bracket after the stop. Breaking before them would leave a stray mark
    /// opening the next utterance.
    /// </summary>
    private static readonly char[] Trailing =
        ['"', '\'', '\u201d', '\u2019', '\u00bb', '\u203a', ')', ']', '\u300d', '\u300f'];

    /// <summary>
    /// The longest an utterance may run without a sentence ending in it.
    ///
    /// Speech models are built to say a sentence, not a page, and degrade or
    /// truncate well before this. Prose that runs on - a paragraph of stream of
    /// consciousness with no full stop - is broken at a word boundary rather
    /// than sent whole.
    /// </summary>
    private const int LongestUtterance = 300;

    /// <summary>
    /// Cuts a stretch of narration into things a voice would say in one breath.
    ///
    /// Sentence endings and paragraph breaks, which the scene's plain text
    /// carries as newlines. Ranges are in the text's own coordinates, because
    /// the prose is marked up where it stands and an offset that has drifted
    /// puts the highlight on the wrong words.
    /// </summary>
    internal static IEnumerable<(int Start, int End)> Utterances(string text, int start, int end)
    {
        var from = start;
        var at = start;

        while (at < end)
        {
            var character = text[at];
            at++;

            if (character == '\n')
            {
                // A paragraph break is always a break, and is never spoken.
                if (Trim(text, from, at - 1) is { } paragraph)
                    yield return paragraph;
                from = at;
                continue;
            }

            if (Array.IndexOf(Terminators, character) >= 0)
            {
                // Take the closing marks and the run of stops with it: "..." and
                // "?!" are one ending, not three.
                while (at < end
                       && (Array.IndexOf(Terminators, text[at]) >= 0
                           || Array.IndexOf(Trailing, text[at]) >= 0))
                {
                    at++;
                }
                if (Trim(text, from, at) is { } sentence)
                    yield return sentence;
                from = at;
                continue;
            }

            // Nothing has ended this in far too long. Break at the last word
            // boundary rather than mid-word.
            if (at - from >= LongestUtterance)
            {
                var breakAt = text.LastIndexOf(' ', at - 1, at - from);
                if (breakAt <= from)
                    breakAt = at;
                if (Trim(text, from, breakAt) is { } piece)
                    yield return piece;
                from = breakAt;
                at = breakAt;
                // Step past the space, so it does not open the next utterance.
                while (at < end && text[at] == ' ')
                    at++;
                from = at;
            }
        }

        if (Trim(text, from, end) is { } last)
            yield return last;
    }

    /// <summary>
    /// A range with the whitespace taken off both ends, or null when there is
    /// nothing but whitespace in it.
    ///
    /// The range is what the prose is marked up by, so a leading space would be
    /// highlighted as though it were part of the sentence being spoken.
    /// </summary>
    private static (int Start, int End)? Trim(string text, int start, int end)
    {
        while (start < end && char.IsWhiteSpace(text[start]))
            start++;
        while (end > start && char.IsWhiteSpace(text[end - 1]))
            end--;
        return end > start ? (start, end) : null;
    }

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

        // One utterance per sentence, rather than one per gap between quotes.
        //
        // The gap between two quoted lines is not a unit of speech - in a scene
        // with little dialogue it is the entire scene. That was handed to the
        // engine as a single utterance: thirty seconds of audio from one call,
        // minutes of waiting before the first sound, and a model asked for far
        // more than it is built to say in one breath. It is also the wrong unit
        // to follow along: highlighting a whole scene tells the writer nothing
        // about where the voice is.
        void AddNarration(int start, int end)
        {
            foreach (var (from, to) in Utterances(text, start, end))
                AddUtterance(from, to);
        }

        // Every range Utterances yields is non-empty and already trimmed, so
        // there is nothing to re-check here.
        void AddUtterance(int start, int end)
        {
            var raw = Clean(text[start..end]);
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
