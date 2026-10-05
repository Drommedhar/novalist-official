using System.Net;
using System.Text.RegularExpressions;
using Novalist.Core.Utilities;

namespace Novalist.Core.Services;

/// <summary>Parses stored prose into the shared blocks consumed by document writers.</summary>
internal static partial class ExportProse
{
    // ─── HTML Processing ─────────────────────────────────────────────

    /// <summary>
    /// Extract plain-text paragraphs from scene HTML content.
    /// Returns a list of paragraphs with inline formatting preserved as segments.
    /// </summary>
    internal static List<List<InlineSegment>> ParseHtmlToParagraphs(
        string html, IReadOnlyDictionary<string, string>? footnotes = null)
        => [.. ParseHtmlToBlocks(html, footnotes).Select(b => b.Segments)];

    /// <summary>
    /// A scene's content as ordered blocks, each carrying its paragraph style
    /// and whether it is a list item.
    ///
    /// Every writer parses from here rather than from raw HTML, so a style added
    /// in the editor reaches DOCX, EPUB, Markdown and LaTeX the same way instead
    /// of being honoured by whichever exporter happened to grow a case for it.
    /// </summary>
    internal static List<ExportBlock> ParseHtmlToBlocks(
        string html, IReadOnlyDictionary<string, string>? footnotes = null)
    {
        if (string.IsNullOrWhiteSpace(html)) return [];

        var blocks = new List<ExportBlock>();
        var matches = BlockRegex().Matches(html);

        if (matches.Count == 0)
        {
            // No block markup at all: the whole thing is one paragraph.
            var stripped = StripHtml(html);
            if (!string.IsNullOrWhiteSpace(stripped))
                blocks.Add(new ExportBlock(
                    [new InlineSegment { Text = stripped.Trim() }], null, ListKind.None));
            return blocks;
        }

        // A list item's kind comes from the ul/ol it sits in, which the per-item
        // match cannot see, so the enclosing tag is tracked across the loop.
        var listKind = ListKind.None;
        foreach (Match match in matches)
        {
            var tag = match.Groups["tag"].Value.ToLowerInvariant();
            if (tag is "ul" or "ol")
            {
                listKind = tag == "ul" ? ListKind.Bullet : ListKind.Number;
                continue;
            }
            if (tag is "/ul" or "/ol")
            {
                listKind = ListKind.None;
                continue;
            }

            // An image is a block of its own: the editor only ever puts one in
            // a paragraph by itself, and a writer that tried to mix it with
            // runs would have to invent a layout nobody asked for.
            var image = ImageTagRegex().Match(match.Groups["body"].Value);
            if (image.Success)
            {
                var attrs = image.Groups["attrs"].Value;
                blocks.Add(new ExportBlock([], null, ListKind.None)
                {
                    ImagePath = WebUtility.HtmlDecode(HtmlAttribute(attrs, "src")),
                    ImageAlt = WebUtility.HtmlDecode(HtmlAttribute(attrs, "alt"))
                });
                continue;
            }

            var segments = ParseInlineFormatting(match.Groups["body"].Value, footnotes);
            // A paragraph holding nothing but a footnote anchor is still
            // worth keeping - the note is the content.
            if (segments.Count == 0
                || segments.All(s => string.IsNullOrWhiteSpace(s.Text) && s.FootnoteText == null))
                continue;

            blocks.Add(tag == "li"
                // A stray li outside any list still reads as a bullet rather
                // than silently becoming body text.
                ? new ExportBlock(segments, null, listKind == ListKind.None ? ListKind.Bullet : listKind)
                : new ExportBlock(segments, ExtractStyleClass(match.Groups["attrs"].Value), ListKind.None));
        }

        return blocks;
    }

    /// <summary>
    /// Parse inline formatting (bold, italic, underline) from HTML content.
    /// </summary>
    internal static List<InlineSegment> ParseInlineFormatting(
        string html, IReadOnlyDictionary<string, string>? footnotes = null)
    {
        var segments = new List<InlineSegment>();
        ParseInlineRecursive(html, false, false, segments, footnotes);
        return segments;
    }

