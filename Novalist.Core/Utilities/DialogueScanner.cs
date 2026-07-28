using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Novalist.Core.Utilities;

/// <summary>
/// One quoted passage found in a scene's HTML, carrying everything the Dialogue
/// view needs: the spoken text, the surrounding prose used to work out who said
/// it, and the exact HTML range to splice a rewrite back into.
/// </summary>
/// <param name="LineKey">Stable identity for the line inside its scene — the
/// hash of the normalized spoken text plus an ordinal that separates repeats of
/// the same words. Survives edits to neighbouring lines, which a plain position
/// index would not, so a speaker the writer assigned by hand stays put.</param>
/// <param name="HtmlStart">Index into the scene HTML where the spoken text
/// starts (just after the opening quote mark).</param>
/// <param name="HtmlEnd">Exclusive end of the spoken text in the scene HTML.</param>
/// <param name="Editable">False when the HTML behind the spoken text carries
/// markup (emphasis, a mention span, a footnote anchor). Rewriting such a range
/// as plain text would silently destroy that markup, so the view offers those
/// lines read-only and sends the writer to the scene instead.</param>
/// <param name="ParagraphIndex">Which paragraph of the scene the line sits in.
/// Two quotes sharing one paragraph with no speaker named between them belong to
/// the same person — the strongest signal there is for an untagged line.</param>
/// <param name="TextStart">Where the line starts in the scene's plain text, so
/// attribution can look back over the narration leading up to it.</param>
public sealed record DialogueSpan(
    string LineKey,
    string Text,
    int HtmlStart,
    int HtmlEnd,
    string ContextBefore,
    string ContextAfter,
    string HtmlBefore,
    string HtmlAfter,
    bool Editable,
    int ParagraphIndex,
    int TextStart,
    int TextEnd);

/// <summary>A scanned scene: its plain-text projection and the quoted lines
/// found in it. The text comes back with the spans so attribution can read the
/// surrounding narration without projecting the HTML a second time.</summary>
public sealed record DialogueScan(string Text, IReadOnlyList<DialogueSpan> Spans);

/// <summary>
/// Finds the quoted passages in a scene's HTML and maps each one back to the
/// byte range it occupies, so the Dialogue view can both read a character's
/// lines and write an edited one back into the scene file.
///
/// The quote pairs are the same set the Inspector's dialogue-ratio uses, so a
/// German project written with low quotes and a French one written with
/// guillemets are both recognised. Scanning runs over a plain-text projection of
/// the HTML (tags dropped, entities decoded) with a per-character index map, so
/// a match found in the text is still addressable in the original markup.
/// </summary>
public static class DialogueScanner
{
    /// <summary>Matched quote pairs across the languages Novalist ships. Shared
    /// with the Inspector's dialogue-ratio so both agree on what counts as
    /// dialogue.</summary>
    public static readonly Regex QuoteRegex = new(
        "(?:\"[^\"]*\"|“[^”]*”|„[^“]*“|«[^»]*»|»[^«]*«|‹[^›]*›|‚[^‘]*‘)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    /// <summary>How much prose either side of a quote is kept as attribution
    /// context. Long enough for "... ," she said, turning away" to survive,
    /// short enough that the next sentence's names do not leak in.</summary>
    private const int ContextWidth = 120;

    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "div", "li", "ul", "ol", "blockquote", "h1", "h2", "h3", "h4", "h5", "h6", "tr", "hr"
    };

    /// <summary>
    /// Every quoted passage in the scene, in document order. Empty and
    /// whitespace-only quotes are skipped — they carry no dialogue and would
    /// otherwise collide on the line key.
    /// </summary>
    public static IReadOnlyList<DialogueSpan> Scan(string? html) => ScanScene(html).Spans;

