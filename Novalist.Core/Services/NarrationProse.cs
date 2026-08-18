using System.Text;
using Novalist.Core.Utilities;

namespace Novalist.Core.Services;

/// <summary>
/// Marks a scene's own HTML up with the segments a reading is made of, so the
/// prose can be shown as the writer wrote it rather than as a list of rows
/// pulled out of it.
///
/// A reading is a thing you follow, and a list of extracted lines is not
/// followable: the paragraphing is gone, the emphasis is gone, and the writer
/// has to reconstruct where they are in their own scene from a column of
/// fragments. So the segments come to the prose instead of the prose coming to
/// the segments.
///
/// The one rule that makes this safe: <b>a marker never contains a tag.</b>
/// Every maximal run of plain text inside a segment gets its own marker, closed
/// before the next <c>&lt;</c> and reopened after the matching <c>&gt;</c>. A
/// segment spanning a paragraph break, or straddling the end of an
/// <c>&lt;em&gt;</c>, therefore produces several markers sharing one key rather
/// than one marker with mis-nested markup inside it. Wrapping the range whole
/// would emit <c>&lt;em&gt;…&lt;span&gt;…&lt;/em&gt;…&lt;/span&gt;</c>, which
/// no browser will render as written, and the scene would silently come apart.
/// </summary>
public static class NarrationProse
{
    /// <summary>The attribute the frame reads a segment's key from.</summary>
    public const string KeyAttribute = "data-nl-seg";

    /// <summary>The attribute carrying the segment's kind, so the frame can
    /// tint spoken lines without consulting the segment list.</summary>
    public const string KindAttribute = "data-nl-kind";

    /// <summary>
    /// The scene's HTML with each segment's text wrapped in marker spans.
    ///
    /// Returns the HTML untouched when there is nothing to mark: no segments, or
    /// a scene whose text the projection could not address.
    /// </summary>
    public static string Annotate(string? html, IReadOnlyList<NarrationSegment> segments)
    {
        if (string.IsNullOrEmpty(html) || segments.Count == 0)
            return html ?? string.Empty;

        var projection = DialogueScanner.ProjectScene(html);
        if (projection.Text.Length == 0)
            return html;

        // Every marker is an (html index, what to emit) pair, applied in one
        // pass over the original so no offset ever has to be adjusted for an
        // insertion made earlier.
        var opens = new List<(int At, string Tag)>();
        var closes = new List<int>();

        foreach (var segment in segments)
        {
            var from = Math.Clamp(segment.TextStart, 0, projection.Text.Length);
            var to = Math.Clamp(segment.TextEnd, from, projection.Text.Length);
            if (to <= from)
                continue;

            var tag = $"<span {KeyAttribute}=\"{Escape(segment.Key)}\" " +
                      $"{KindAttribute}=\"{segment.Kind.ToString().ToLowerInvariant()}\">";

            // Walk the segment's characters and cut a marker wherever the markup
            // does: consecutive text characters share one, and any gap between
            // them is a tag, which ends the run.
            var runStart = -1;
            var runEnd = -1;
            for (var i = from; i < to; i++)
            {
                var charStart = projection.Start[i];
                var charEnd = projection.End[i];
                // The newline the projection writes for a block tag is not a
                // character of the scene - it occupies no HTML - so it cannot
                // carry a marker and it ends whatever run it follows.
                if (charEnd <= charStart)
                {
                    Flush(opens, closes, tag, ref runStart, ref runEnd);
                    continue;
                }

                if (runStart < 0)
                {
                    runStart = charStart;
                    runEnd = charEnd;
                    continue;
                }

                if (charStart == runEnd)
                {
                    runEnd = charEnd;
                    continue;
                }

                // A gap means markup between the two characters.
                Flush(opens, closes, tag, ref runStart, ref runEnd);
                runStart = charStart;
                runEnd = charEnd;
            }

            Flush(opens, closes, tag, ref runStart, ref runEnd);
        }

        if (opens.Count == 0)
            return html;

        return Splice(html, opens, closes);
    }

    /// <summary>Records the run built so far as one marker, and clears it.</summary>
    private static void Flush(
        List<(int At, string Tag)> opens,
        List<int> closes,
        string tag,
        ref int runStart,
        ref int runEnd)
    {
        if (runStart < 0)
            return;

        opens.Add((runStart, tag));
        closes.Add(runEnd);
        runStart = -1;
        runEnd = -1;
    }

    /// <summary>
    /// Rebuilds the HTML with the markers in place.
    ///
    /// Closes are emitted before opens at the same index, so two segments that
    /// meet exactly - a quote and the tag that follows it, with no space between
    /// them - do not nest one inside the other.
    /// </summary>
    private static string Splice(string html, List<(int At, string Tag)> opens, List<int> closes)
    {
        var openAt = opens.OrderBy(o => o.At).ToArray();
        var closeAt = closes.OrderBy(c => c).ToArray();
        var builder = new StringBuilder(html.Length + opens.Count * 48);

        var o = 0;
        var c = 0;
        for (var i = 0; i <= html.Length; i++)
        {
            while (c < closeAt.Length && closeAt[c] == i)
            {
                builder.Append("</span>");
                c++;
            }

            while (o < openAt.Length && openAt[o].At == i)
            {
                builder.Append(openAt[o].Tag);
                o++;
            }

            if (i < html.Length)
                builder.Append(html[i]);
        }

        return builder.ToString();
    }

    /// <summary>A key as an attribute value. Keys are hashes and line ids, so
    /// this only ever has work to do if one day they are not.</summary>
    private static string Escape(string value)
        => value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;");
}