    private static void ParseInlineRecursive(
        string html, bool bold, bool italic, List<InlineSegment> segments,
        IReadOnlyDictionary<string, string>? footnotes = null, bool strike = false)
    {
        var pos = 0;
        while (pos < html.Length)
        {
            var tagStart = html.IndexOf('<', pos);
            if (tagStart < 0)
            {
                // Remaining text
                var text = WebUtility.HtmlDecode(html[pos..]);
                if (!string.IsNullOrEmpty(text))
                    segments.Add(new InlineSegment
                    {
                        Text = text,
                        Bold = bold,
                        Italic = italic,
                        Strike = strike
                    });
                break;
            }

            // Text before tag
            if (tagStart > pos)
            {
                var text = WebUtility.HtmlDecode(html[pos..tagStart]);
                if (!string.IsNullOrEmpty(text))
                    segments.Add(new InlineSegment
                    {
                        Text = text,
                        Bold = bold,
                        Italic = italic,
                        Strike = strike
                    });
            }

            var tagEnd = html.IndexOf('>', tagStart);
            if (tagEnd < 0) break;

            var tag = html[(tagStart + 1)..tagEnd].Trim().ToLowerInvariant();
            pos = tagEnd + 1;

            // Self-closing tags
            if (tag is "br" or "br/" or "br /")
            {
                segments.Add(new InlineSegment
                {
                    Text = "\n",
                    Bold = bold,
                    Italic = italic,
                    Strike = strike
                });
                continue;
            }

            // Skip closing tags at this level
            if (tag.StartsWith('/'))
                continue;

            // Remove attributes from tag name for matching
            var tagName = tag.Split(' ', '/')[0];

            // Find matching closing tag
            var closingTag = $"</{tagName}>";
            var closeIdx = FindMatchingCloseTag(html, pos, tagName);
            if (closeIdx < 0)
            {
                // No closing tag found, skip
                continue;
            }

            var innerContent = html[pos..closeIdx];
            pos = closeIdx + closingTag.Length;

            // A footnote anchor is not prose. Left to the default branch it
            // became a bare digit sitting in the middle of a sentence.
            if (tagName == "sup" && tag.Contains("nv-fn"))
            {
                var id = FootnoteIdRegex().Match(tag).Groups[1].Value;
                if (footnotes != null && footnotes.TryGetValue(id, out var noteText))
                    segments.Add(new InlineSegment { FootnoteText = noteText });
                continue;
            }

            switch (tagName)
            {
                case "b" or "strong":
                    ParseInlineRecursive(innerContent, true, italic, segments, footnotes, strike);
                    break;
                case "i" or "em":
                    ParseInlineRecursive(innerContent, bold, true, segments, footnotes, strike);
                    break;
                case "s" or "strike" or "del":
                    ParseInlineRecursive(innerContent, bold, italic, segments, footnotes, true);
                    break;
                case "u":
                    // Underline treated as regular text in export (no underline in most book formats)
                    ParseInlineRecursive(innerContent, bold, italic, segments, footnotes, strike);
                    break;
                case "span":
                    // Spans may carry style info but for export we just recurse.
                    // A highlight is one of them: it is a working mark the
                    // writer left themselves, not something to print.
                    ParseInlineRecursive(innerContent, bold, italic, segments, footnotes, strike);
                    break;
                default:
                    // Unknown tag - just extract text
                    ParseInlineRecursive(innerContent, bold, italic, segments, footnotes, strike);
                    break;
            }
        }
    }