    /// <summary>
    /// The scene's quoted lines together with the plain text they came from.
    /// Callers that need to read the narration around a line — attribution —
    /// use this; callers that only need the lines use <see cref="Scan"/>.
    /// </summary>
    public static DialogueScan ScanScene(string? html)
    {
        if (string.IsNullOrEmpty(html))
            return new DialogueScan(string.Empty, []);

        var projection = Project(html);
        var text = projection.Text;
        if (text.Length == 0)
            return new DialogueScan(string.Empty, []);

        var spans = new List<DialogueSpan>();
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        // Paragraph boundaries are the newlines the projection wrote for block
        // tags; walking them alongside the matches keeps this a single pass.
        var paragraph = 0;
        var scanned = 0;

        foreach (Match match in QuoteRegex.Matches(text))
        {
            // The match includes both quote marks; the spoken text is what sits
            // between them. Every pair we recognise uses single-char marks.
            var innerStart = match.Index + 1;
            var innerLength = match.Length - 2;
            if (innerLength <= 0)
                continue;

            var spoken = text.Substring(innerStart, innerLength);
            if (spoken.Trim().Length == 0)
                continue;

            var htmlStart = projection.Start[innerStart];
            var htmlEnd = projection.End[innerStart + innerLength - 1];

            var (beforeStart, afterEnd) = ContextBounds(text, match.Index, match.Index + match.Length);
            var contextBefore = text[beforeStart..match.Index];
            var contextAfter = text[(match.Index + match.Length)..afterEnd];

            while (scanned < match.Index)
            {
                if (text[scanned] == '\n') paragraph++;
                scanned++;
            }

            var normalized = Normalize(spoken);
            var ordinal = ordinals.GetValueOrDefault(normalized);
            ordinals[normalized] = ordinal + 1;

            spans.Add(new DialogueSpan(
                BuildLineKey(normalized, ordinal),
                spoken.Trim(),
                htmlStart,
                htmlEnd,
                contextBefore,
                contextAfter,
                HtmlSlice(html, projection, beforeStart, match.Index),
                HtmlSlice(html, projection, match.Index + match.Length, afterEnd),
                IsLiteral(html, htmlStart, htmlEnd),
                paragraph,
                match.Index,
                match.Index + match.Length));
        }

        return new DialogueScan(text, spans);
    }

    /// <summary>
    /// Replaces one span's spoken text in the scene HTML. The caller passes the
    /// text it believes is there; a mismatch means the scene changed since the
    /// scan (edited in the editor, say) and the write is refused rather than
    /// applied to the wrong words. Returns null when the line no longer matches.
    /// </summary>
    public static string? ReplaceLine(string html, DialogueSpan span, string newText)
    {
        if (span.HtmlEnd > html.Length || !span.Editable)
            return null;

        var current = html[span.HtmlStart..span.HtmlEnd];
        if (!string.Equals(current.Trim(), span.Text, StringComparison.Ordinal))
            return null;

        // Keep whatever spacing sat inside the quote marks so "…text " does not
        // lose its trailing space when only the words changed.
        var leading = current[..(current.Length - current.TrimStart().Length)];
        var trailing = current[(current.TrimEnd().Length)..];
        var encoded = Encode(newText.Trim());
        return html[..span.HtmlStart] + leading + encoded + trailing + html[span.HtmlEnd..];
    }

