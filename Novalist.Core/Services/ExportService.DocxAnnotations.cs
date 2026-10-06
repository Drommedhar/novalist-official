using System.IO.Compression;
using System.Text;
using Novalist.Core.Models;
using Novalist.Core.Utilities;
using static Novalist.Core.Services.ExportArchive;
using static Novalist.Core.Services.ExportProse;

namespace Novalist.Core.Services;

public partial class ExportService
{
    /// <summary>
    /// The <c>word/comments.xml</c> part. Ids are the comment's index rather
    /// than Novalist's GUID: the schema requires an integer, and the id only has
    /// to match the anchors in the same document.
    /// </summary>
    internal static string GenerateDocxComments(IReadOnlyList<SceneExportComment> comments)
    {
        var body = new StringBuilder();
        for (var i = 0; i < comments.Count; i++)
        {
            var comment = comments[i];
            body.Append($"<w:comment w:id=\"{i}\" w:author=\"{EscapeXml(DocxCommentAuthor)}\" ");
            body.Append($"w:initials=\"N\" w:date=\"{comment.CreatedAt.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}\">");
            foreach (var line in comment.Text.Replace("\r\n", "\n").Split('\n'))
                body.Append($"<w:p><w:r><w:t xml:space=\"preserve\">{EscapeXml(line)}</w:t></w:r></w:p>");
            body.Append("</w:comment>");
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:comments xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              {body}
            </w:comments>
            """;
    }

    /// <summary>
    /// Wraps a scene's paragraph in comment range markers when a comment's
    /// anchor text appears in it. Anchoring by text rather than by offset is
    /// deliberate: the export pipeline reflows paragraphs, so a stored offset
    /// would land in the wrong place.
    /// </summary>
    private static string WrapDocxCommentRanges(
        string paragraphXml,
        string paragraphText,
        IReadOnlyDictionary<string, int> commentIds,
        SceneExportContent scene,
        HashSet<string> alreadyAnchored)
    {
        var prefix = new StringBuilder();
        var suffix = new StringBuilder();

        foreach (var comment in scene.Comments)
        {
            if (alreadyAnchored.Contains(comment.Id))
                continue;
            if (string.IsNullOrWhiteSpace(comment.AnchorText)
                || !paragraphText.Contains(comment.AnchorText, StringComparison.Ordinal))
                continue;

            // Every scene comment is in the map: it is built from the same
            // scenes, so there is no not-found case to handle.
            var id = commentIds[comment.Id];
            alreadyAnchored.Add(comment.Id);
            prefix.Append($"<w:commentRangeStart w:id=\"{id}\"/>");
            suffix.Append($"<w:commentRangeEnd w:id=\"{id}\"/>");
            suffix.Append($"<w:r><w:commentReference w:id=\"{id}\"/></w:r>");
        }

        if (prefix.Length == 0)
            return paragraphXml;

        // The markers sit inside the paragraph, after its properties. The caller
        // always builds the paragraph with a w:pPr, so the marker is present.
        var insertAt = paragraphXml.IndexOf("</w:pPr>", StringComparison.Ordinal)
            + "</w:pPr>".Length;

        return paragraphXml[..insertAt]
            + prefix
            + paragraphXml[insertAt..].Replace("</w:p>", suffix + "</w:p>");
    }

    /// <summary>
    /// Two list definitions, a bullet and a decimal, each with the nine levels
    /// Word expects. Novalist only ever emits level zero, but a definition
    /// missing its deeper levels makes Word treat the whole part as corrupt.
    /// </summary>
    /// <summary>
    /// The <c>word/footnotes.xml</c> part. The first two entries are the
    /// separator and continuation notes Word expects to exist; the writer's own
    /// notes follow from id 2.
    /// </summary>
    private static string GenerateDocxFootnotes(List<string> notes)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < notes.Count; i++)
        {
            sb.Append($"<w:footnote w:id=\"{i + 2}\"><w:p><w:pPr><w:pStyle w:val=\"FootnoteText\"/></w:pPr>")
              .Append("<w:r><w:rPr><w:rStyle w:val=\"FootnoteReference\"/></w:rPr><w:footnoteRef/></w:r>")
              .Append($"<w:r><w:t xml:space=\"preserve\"> {EscapeXml(notes[i])}</w:t></w:r></w:p></w:footnote>");
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:footnotes xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:footnote w:id="0" w:type="separator"><w:p><w:r><w:separator/></w:r></w:p></w:footnote>
              <w:footnote w:id="1" w:type="continuationSeparator"><w:p><w:r><w:continuationSeparator/></w:r></w:p></w:footnote>
              {sb}
            </w:footnotes>
            """;
    }
}