    // The trailing `return -1` after the loop is compiler-required but
    // unreachable: the loop only exits by returning (depth hits 0) or via the
    // inner `nextClose < 0` return. Excluded so that dead line doesn't block 100%.
    /// <summary>
    /// The next opening tag of exactly this name.
    ///
    /// A plain IndexOf on "&lt;s" also matches "&lt;span", which made the
    /// nesting count wrong and dropped a struck phrase that happened to share a
    /// paragraph with a span - and the same for b/blockquote and i/img.
    /// </summary>
    private static int NextOpenTag(string html, int from, string openPattern)
    {
        // Unconditional: the only ways out are a match or running off the end,
        // both of which return. A bounded loop would leave an unreachable line
        // after it.
        var pos = from;
        while (true)
        {
            var at = html.IndexOf(openPattern, pos, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return -1;
            var after = at + openPattern.Length;
            if (after >= html.Length || !char.IsAsciiLetterOrDigit(html[after]))
                return at;
            pos = at + 1;
        }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static int FindMatchingCloseTag(string html, int startPos, string tagName)
    {
        var depth = 1;
        var pos = startPos;
        var openPattern = $"<{tagName}";
        var closePattern = $"</{tagName}>";

        while (pos < html.Length && depth > 0)
        {
            var nextOpen = NextOpenTag(html, pos, openPattern);
            var nextClose = html.IndexOf(closePattern, pos, StringComparison.OrdinalIgnoreCase);

            if (nextClose < 0) return -1;

            if (nextOpen >= 0 && nextOpen < nextClose)
            {
                depth++;
                pos = nextOpen + openPattern.Length;
            }
            else
            {
                depth--;
                if (depth == 0) return nextClose;
                pos = nextClose + closePattern.Length;
            }
        }

        return -1;
    }

    /// <summary>
    /// Strip all HTML tags and decode entities.
    /// </summary>
    internal static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        var text = Regex.Replace(html, "<[^>]+>", string.Empty);
        return WebUtility.HtmlDecode(text);
    }

    // ─── Normseiten (German standard pages) ──────────────────────────

    /// <summary>
    /// Turns editor HTML into Normseite blocks. Paragraphs carrying the
    /// editor's heading / subheading style become headings; everything else is
    /// body text, with a blank line between paragraphs.
    /// </summary>
    internal static List<NormseitenBlock> HtmlToNormseitenBlocks(string html)
    {
        var blocks = new List<NormseitenBlock>();
        if (string.IsNullOrWhiteSpace(html)) return blocks;

        var matches = ParagraphAnyRegex().Matches(html);
        if (matches.Count == 0)
        {
            var stripped = StripHtml(html);
            if (!string.IsNullOrWhiteSpace(stripped))
                blocks.Add(NormseitenBlock.Body(stripped));
            return blocks;
        }

        foreach (Match match in matches)
        {
            var styleId = ExtractStyleClass(match.Groups[1].Value);
            var text = string.Concat(ParseInlineFormatting(match.Groups[2].Value).Select(s => s.Text));
            if (string.IsNullOrWhiteSpace(text))
            {
                blocks.Add(NormseitenBlock.Blank());
                continue;
            }
            blocks.Add(styleId is "heading" or "subheading"
                ? NormseitenBlock.Heading(text)
                : NormseitenBlock.Body(text));
            blocks.Add(NormseitenBlock.Blank());
        }

        return blocks;
    }

    /// <summary>
    /// One attribute's value out of a tag's attribute text, whichever order the
    /// attributes are written in. Empty when the attribute is absent, which for
    /// alt text means decorative rather than undescribed.
    /// </summary>
    internal static string HtmlAttribute(string attrs, string name)
    {
        foreach (Match attr in HtmlAttributeRegex().Matches(attrs))
        {
            if (string.Equals(attr.Groups["name"].Value, name, StringComparison.OrdinalIgnoreCase))
                return attr.Groups["value"].Value;
        }
        return string.Empty;
    }

    /// <summary>The id in a <c>&lt;sup class="nv-fn" data-fn-id="..."&gt;</c> anchor.</summary>
    [GeneratedRegex(@"data-fn-id=""([^""]*)""")]
    private static partial Regex FootnoteIdRegex();

    [GeneratedRegex(@"<p[^>]*>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphRegex();

    [GeneratedRegex(@"<p[^>]*\bclass=""([^""]*)""[^>]*>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphWithClassRegex();

    /// <summary>Paragraphs and list items in document order, plus the bare
    /// <c>ul</c>/<c>ol</c> boundaries that say which kind of list an item is in.</summary>
    [GeneratedRegex(
        @"<(?<tag>p|li)(?<attrs>[^>]*)>(?<body>.*?)</\k<tag>>|<(?<tag>ul|ol)[^>]*>|<(?<tag>/ul|/ol)>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex BlockRegex();

    [GeneratedRegex(@"<img\b(?<attrs>[^>]*)>", RegexOptions.IgnoreCase)]
    internal static partial Regex ImageTagRegex();

    [GeneratedRegex(
        @"(?<name>[a-zA-Z-]+)\s*=\s*[""'](?<value>[^""']*)[""']",
        RegexOptions.IgnoreCase)]
    private static partial Regex HtmlAttributeRegex();

    [GeneratedRegex(@"<p([^>]*)>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphAnyRegex();

    internal static string? ExtractStyleClass(string attrs)
    {
        var m = Regex.Match(attrs, @"class=""([^""]*)""", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        foreach (var token in m.Groups[1].Value.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
            if (token.StartsWith("nv-style-")) return token.Substring("nv-style-".Length);
        return null;
    }
}
