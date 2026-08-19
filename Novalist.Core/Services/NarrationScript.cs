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
/// speaker the writer set survives an edit to a neighbouring paragraph. One
/// per utterance: a speech of three sentences is three of these, because that
/// is what gets spoken, cached and highlighted.</param>
/// <param name="LineKey">The dialogue line this utterance belongs to - the
/// Dialogue view's own key, which is what lets a correction made here show up
/// there. Several utterances of one speech share it, because a speaker and a
/// direction belong to the line the writer wrote and not to the breath the
/// model happens to take. On narration it is the segment's own key.</param>
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
    string LineKey,
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
/// <summary>
/// What a splitter has to know about a language to tell a sentence ending from
/// a full stop that is merely a full stop.
/// </summary>
/// <param name="Abbreviations">Words that take a point without ending anything.
/// Lower-cased and without their point.</param>
/// <param name="OrdinalPoint">True where the language writes ordinals with a
/// point after the digits, as German does.</param>
public sealed record UtteranceLanguage(
    IReadOnlySet<string> Abbreviations, bool OrdinalPoint)
{
    /// <summary>What a language with no analysis pack gets: the rules that need
    /// no vocabulary. An unknown language still gets initials, decimals and the
    /// lower-case lookahead, which is most of the benefit.</summary>
    public static readonly UtteranceLanguage None =
        new(new HashSet<string>(StringComparer.OrdinalIgnoreCase), false);

    /// <summary>The splitting rules a scene-analysis pack carries.</summary>
    public static UtteranceLanguage From(SceneAnalysisLexicon? lexicon)
        => lexicon == null
            ? None
            : new UtteranceLanguage(
                lexicon.Abbreviations.ToHashSet(StringComparer.OrdinalIgnoreCase),
                lexicon.OrdinalPoint);
}

