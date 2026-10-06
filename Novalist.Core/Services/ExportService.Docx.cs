using System.IO.Compression;
using System.Text;
using Novalist.Core.Models;
using Novalist.Core.Utilities;
using static Novalist.Core.Services.ExportArchive;
using static Novalist.Core.Services.ExportProse;

namespace Novalist.Core.Services;

public partial class ExportService
{
    // ─── DOCX Export ─────────────────────────────────────────────────

    private static async Task ExportToDocxAsync(
        List<ChapterExportContent> chapters,
        ExportOptions options,
        string outputPath)
    {
        if (options.ResolvePreset().NormseitenGrid)
        {
            await WriteNormseitenDocxAsync(BuildManuscriptBlocks(chapters, options), options, outputPath);
            return;
        }

        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        var smf = options.ResolvePreset().ShunnHeader;

        // Comments travel with the scenes so an editor opening the file sees
        // them as Word comments they can reply to, rather than as inline prose.
        var exportComments = chapters
            .SelectMany(c => c.Scenes)
            .SelectMany(sc => sc.Comments)
            .ToList();
        var hasComments = exportComments.Count > 0;
        // Word keys comment parts by integer id; this maps Novalist's GUIDs onto
        // the position each comment occupies in the document-wide list.
        var commentIds = exportComments
            .Select((c, i) => (c.Id, i))
            .ToDictionary(pair => pair.Id, pair => pair.i, StringComparer.Ordinal);

        // Prose images: one media part each, with a relationship the drawing
        // runs point at. Collected before the package parts so both the content
        // types and the relationships can declare them.
        var proseImages = CollectProseImages(chapters);
        var imageRels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var imageParts = new List<(string Part, string Absolute)>();
        foreach (var (absolute, _) in proseImages)
        {
            var ext = Path.GetExtension(absolute).ToLowerInvariant().TrimStart('.');
            var part = $"media/image-{imageParts.Count + 1}.{ext}";
            imageRels[absolute] = $"rIdImage{imageParts.Count + 1}";
            imageParts.Add((part, absolute));
        }

        var imageExtensions = imageParts
            .Select(p => Path.GetExtension(p.Part).TrimStart('.').ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(ext => $"\n  <Default Extension=\"{ext}\" ContentType=\"{DocxImageContentType(ext)}\"/>")
            .ToList();

        await WriteDocxPackageLinksAsync(zip, smf, hasComments, imageExtensions, imageParts);

        foreach (var (part, absolute) in imageParts)
            await WriteBinaryEntryAsync(zip, $"word/{part}", absolute);

        // word/styles.xml
        await WriteEntryAsync(zip, "word/styles.xml", GenerateDocxStyles(options));
        // Real Word list numbering rather than a literal bullet character typed
        // into the text, so an editor can renumber and restyle the list.
        await WriteEntryAsync(zip, "word/numbering.xml", GenerateDocxNumbering());

        if (hasComments)
            await WriteEntryAsync(zip, "word/comments.xml", GenerateDocxComments(exportComments));

        await WriteDocxHeaderAsync(zip, smf, options);

        var (body, footnoteDefs) = BuildDocxBody(chapters, options, imageRels, commentIds);

        await WriteDocxDocumentAsync(zip, smf, body, footnoteDefs);
    }

    /// <summary>
    /// One matter page as DOCX paragraphs. Kinds without a heading render as
    /// body text only, which is how a dedication or a copyright page is set.
    /// </summary>
    private static string BuildDocxMatter(MatterExportContent matter, bool pageBreakBefore)
    {
        var builder = new StringBuilder();
        var breakBefore = pageBreakBefore ? "<w:pageBreakBefore/>" : string.Empty;

        if (!string.IsNullOrEmpty(matter.Title))
        {
            builder.Append(
                $"<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/>{breakBefore}</w:pPr>"
                + $"<w:r><w:t>{EscapeXml(matter.Title)}</w:t></w:r></w:p>");
            breakBefore = string.Empty;
        }

        var first = true;
        foreach (var para in ParseHtmlToParagraphs(matter.HtmlContent))
        {
            // The break rides on the first paragraph when there is no heading.
            var pPr = first && breakBefore.Length > 0
                ? $"<w:pStyle w:val=\"NoIndent\"/>{breakBefore}"
                : "<w:pStyle w:val=\"BodyText\"/>";
            builder.Append($"<w:p><w:pPr>{pPr}</w:pPr>{SegmentsToDocxRuns(para)}</w:p>");
            first = false;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Author shown on exported comments. Novalist does not model an author
    /// identity, and inventing one from the OS user would put a real name into a
    /// file the writer may be sending to a stranger.
    /// </summary>
    private const string DocxCommentAuthor = "Novalist";

    /// <summary>
    /// Word runs. A footnote segment becomes a real <c>w:footnoteReference</c>,
    /// which is what lets an editor see it at the bottom of the page and Word
    /// renumber it - the manual has promised this for a long time while the
    /// exporter appended a paragraph of plain text instead.
    /// </summary>
    private static string SegmentsToDocxRuns(
        List<InlineSegment> segments, List<string>? footnoteDefs = null)
    {
        var sb = new StringBuilder();
        foreach (var seg in segments)
        {
            if (seg.FootnoteText != null)
            {
                if (footnoteDefs == null) continue;
                footnoteDefs.Add(seg.FootnoteText);
                // Ids 0 and 1 are the separator and continuation notes Word
                // requires, so the writer's notes start at 2.
                var id = footnoteDefs.Count + 1;
                sb.Append("<w:r><w:rPr><w:rStyle w:val=\"FootnoteReference\"/></w:rPr>")
                  .Append($"<w:footnoteReference w:id=\"{id}\"/></w:r>");
                continue;
            }
            var rPr = "";
            if (seg.Bold || seg.Italic || seg.Strike)
            {
                var b = seg.Bold ? "<w:b/><w:bCs/>" : "";
                var i = seg.Italic ? "<w:i/><w:iCs/>" : "";
                var s = seg.Strike ? "<w:strike/>" : "";
                rPr = $"<w:rPr>{b}{i}{s}</w:rPr>";
            }
            sb.Append($"<w:r>{rPr}<w:t xml:space=\"preserve\">{EscapeXml(seg.Text)}</w:t></w:r>");
        }
        return sb.ToString();
    }

    private static int CmToTwips(double cm) => (int)Math.Round(cm / 2.54 * 1440);

    private static async Task WriteDocxPackageLinksAsync(ZipArchive zip, bool smf, bool hasComments, List<string> imageExtensions, List<(string Part, string Absolute)> imageParts)
    {
        // [Content_Types].xml
        var contentTypesExtra = smf
            ? "\n  <Override PartName=\"/word/header1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml\"/>"
            : "";

        await WriteEntryAsync(zip, "[Content_Types].xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>{string.Concat(imageExtensions)}
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
              <Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>
              <Override PartName="/word/footnotes.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml"/>{contentTypesExtra}{(hasComments ? "\n  <Override PartName=\"/word/comments.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml\"/>" : "")}
            </Types>
            """);

        // _rels/.rels
        await WriteEntryAsync(zip, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);

        // word/_rels/document.xml.rels
        var headerRel = smf
            ? "\n  <Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" Target=\"header1.xml\"/>"
            : "";

        await WriteEntryAsync(zip, "word/_rels/document.xml.rels", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
              <Relationship Id="rId4" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>
              <Relationship Id="rId5" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footnotes" Target="footnotes.xml"/>{ImageRelationshipsXml(imageParts)}{headerRel}{(hasComments ? "\n  <Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments\" Target=\"comments.xml\"/>" : "")}
            </Relationships>
            """);

    }

    private static async Task WriteDocxHeaderAsync(ZipArchive zip, bool smf, ExportOptions options)
    {
        // SMF header
        if (smf)
        {
            var headerText = RunningHead(options);

            await WriteEntryAsync(zip, "word/header1.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:p>
                    <w:pPr><w:jc w:val="right"/></w:pPr>
                    <w:r><w:rPr><w:rFonts w:ascii="Courier New" w:hAnsi="Courier New"/><w:sz w:val="20"/></w:rPr><w:t xml:space="preserve">{EscapeXml(headerText)} / </w:t></w:r>
                    <w:r><w:rPr><w:rFonts w:ascii="Courier New" w:hAnsi="Courier New"/><w:sz w:val="20"/></w:rPr><w:fldChar w:fldCharType="begin"/></w:r>
                    <w:r><w:rPr><w:rFonts w:ascii="Courier New" w:hAnsi="Courier New"/><w:sz w:val="20"/></w:rPr><w:instrText> PAGE </w:instrText></w:r>
                    <w:r><w:rPr><w:rFonts w:ascii="Courier New" w:hAnsi="Courier New"/><w:sz w:val="20"/></w:rPr><w:fldChar w:fldCharType="end"/></w:r>
                  </w:p>
                </w:hdr>
                """);
        }

    }

    private static (StringBuilder Body, List<string> Footnotes) BuildDocxBody(List<ChapterExportContent> chapters, ExportOptions options, Dictionary<string, string> imageRels, Dictionary<string, int> commentIds)
    {
        // Build document body. Notes are collected as it is laid out; the part
        // they live in can only be written once their numbers are known.
        var body = new StringBuilder();
        var footnoteDefs = new List<string>();

        if (options.IncludeTitlePage)
        {
            body.Append($"<w:p><w:pPr><w:pStyle w:val=\"Title\"/></w:pPr><w:r><w:t>{EscapeXml(options.Title)}</w:t></w:r></w:p>");
            if (!string.IsNullOrWhiteSpace(options.Author))
                body.Append($"<w:p><w:pPr><w:pStyle w:val=\"Subtitle\"/></w:pPr><w:r><w:t>{EscapeXml(options.Author)}</w:t></w:r></w:p>");
        }

        // Front matter, each on its own page, before the story.
        foreach (var matter in options.Matter.Where(m => m.Placement == "Front"))
            body.Append(BuildDocxMatter(matter, options.IncludeTitlePage || body.Length > 0));

        var matterPrecedesChapters = options.Matter.Any(m => m.Placement == "Front");
        var context = new DocxRenderContext(options, imageRels, commentIds, footnoteDefs);

        for (var i = 0; i < chapters.Count; i++)
        {
            var chapter = chapters[i];
            var needsPageBreak = i > 0 || options.IncludeTitlePage || matterPrecedesChapters;
            AppendDocxChapter(body, chapter, context, needsPageBreak);
        }

        // Back matter, after the story.
        foreach (var matter in options.Matter.Where(m => m.Placement == "Back"))
            body.Append(BuildDocxMatter(matter, true));

        return (body, footnoteDefs);
    }

    private static async Task WriteDocxDocumentAsync(ZipArchive zip, bool smf, StringBuilder body, List<string> footnoteDefs)
    {
        // Section properties
        var sectPrHeader = smf
            ? "<w:headerReference w:type=\"default\" r:id=\"rId2\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"/>"
            : "";

        // Written after the body: the notes are only known once it is laid out.
        await WriteEntryAsync(zip, "word/footnotes.xml", GenerateDocxFootnotes(footnoteDefs));

        await WriteEntryAsync(zip, "word/document.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                {body}
                <w:sectPr>
                  {sectPrHeader}
                  <w:pgSz w:w="12240" w:h="15840"/>
                  <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/>
                </w:sectPr>
              </w:body>
            </w:document>
            """);
    }

    private static void AppendDocxChapter(StringBuilder body, ChapterExportContent chapter, DocxRenderContext context, bool needsPageBreak)
    {

        // Chapter heading
        var docxHeading = chapter.Heading;
        var breakBefore = needsPageBreak ? "<w:pageBreakBefore/>" : string.Empty;
        if (chapter.HideHeading)
        {
            // The page still turns; only the words are gone.
            if (needsPageBreak)
                body.Append("<w:p><w:pPr><w:pageBreakBefore/></w:pPr></w:p>");
        }
        else
        {
            body.Append($"<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/>{breakBefore}</w:pPr><w:r><w:t>{EscapeXml(docxHeading)}</w:t></w:r></w:p>");
            if (!string.IsNullOrWhiteSpace(chapter.Subtitle))
                body.Append($"<w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr><w:r><w:rPr><w:i/></w:rPr><w:t>{EscapeXml(chapter.Subtitle)}</w:t></w:r></w:p>");
        }

        // Scenes
        for (var si = 0; si < chapter.Scenes.Count; si++)
        {
            AppendDocxScene(body, chapter.Scenes[si], si, context);
        }
    }

    private static void AppendDocxScene(StringBuilder body, SceneExportContent scene, int si, DocxRenderContext context)
    {
        // Scene break between scenes
        if (si > 0)
        {
            body.Append($"<w:p><w:pPr><w:pStyle w:val=\"SceneBreak\"/></w:pPr><w:r><w:t>{SceneBreakText}</w:t></w:r></w:p>");
        }

        var blocks = ParseHtmlToBlocks(scene.HtmlContent, scene.Footnotes);
        var isFirstPara = si == 0;
        // One anchor per comment: a phrase repeated across paragraphs
        // would otherwise mark every occurrence.
        var anchored = new HashSet<string>(StringComparer.Ordinal);

        foreach (var block in blocks)
        {
            if (block.ImagePath != null)
            {
                if (context.Images.TryGetValue(block.ImagePath, out var relId))
                    body.Append(DocxImageParagraph(block.ImagePath, relId, block.ImageAlt));
                continue;
            }

            var para = block.Segments;

            // The chapter's opening paragraph, when the layout asks for
            // a drop cap: Word wants the initial in a framed paragraph
            // of its own, with the rest following as normal text.
            var docxPreset = context.Options.ResolvePreset();
            if (isFirstPara && si == 0 && docxPreset.DropCap && block.StyleId == null
                && block.List == ListKind.None)
            {
                var opener = SplitOpener(
                    string.Concat(para.Select(seg => seg.Text)),
                    docxPreset.LeadInSmallCapsWords);
                if (opener != null)
                {
                    body.Append(DocxDropCapParagraphs(opener.Value, docxPreset));
                    isFirstPara = false;
                    continue;
                }
            }

            var runs = SegmentsToDocxRuns(para, context.Footnotes);
            var paragraphXml =
                $"<w:p><w:pPr>{DocxParagraphProperties(block, isFirstPara)}</w:pPr>{runs}</w:p>";

            // Skipped when this scene has none, even if other scenes do.
            if (scene.Comments.Count > 0)
            {
                var plain = string.Concat(para.Select(seg => seg.Text));
                paragraphXml = WrapDocxCommentRanges(
                    paragraphXml, plain, context.Comments, scene, anchored);
            }

            body.Append(paragraphXml);
            isFirstPara = false;
        }
    }

    private sealed record DocxRenderContext(
        ExportOptions Options, Dictionary<string, string> Images,
        Dictionary<string, int> Comments, List<string> Footnotes);
}
