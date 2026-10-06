using System.Text.RegularExpressions;
using System.Text;
using Novalist.Core.Models;
using XBrushes = PdfSharpCore.Drawing.XBrushes;
using XFont = PdfSharpCore.Drawing.XFont;
using XFontStyle = PdfSharpCore.Drawing.XFontStyle;
using XGraphics = PdfSharpCore.Drawing.XGraphics;
using XImage = PdfSharpCore.Drawing.XImage;
using XPoint = PdfSharpCore.Drawing.XPoint;
using XUnit = PdfSharpCore.Drawing.XUnit;
using static Novalist.Core.Services.ExportProse;

namespace Novalist.Core.Services;

public partial class ExportService
{
    /// <summary>One logical line of codex prose after markdown-ish parsing.</summary>
    internal sealed class CodexProseLine
    {
        public bool Heading { get; init; }
        public bool Bullet { get; init; }
        public List<InlineSegment> Segments { get; init; } = [];
    }

    /// <summary>
    /// Turns stored entity prose — editor HTML, markdown, or plain text — into
    /// lines a PDF page can lay out: block tags and newlines end a line, stray
    /// control characters are dropped (they render as boxes), and leading
    /// <c>#</c> / <c>*</c> markers plus <c>**bold**</c> spans become styling
    /// instead of literal text.
    /// </summary>
    internal static List<CodexProseLine> ParseCodexProse(string content)
    {
        var text = StripHtml(BlockTagRegex().Replace(content, "\n"))
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');
        // Tabs and friends become spaces; every other control character is
        // dropped, since a PDF renders it as a box glyph.
        text = new string(text
            .Select(c => c != '\n' && char.IsWhiteSpace(c) ? ' ' : c)
            .Where(c => c == '\n' || !char.IsControl(c))
            .ToArray());

        var lines = new List<CodexProseLine>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                lines.Add(new CodexProseLine());
                continue;
            }

            var heading = MarkdownHeadingRegex().IsMatch(line);
            if (heading) line = MarkdownHeadingRegex().Replace(line, string.Empty);
            var bullet = !heading && MarkdownBulletRegex().IsMatch(line);
            if (bullet) line = MarkdownBulletRegex().Replace(line, string.Empty);

            lines.Add(new CodexProseLine
            {
                Heading = heading,
                Bullet = bullet,
                Segments = ParseInlineMarkdown(line)
            });
        }
        return lines;
    }

    /// <summary>Splits a line into runs, toggling bold on <c>**</c> markers.</summary>
    internal static List<InlineSegment> ParseInlineMarkdown(string line)
    {
        var segments = new List<InlineSegment>();
        var buffer = new StringBuilder();
        var bold = false;

        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '*' && i + 1 < line.Length && line[i + 1] == '*')
            {
                if (buffer.Length > 0)
                {
                    segments.Add(new InlineSegment { Text = buffer.ToString(), Bold = bold });
                    buffer.Clear();
                }
                bold = !bold;
                i++;
                continue;
            }
            buffer.Append(line[i]);
        }

        if (buffer.Length > 0)
            segments.Add(new InlineSegment { Text = buffer.ToString(), Bold = bold });
        return segments;
    }

    /// <summary>
    /// Splits a word too wide for a whole line (a long URL, or prose written
    /// without spaces) into chunks that fit.
    /// </summary>
    private static IEnumerable<string> HardBreak(string word, XFont font, XGraphics gfx, double maxWidth)
    {
        var start = 0;
        while (start < word.Length)
        {
            var take = 1;
            while (start + take < word.Length &&
                   gfx.MeasureString(word.Substring(start, take + 1), font).Width <= maxWidth)
                take++;
            yield return word.Substring(start, take);
            start += take;
        }
    }

    [GeneratedRegex(@"<br\s*/?>|</(?:p|div|li|tr|h[1-6])\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTagRegex();

    [GeneratedRegex(@"^#{1,6}\s+")]
    private static partial Regex MarkdownHeadingRegex();

    [GeneratedRegex(@"^[-*+]\s+")]
    private static partial Regex MarkdownBulletRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRunRegex();
}
