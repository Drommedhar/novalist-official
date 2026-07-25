using System.Text;

namespace Novalist.Core.Utilities;

/// <summary>Kind of a source block fed into <see cref="NormseitenRenderer"/>.</summary>
public enum NormseitenBlockKind
{
    /// <summary>Document title. Rendered upper-case, no surrounding blank line.</summary>
    Title,
    /// <summary>Section heading. Rendered upper-case with a blank line before and after.</summary>
    Heading,
    /// <summary>Body paragraph.</summary>
    Text,
    /// <summary>Explicit blank line (paragraph separator).</summary>
    Blank
}

/// <summary>One logical block of a document on its way onto the Normseite grid.</summary>
public readonly record struct NormseitenBlock(NormseitenBlockKind Kind, string Text)
{
    public static NormseitenBlock Title(string text) => new(NormseitenBlockKind.Title, text);
    public static NormseitenBlock Heading(string text) => new(NormseitenBlockKind.Heading, text);
    public static NormseitenBlock Body(string text) => new(NormseitenBlockKind.Text, text);
    public static NormseitenBlock Blank() => new(NormseitenBlockKind.Blank, string.Empty);
}

/// <summary>
/// Counts for a rendered Normseiten document.
/// </summary>
/// <param name="Lines">Total grid lines, blank lines included.</param>
/// <param name="Pages">Grid pages — <paramref name="Lines"/> divided by the page height, rounded up.</param>
/// <param name="Characters">Characters of the rendered text, spaces included, line breaks excluded.</param>
/// <param name="CharacterPages">
/// Characters divided by 1500 — the character-count convention German
/// publishers and VG Wort use alongside the line grid.
/// </param>
public readonly record struct NormseitenMetrics(int Lines, int Pages, int Characters, double CharacterPages);

/// <summary>
/// Lays text out on the German "Normseite" grid: a fixed number of monospace
/// columns per line and a fixed number of lines per page, so a page count can
/// be read off the document instead of estimated.
/// </summary>
/// <remarks>
/// Wrapping is greedy and never splits a word: a word longer than the column
/// width gets a line of its own and overhangs, which is what a monospace
/// manuscript layout does. Runs of whitespace inside a paragraph collapse to a
/// single space, and consecutive blank lines collapse to one.
/// </remarks>
public static class NormseitenRenderer
{
    /// <summary>Default columns per line.</summary>
    public const int DefaultColumns = 60;

    /// <summary>Default lines per page.</summary>
    public const int DefaultLines = 30;

    /// <summary>Characters per page under the character-count convention.</summary>
    public const int CharactersPerPage = 1500;

    /// <summary>
    /// Renders blocks onto the grid, returning one string per line (an empty
    /// string is a blank line). Leading and trailing blank lines are trimmed.
    /// </summary>
    public static List<string> RenderLines(IEnumerable<NormseitenBlock> blocks, int columns = DefaultColumns)
    {
        if (columns < 1) columns = DefaultColumns;

        var output = new List<string>();
        var previousBlank = true;

        void AppendBlank()
        {
            if (previousBlank) return;
            output.Add(string.Empty);
            previousBlank = true;
        }

        foreach (var block in blocks)
        {
            var text = Normalize(block.Text);

            if (block.Kind == NormseitenBlockKind.Blank || text.Length == 0)
            {
                AppendBlank();
                continue;
            }

            switch (block.Kind)
            {
                case NormseitenBlockKind.Title:
                    output.AddRange(Wrap(text.ToUpperInvariant(), columns));
                    previousBlank = false;
                    break;
                case NormseitenBlockKind.Heading:
                    AppendBlank();
                    output.AddRange(Wrap(text.ToUpperInvariant(), columns));
                    output.Add(string.Empty);
                    previousBlank = true;
                    break;
                default:
                    output.AddRange(Wrap(text, columns));
                    previousBlank = false;
                    break;
            }
        }

        while (output.Count > 0 && output[^1].Length == 0)
            output.RemoveAt(output.Count - 1);

        return output;
    }

    /// <summary>Counts lines, pages and characters for already-rendered lines.</summary>
    public static NormseitenMetrics Measure(IReadOnlyList<string> lines, int linesPerPage = DefaultLines)
    {
        if (linesPerPage < 1) linesPerPage = DefaultLines;
        var characters = lines.Sum(l => l.Length);
        var pages = (lines.Count + linesPerPage - 1) / linesPerPage;
        return new NormseitenMetrics(lines.Count, pages, characters, (double)characters / CharactersPerPage);
    }

    /// <summary>Renders and measures in one step.</summary>
    public static NormseitenMetrics MeasureBlocks(
        IEnumerable<NormseitenBlock> blocks,
        int columns = DefaultColumns,
        int linesPerPage = DefaultLines)
        => Measure(RenderLines(blocks, columns), linesPerPage);

    /// <summary>
    /// Collapses whitespace runs to single spaces and trims the result, so a
    /// paragraph that arrived with newlines or tabs still wraps cleanly.
    /// </summary>
    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var sb = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (sb.Length > 0) pendingSpace = true;
                continue;
            }
            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Greedy word wrap that never breaks a word.</summary>
    private static List<string> Wrap(string text, int columns)
    {
        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length == 0)
            {
                current.Append(word);
                continue;
            }
            if (current.Length + 1 + word.Length <= columns)
            {
                current.Append(' ').Append(word);
                continue;
            }
            lines.Add(current.ToString());
            current.Clear().Append(word);
        }
        lines.Add(current.ToString());
        return lines;
    }
}
