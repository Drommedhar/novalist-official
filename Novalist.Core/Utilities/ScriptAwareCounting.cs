using System.Text.RegularExpressions;

namespace Novalist.Core.Utilities;

/// <summary>
/// Counting and timing text in scripts that do not put spaces between words.
///
/// Novalist ships zh-CN as a bundled interface and analysis language and counted
/// every script the same way: runs of letters separated by spaces. A Chinese
/// scene of five hundred characters came out as a handful of "words", because
/// the whole run matched once - so the word count, the daily goal, every target
/// and the reading time were all wrong for a language the app claims to support.
///
/// The convention publishers use for Chinese, Japanese and Korean is one
/// character, one unit, and that is what this counts. Thai is different again:
/// it has no spaces and no per-character convention, so its runs are divided by
/// an average word length. That is an approximation and is marked as one -
/// better than counting a whole Thai sentence as a single word.
/// </summary>
public static partial class ScriptAwareCounting
{
    /// <summary>
    /// Average characters per Thai word. Thai needs a dictionary to segment
    /// properly and .NET ships no word breaker for it; five is the figure Thai
    /// corpora settle around and is a great deal closer than one.
    /// </summary>
    internal const double ThaiCharsPerWord = 5;

    /// <summary>Reading speed for space-delimited scripts, in words a minute.</summary>
    public const int WordsPerMinute = 200;

    /// <summary>
    /// Reading speed for Chinese, Japanese and Korean, in characters a minute.
    ///
    /// Reading these is measured in characters because a character carries far
    /// more than a Latin one; timing them at a words-a-minute rate reports a
    /// Chinese chapter as several times longer than it takes to read.
    /// </summary>
    public const int CharactersPerMinute = 500;

    /// <summary>Han, kana, and hangul: one character, one unit.</summary>
    [GeneratedRegex(@"[\p{IsCJKUnifiedIdeographs}\p{IsCJKUnifiedIdeographsExtensionA}"
        + @"\p{IsHiragana}\p{IsKatakana}\p{IsHangulSyllables}\p{IsHangulJamo}]",
        RegexOptions.CultureInvariant)]
    private static partial Regex CjkRegex();

    [GeneratedRegex(@"\p{IsThai}", RegexOptions.CultureInvariant)]
    private static partial Regex ThaiRegex();

    /// <summary>Words in space-delimited scripts, which is what the app has
    /// always counted and still counts for those.</summary>
    [GeneratedRegex(@"[\p{L}\p{N}]+(?:['’-][\p{L}\p{N}]+)*", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    /// <summary>
    /// How many words this text is worth, whatever script it is in.
    ///
    /// Mixed text is counted correctly by construction: the CJK and Thai
    /// characters are removed before the space-delimited pass, so a Chinese
    /// sentence with an English name in it counts the name once and each Chinese
    /// character once, rather than counting the run as one word or twice.
    /// </summary>
    public static int Count(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        var cjk = CjkRegex().Matches(text).Count;
        var thai = ThaiRegex().Matches(text).Count;

        // The rest, with the non-spacing scripts taken out so they are not
        // counted a second time as runs of letters.
        var rest = ThaiRegex().Replace(CjkRegex().Replace(text, " "), " ");
        var words = WordRegex().Matches(rest).Count;

        return words + cjk + (int)Math.Ceiling(thai / ThaiCharsPerWord);
    }

    /// <summary>
    /// Minutes to read this text, timed by the script it is mostly in.
    ///
    /// Chinese, Japanese and Korean are timed in characters a minute; everything
    /// else in words a minute. A Chinese chapter timed at a words-a-minute rate
    /// reads as several times longer than it takes.
    /// </summary>
    public static int ReadingMinutes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        var cjk = CjkRegex().Matches(text).Count;
        var rest = CjkRegex().Replace(text, " ");
        var words = WordRegex().Matches(rest).Count
            + (int)Math.Ceiling(ThaiRegex().Matches(rest).Count / ThaiCharsPerWord);

        var minutes = (cjk / (double)CharactersPerMinute) + (words / (double)WordsPerMinute);
        // Anything with text in it takes at least a minute to read: reporting
        // zero for a page of prose is worse than rounding up.
        return minutes <= 0 ? 0 : Math.Max(1, (int)Math.Ceiling(minutes));
    }

    /// <summary>
    /// Whether this text is mostly a script with no spaces between words. Used
    /// where a figure only means something for one or the other - a
    /// words-per-sentence average over Chinese says nothing.
    /// </summary>
    public static bool IsNonSpacing(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var nonSpacing = CjkRegex().Matches(text).Count + ThaiRegex().Matches(text).Count;
        var letters = text.Count(char.IsLetter);
        return letters > 0 && nonSpacing > letters / 2;
    }
}