public static class NarrationScript
{
    /// <summary>Marks a narration segment's key, so it can never collide with a
    /// dialogue line key from the same scene.</summary>
    private const string NarrationKeyPrefix = "n:";

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// The largest number a full stop after it can still be an ordinal rather
    /// than a sentence ending.
    ///
    /// German writes <c>3. Mai</c> and <c>1. Kapitel</c>, so a point after a
    /// number followed by a capital is ambiguous in a way no surrounding
    /// evidence resolves. Days and chapter numbers are small and years are not,
    /// which is the whole of the heuristic: <c>Am 3. Mai</c> reads on, and
    /// <c>im Jahr 1997.</c> ends.
    /// </summary>
    private const int LargestOrdinal = 31;

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
    /// <summary>
    /// The opening and closing marks of every quote style the scanner knows.
    ///
    /// A span's range covers the marks and its text does not, which is right for
    /// the highlight and right for the reading. Splitting a speech into
    /// utterances needs the range without them, so they are trimmed off rather
    /// than assumed to be one character each.
    /// </summary>
    private static readonly char[] QuoteMarks =
        ['"', '\u201c', '\u201d', '\u201e', '\u00ab', '\u00bb', '\u2039', '\u203a',
         '\u201a', '\u2018', '\u2019', '\u300c', '\u300d', '\u300e', '\u300f'];

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
    internal static IEnumerable<(int Start, int End)> Utterances(
        string text, int start, int end, UtteranceLanguage? language = null)
    {
        language ??= UtteranceLanguage.None;
        var from = start;
        var at = start;

        while (at < end)
        {
            var character = text[at];
            var stop = at;
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
                // A full stop is the only terminator that is often not one.
                // "The bell rang at 10 a.m. sharp." has three of them and one
                // sentence, and cutting at every one produced three things for
                // a model to say - which is not one breath, it is a stutter,
                // and it fired on any prose carrying a title, an initial, a
                // decimal or a time.
                if (character == '.' && !EndsSentence(text, from, stop, at, end, language))
                    continue;

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
    /// Whether the full stop at <paramref name="stop"/> actually ends a
    /// sentence.
    ///
    /// Four things say it does not, cheapest first: an initial, a known
    /// abbreviation, a number the point belongs to, and a lower-case word after
    /// it. The last needs no vocabulary at all, which is why a language with no
    /// analysis pack still gets most of the benefit.
    /// </summary>
    /// <param name="from">Where the utterance being built started.</param>
    /// <param name="stop">The index of the full stop itself.</param>
    /// <param name="after">The first index past the stop and its trailing
    /// marks.</param>
    private static bool EndsSentence(
        string text, int from, int stop, int after, int end, UtteranceLanguage language)
    {
        // The word the point is attached to, taken back to the last space.
        // Points are part of it, so "a.m" is one token rather than two.
        var wordStart = stop;
        while (wordStart > from
               && (char.IsLetterOrDigit(text[wordStart - 1]) || text[wordStart - 1] == '.'))
        {
            wordStart--;
        }
        var word = text[wordStart..stop];

        // A single letter before a point is an initial: J. R. R. Tolkien.
        if (word.Length == 1 && char.IsLetter(word[0]))
            return false;

        if (word.Length > 0 && language.Abbreviations.Contains(word.TrimEnd('.')))
            return false;

        // What comes next, ignoring the spaces between.
        var next = after;
        while (next < end && char.IsWhiteSpace(text[next]))
            next++;
        var following = next < end ? text[next] : '\0';

        if (word.Length > 0 && char.IsDigit(word[^1]))
        {
            // A decimal or a thousands separator: the point is inside the
            // number and the number carries straight on after it.
            if (after == stop + 1 && next == after && char.IsDigit(following))
                return false;

            // An ordinal, in a language that writes them with a point.
            if (language.OrdinalPoint && char.IsLetter(following)
                && int.TryParse(
                    word.AsSpan(word.LastIndexOf('.') + 1),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var ordinal)
                && ordinal <= LargestOrdinal)
            {
                return false;
            }
        }

        // A sentence starts with a capital in every language that draws the
        // distinction, so something lower-case after the point means the point
        // was an abbreviation we do not know about - which is the safety net
        // under the list, and the reason a language with no analysis pack still
        // gets most of this.
        //
        // Only for a lone stop, though. A run of them - "She waited... and then
        // she left." - is an ending the writer put there deliberately, and it
        // is worth a breath even where the words carry on in lower case.
        var run = stop + 1 < end && Array.IndexOf(Terminators, text[stop + 1]) >= 0;
        return run || !char.IsLower(following);
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
        int? sceneIntensity,
        UtteranceLanguage? utterances = null)
    {
        utterances ??= UtteranceLanguage.None;
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
            foreach (var (from, to) in Utterances(text, start, end, utterances))
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
                // Narration is nobody's line, so it is its own.
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

            // A speech is cut into sentences too, for exactly the reasons
            // narration is. This was the half the fix missed: the gap between
            // quotes became one utterance per sentence while the quote itself
            // stayed whole however long it ran - and a speech longer than the
            // model will say in one go came back cut off mid-word, with the
            // clip written and its duration reported as though nothing had
            // happened.
            //
            // The marks are trimmed off the range before it is cut, so an
            // opening quote never begins an utterance of its own.
            var inner = Inside(text, span.TextStart, span.TextEnd);
            var direction = EmotionDirector.Resolve(
                Override(directionOverrides, span.LineKey),
                span.ContextAfter,
                span.ContextBefore,
                sceneEmotion,
                sceneIntensity,
                directions);

            var pieces = Utterances(text, inner.Start, inner.End, utterances).ToArray();
            // A line the scanner found but the splitter cannot address - a
            // quote with nothing inside it - is still the writer's line, and is
            // carried through as it always was rather than disappearing.
            if (pieces.Length == 0)
                pieces = [(inner.Start, inner.End)];

            for (var piece = 0; piece < pieces.Length; piece++)
            {
                var (from, to) = pieces[piece];
                segments.Add(new NarrationSegment(
                    segments.Count,
                    NarrationSegmentKind.Dialogue,
                    // The first utterance keeps the line's own key, so every
                    // direction and every clip a writer already has stays
                    // addressed to the same thing. Only a speech that actually
                    // splits gains keys that did not exist before.
                    piece == 0 ? span.LineKey : $"{span.LineKey}~{piece}",
                    span.LineKey,
                    Clean(text[from..to]),
                    attribution.CharacterId,
                    attribution.Confidence,
                    attribution.Candidates,
                    // One direction for the whole line. A writer directs what
                    // they wrote, not the breaths a model takes through it.
                    direction,
                    // The marks belong to the line, so the first utterance is
                    // highlighted from the opening quote and the last one to
                    // the closing one - the tint the writer sees is unchanged.
                    piece == 0 ? span.TextStart : from,
                    piece == pieces.Length - 1 ? span.TextEnd : to));
            }
        }

        AddNarration(cursor, text.Length);
        return segments;
    }

    /// <summary>
    /// A quoted range with its marks taken off, so what is split is what is
    /// spoken.
    /// </summary>
    private static (int Start, int End) Inside(string text, int start, int end)
    {
        while (start < end && Array.IndexOf(QuoteMarks, text[start]) >= 0)
            start++;
        while (end > start && Array.IndexOf(QuoteMarks, text[end - 1]) >= 0)
            end--;
        return (start, end);
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
