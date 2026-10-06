using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Novalist.Core.Models;
using Novalist.Core.Utilities;

namespace Novalist.Core.Services;

public static partial class NarrationScript
{
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
}