    /// <summary>The line key a given spoken text would get at a given ordinal.
    /// Lets a caller follow an override across an edit that changed the words.</summary>
    public static string BuildLineKey(string normalizedText, int ordinal)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedText));
        return $"{Convert.ToHexString(hash, 0, 4).ToLowerInvariant()}:{ordinal}";
    }

    /// <summary>Casing- and spacing-insensitive form of a spoken line, so a
    /// whitespace tidy-up in the scene does not orphan a speaker override.</summary>
    public static string Normalize(string text)
        => Regex.Replace(text.Trim(), @"\s+", " ").ToLowerInvariant();

    /// <summary>Plain-text form of scene HTML with a per-character map back into
    /// the markup. Block tags become newlines so paragraph boundaries survive.</summary>
    private sealed record Projection(string Text, int[] Start, int[] End);

    private static Projection Project(string html)
    {
        var text = new StringBuilder(html.Length);
        var start = new List<int>(html.Length);
        var end = new List<int>(html.Length);

        var i = 0;
        while (i < html.Length)
        {
            var ch = html[i];
            if (ch == '<')
            {
                var close = html.IndexOf('>', i);
                if (close < 0)
                    break; // Truncated markup — nothing addressable past here.
                if (IsBlockTag(html, i, close) && text.Length > 0 && text[^1] != '\n')
                {
                    text.Append('\n');
                    start.Add(i);
                    end.Add(i);
                }
                i = close + 1;
                continue;
            }

            if (ch == '&')
            {
                var semi = html.IndexOf(';', i);
                // Bare "&" and runaway entities are just text; a real entity is short.
                if (semi > i && semi - i <= 10)
                {
                    var raw = html[i..(semi + 1)];
                    var decoded = System.Net.WebUtility.HtmlDecode(raw);
                    // Unchanged means it was never an entity — "&amp;" decodes to
                    // "&", so testing the decoded char would reject the commonest one.
                    if (decoded.Length > 0 && decoded != raw)
                    {
                        foreach (var dch in decoded)
                        {
                            text.Append(dch);
                            start.Add(i);
                            end.Add(semi + 1);
                        }
                        i = semi + 1;
                        continue;
                    }
                }
            }

            text.Append(ch);
            start.Add(i);
            end.Add(i + 1);
            i++;
        }

        return new Projection(text.ToString(), start.ToArray(), end.ToArray());
    }

    private static bool IsBlockTag(string html, int open, int close)
    {
        var inner = html[(open + 1)..close].TrimStart('/');
        var nameEnd = 0;
        while (nameEnd < inner.Length && !char.IsWhiteSpace(inner[nameEnd]) && inner[nameEnd] != '/')
            nameEnd++;
        return nameEnd > 0 && BlockTags.Contains(inner[..nameEnd]);
    }

    /// <summary>
    /// How far either side of a quote the attribution context reaches: to the
    /// paragraph edge, to the neighbouring quote mark, or to the width cap —
    /// whichever comes first. Stopping at the neighbouring quote keeps one
    /// speaker's tag from being read as the next speaker's.
    /// </summary>
    private static (int Start, int End) ContextBounds(string text, int quoteStart, int quoteEnd)
    {
        var start = Math.Max(0, quoteStart - ContextWidth);
        for (var i = quoteStart - 1; i >= start; i--)
        {
            if (text[i] == '\n' || IsQuoteMark(text[i]))
            {
                start = i + 1;
                break;
            }
        }

        var end = Math.Min(text.Length, quoteEnd + ContextWidth);
        for (var i = quoteEnd; i < end; i++)
        {
            if (text[i] == '\n' || IsQuoteMark(text[i]))
            {
                end = i;
                break;
            }
        }

        return (start, end);
    }

    private static bool IsQuoteMark(char ch)
        => ch is '"' or '“' or '”' or '„' or '«' or '»' or '‹' or '›' or '‚' or '‘';

    /// <summary>
    /// Whether the markup behind a spoken range is literal text — no tags and no
    /// character entities. Only such a range can be rewritten by swapping the
    /// characters out: a tag would be destroyed, and an entity would silently
    /// change (<c>&amp;nbsp;</c> becoming an ordinary space) on the round trip.
    /// </summary>
    private static bool IsLiteral(string html, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (html[i] is '<' or '&')
                return false;
        }
        return true;
    }

    /// <summary>
    /// The markup behind a plain-text range, used to spot the entity mention
    /// spans the editor writes into a dialogue tag. The bounds reach outward to
    /// the neighbouring characters' edges so the tags wrapping the range —
    /// including the opening <c>&lt;span data-entity-id&gt;</c> that sits before
    /// its own first letter — come along with it.
    /// </summary>
    private static string HtmlSlice(string html, Projection projection, int from, int to)
    {
        if (to <= from)
            return string.Empty;
        var htmlFrom = from > 0 ? projection.End[from - 1] : 0;
        var htmlTo = to < projection.Start.Length ? projection.Start[to] : html.Length;
        return html[htmlFrom..htmlTo];
    }

    private static string Encode(string text)
        => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
