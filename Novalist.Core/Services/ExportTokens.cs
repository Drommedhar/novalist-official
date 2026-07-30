using System.Globalization;
using System.Text.RegularExpressions;

namespace Novalist.Core.Services;

/// <summary>
/// What a token can be resolved against: the book, and optionally the chapter
/// and scene being written at the time.
/// </summary>
public sealed record TokenContext
{
    public string Title { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string Isbn { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;
    public string Series { get; init; } = string.Empty;
    public string SeriesIndex { get; init; } = string.Empty;
    public int WordCount { get; init; }
    public int PageCount { get; init; }

    /// <summary>Number of the chapter being written, from one. Zero outside one.</summary>
    public int ChapterNumber { get; init; }
    public string ChapterTitle { get; init; } = string.Empty;
    public string SceneTitle { get; init; } = string.Empty;
    public string Act { get; init; } = string.Empty;

    /// <summary>
    /// When the export ran. Passed in rather than read from the clock so an
    /// export is reproducible and a test can assert on it.
    /// </summary>
    public DateTime ExportedAt { get; init; } = DateTime.Now;
}

/// <summary>
/// Resolves <c>&lt;$token&gt;</c> placeholders at export time.
///
/// Novalist had no token parser at all: substitution was the fixed set of
/// ExportOptions fields, so a title page could not say "Book two of the Salt
/// Road" without the writer typing it and remembering to change it, and a
/// running head could not carry the chapter title at all.
///
/// Deliberately a small set. Scrivener resolves around seventy tags and most of
/// them are for things Novalist does not have; these are the ones a title page,
/// a heading format, a separator and a running head actually need. An unknown
/// token is left exactly as written - silently deleting something a writer typed
/// is worse than printing it, and it makes a typo visible instead of invisible.
/// </summary>
public static partial class ExportTokens
{
    [GeneratedRegex(@"<\$([a-zA-Z]+)>")]
    private static partial Regex TokenRegex();

    /// <summary>Every token this resolves, for documentation and for the UI.</summary>
    public static IReadOnlyList<string> Known { get; } =
    [
        "title", "author", "isbn", "publisher", "series", "seriesindex",
        "wordcount", "pagecount", "date", "year",
        "chapternumber", "chapterroman", "chaptertitle", "scenetitle", "act"
    ];

    /// <summary>
    /// Replaces every token in <paramref name="text"/>. Tokens are matched
    /// case-insensitively, because a writer typing one in a hurry should not
    /// have to remember which half of it was capitalised.
    /// </summary>
    public static string Resolve(string? text, TokenContext context)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

        return TokenRegex().Replace(text, match =>
        {
            var value = Lookup(match.Groups[1].Value.ToLowerInvariant(), context);
            // Null means "not a token I know". Left as written, so a typo shows
            // up in the proof rather than quietly eating the words around it.
            return value ?? match.Value;
        });
    }

    private static string? Lookup(string token, TokenContext c) => token switch
    {
        "title" => c.Title,
        "author" => c.Author,
        "isbn" => c.Isbn,
        "publisher" => c.Publisher,
        "series" => c.Series,
        "seriesindex" => c.SeriesIndex,
        "wordcount" => c.WordCount.ToString("N0", CultureInfo.CurrentCulture),
        "pagecount" => c.PageCount.ToString(CultureInfo.InvariantCulture),
        "date" => c.ExportedAt.ToString("d", CultureInfo.CurrentCulture),
        "year" => c.ExportedAt.Year.ToString(CultureInfo.InvariantCulture),
        "chapternumber" => c.ChapterNumber.ToString(CultureInfo.InvariantCulture),
        "chapterroman" => Roman(c.ChapterNumber),
        "chaptertitle" => c.ChapterTitle,
        "scenetitle" => c.SceneTitle,
        "act" => c.Act,
        _ => null
    };

    private static readonly (int Value, string Numeral)[] RomanTable =
    [
        (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
        (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
        (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
    ];

    /// <summary>
    /// A chapter number in Roman numerals, which is how a great many books
    /// number their chapters and which no format string could produce.
    /// Zero and below have no numeral, so they come back empty.
    /// </summary>
    internal static string Roman(int value)
    {
        if (value <= 0) return string.Empty;

        var output = new System.Text.StringBuilder();
        foreach (var (amount, numeral) in RomanTable)
        {
            while (value >= amount)
            {
                output.Append(numeral);
                value -= amount;
            }
        }
        return output.ToString();
    }
}
