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

        foreach (var (part, absolute) in imageParts)
            await WriteBinaryEntryAsync(zip, $"word/{part}", absolute);

        // word/styles.xml
        await WriteEntryAsync(zip, "word/styles.xml", GenerateDocxStyles(options));
        // Real Word list numbering rather than a literal bullet character typed
        // into the text, so an editor can renumber and restyle the list.
        await WriteEntryAsync(zip, "word/numbering.xml", GenerateDocxNumbering());

        if (hasComments)
            await WriteEntryAsync(zip, "word/comments.xml", GenerateDocxComments(exportComments));

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

        for (var i = 0; i < chapters.Count; i++)
        {
            var chapter = chapters[i];
            var needsPageBreak = i > 0 || options.IncludeTitlePage || matterPrecedesChapters;

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
                // Scene break between scenes
                if (si > 0)
                {
                    body.Append($"<w:p><w:pPr><w:pStyle w:val=\"SceneBreak\"/></w:pPr><w:r><w:t>{SceneBreakText}</w:t></w:r></w:p>");
                }

                var scene = chapter.Scenes[si];
                var blocks = ParseHtmlToBlocks(scene.HtmlContent, scene.Footnotes);
                var isFirstPara = si == 0;
                // One anchor per comment: a phrase repeated across paragraphs
                // would otherwise mark every occurrence.
                var anchored = new HashSet<string>(StringComparer.Ordinal);

                foreach (var block in blocks)
                {
                    if (block.ImagePath != null)
                    {
                        if (imageRels.TryGetValue(block.ImagePath, out var relId))
                            body.Append(DocxImageParagraph(block.ImagePath, relId, block.ImageAlt));
                        continue;
                    }

                    var para = block.Segments;

                    // The chapter's opening paragraph, when the layout asks for
                    // a drop cap: Word wants the initial in a framed paragraph
                    // of its own, with the rest following as normal text.
                    var docxPreset = options.ResolvePreset();
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

                    var runs = SegmentsToDocxRuns(para, footnoteDefs);
                    var paragraphXml =
                        $"<w:p><w:pPr>{DocxParagraphProperties(block, isFirstPara)}</w:pPr>{runs}</w:p>";

                    // Skipped when this scene has none, even if other scenes do.
                    if (scene.Comments.Count > 0)
                    {
                        var plain = string.Concat(para.Select(seg => seg.Text));
                        paragraphXml = WrapDocxCommentRanges(
                            paragraphXml, plain, commentIds, scene, anchored);
                    }

                    body.Append(paragraphXml);
                    isFirstPara = false;
                }
            }
        }

        // Back matter, after the story.
        foreach (var matter in options.Matter.Where(m => m.Placement == "Back"))
            body.Append(BuildDocxMatter(matter, true));

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
    /// Author shown on exported comments. Novalist does not model an author
    /// identity, and inventing one from the OS user would put a real name into a
    /// file the writer may be sending to a stranger.
    /// </summary>
    private const string DocxCommentAuthor = "Novalist";

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
    /// Word's own drop cap: the initial sits in a framed paragraph the text
    /// wraps around, and the rest of the opener follows as ordinary prose with
    /// its lead-in words in small capitals.
    /// </summary>
    private static string DocxDropCapParagraphs(
        (string Initial, string LeadIn, string Tail) opener, ExportPreset preset)
    {
        var size = (int)Math.Round(preset.BodyFontSizePt * 2 * 3);  // half-points, three lines
        var initial =
            "<w:p><w:pPr><w:framePr w:dropCap=\"drop\" w:lines=\"3\" w:wrap=\"around\""
            + " w:vAnchor=\"text\" w:hAnchor=\"text\"/><w:spacing w:line=\"640\" w:lineRule=\"exact\"/>"
            + $"</w:pPr><w:r><w:rPr><w:sz w:val=\"{size}\"/></w:rPr>"
            + $"<w:t xml:space=\"preserve\">{EscapeXml(opener.Initial)}</w:t></w:r></w:p>";

        var lead = opener.LeadIn.Length == 0
            ? string.Empty
            : $"<w:r><w:rPr><w:smallCaps/></w:rPr><w:t xml:space=\"preserve\">{EscapeXml(opener.LeadIn)}</w:t></w:r>";

        return initial
            + $"<w:p>{lead}<w:r><w:t xml:space=\"preserve\">{EscapeXml(opener.Tail)}</w:t></w:r></w:p>";
    }

    /// <summary>The content type Word expects for an image part.</summary>
    private static string DocxImageContentType(string extension) => extension switch
    {
        "jpg" or "jpeg" => "image/jpeg",
        "gif" => "image/gif",
        "bmp" => "image/bmp",
        "webp" => "image/webp",
        _ => "image/png"
    };

    /// <summary>One relationship per image part, for the document's rels file.</summary>
    private static string ImageRelationshipsXml(List<(string Part, string Absolute)> parts)
        => string.Concat(parts.Select((p, i) =>
            "\n  <Relationship Id=\"rIdImage" + (i + 1) + "\" Type=\""
            + "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"
            + "\" Target=\"" + p.Part + "\"/>"));

    /// <summary>
    /// One centred paragraph holding an inline drawing. Word measures in EMUs
    /// (914400 to the inch), so the pixel size is converted at 96dpi and capped
    /// at six inches - an image wider than the page is a broken document rather
    /// than a large picture.
    /// </summary>
    private static string DocxImageParagraph(string path, string relId, string alt)
    {
        var (pixelWidth, pixelHeight) = ImageSize(path);
        var naturalWidth = Math.Max(0.1, pixelWidth / 96.0);
        var widthInches = Math.Min(6.0, naturalWidth);
        var heightInches = pixelHeight / 96.0 * (widthInches / naturalWidth);
        var cx = (long)(widthInches * 914400);
        var cy = (long)(heightInches * 914400);
        var description = EscapeXml(alt);
        var id = Math.Abs(relId.GetHashCode()) % 100000 + 1;

        return "<w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr><w:r><w:drawing>"
            + "<wp:inline xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\""
            + " distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">"
            + $"<wp:extent cx=\"{cx}\" cy=\"{cy}\"/>"
            + $"<wp:docPr id=\"{id}\" name=\"Image {id}\" descr=\"{description}\"/>"
            + "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">"
            + "<a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">"
            + "<pic:pic xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">"
            + $"<pic:nvPicPr><pic:cNvPr id=\"0\" name=\"Image {id}\" descr=\"{description}\"/>"
            + "<pic:cNvPicPr/></pic:nvPicPr>"
            + "<pic:blipFill><a:blip xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\""
            + $" r:embed=\"{relId}\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>"
            + $"<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm>"
            + "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>"
            + "</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>";
    }

    /// <summary>
    /// An image's pixel size, read from the file's own header. PNG and JPEG
    /// cover what the editor accepts; anything else falls back to a square,
    /// which lays out sensibly even when it is not exact.
    /// </summary>
    internal static (int Width, int Height) ImageSize(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            var signature = reader.ReadBytes(8);

            // PNG: width and height are big-endian ints at byte 16.
            if (signature.Length == 8 && signature[0] == 0x89 && signature[1] == 0x50)
            {
                stream.Position = 16;
                var w = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(reader.ReadBytes(4));
                var h = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(reader.ReadBytes(4));
                return (w, h);
            }

            // JPEG: walk the segments to the start-of-frame, which carries the size.
            if (signature.Length >= 2 && signature[0] == 0xFF && signature[1] == 0xD8)
            {
                stream.Position = 2;
                while (stream.Position < stream.Length - 8)
                {
                    if (reader.ReadByte() != 0xFF) continue;
                    var marker = reader.ReadByte();
                    var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
                    if (marker is >= 0xC0 and <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                    {
                        reader.ReadByte();
                        var h = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
                        var w = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
                        return (w, h);
                    }
                    stream.Position += length - 2;
                }
            }
        }
        catch (Exception e) when (e is IOException or EndOfStreamException
            or ArgumentException or UnauthorizedAccessException)
        {
            // Unreadable is not fatal: the fallback below still lays out.
        }
        return (600, 600);
    }

    /// <summary>
    /// The styles part of a reference DOCX, or null when there is not a usable
    /// one. A publisher's house style arrives as a styled Word file, not as a
    /// list of settings, and reapplying it by hand after every export is how a
    /// submission goes out in the wrong font.
    /// </summary>
    internal static string? ReadReferenceStyles(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            using var archive = ZipFile.OpenRead(path);
            var entry = archive.GetEntry("word/styles.xml");
            if (entry == null) return null;

            using var reader = new StreamReader(entry.Open());
            var xml = reader.ReadToEnd();
            // A part without the wordprocessing root is not a styles part, and
            // writing it would produce a file Word refuses to open at all.
            return xml.Contains("<w:styles", StringComparison.Ordinal) ? xml : null;
        }
        catch (Exception ex) when (
            ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // A reference document that cannot be read is a reason to fall back
            // to ours, never a reason to fail the export the writer asked for.
            return null;
        }
    }

    private static string GenerateDocxStyles(ExportOptions options)
    {
        if (ReadReferenceStyles(options.ReferenceDocPath) is { } borrowed) return borrowed;

        var smf = options.ResolvePreset().ShunnHeader;
        var fontFamily = smf ? "Courier New" : "Georgia";
        var fontSize = "24";
        var lineSpacing = smf ? "480" : "360";
        var bodyIndent = smf ? "<w:ind w:firstLine=\"720\"/>" : "<w:spacing w:after=\"160\"/>";
        var noIndentSpacing = smf ? "" : "<w:spacing w:after=\"160\"/>";

        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:docDefaults>
                <w:rPrDefault>
                  <w:rPr>
                    <w:rFonts w:ascii="{fontFamily}" w:hAnsi="{fontFamily}" w:eastAsia="{fontFamily}" w:cs="{fontFamily}"/>
                    <w:sz w:val="{fontSize}"/>
                    <w:szCs w:val="{fontSize}"/>
                    <w:lang w:val="en-US"/>
                  </w:rPr>
                </w:rPrDefault>
                <w:pPrDefault>
                  <w:pPr>
                    <w:spacing w:line="{lineSpacing}" w:lineRule="auto"/>
                  </w:pPr>
                </w:pPrDefault>
              </w:docDefaults>

              <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
                <w:name w:val="Normal"/>
              </w:style>

              <w:style w:type="paragraph" w:styleId="Title">
                <w:name w:val="Title"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:jc w:val="center"/>
                  <w:spacing w:before="4800" w:after="240"/>
                </w:pPr>
                <w:rPr>
                  <w:sz w:val="52"/>
                  <w:szCs w:val="52"/>
                  <w:b/>
                  <w:bCs/>
                </w:rPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="Subtitle">
                <w:name w:val="Subtitle"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:jc w:val="center"/>
                  <w:spacing w:before="240"/>
                </w:pPr>
                <w:rPr>
                  <w:sz w:val="32"/>
                  <w:szCs w:val="32"/>
                  <w:i/>
                  <w:iCs/>
                </w:rPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="Heading1">
                <w:name w:val="heading 1"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:jc w:val="center"/>
                  <w:spacing w:before="1440" w:after="720"/>
                </w:pPr>
                <w:rPr>
                  <w:sz w:val="36"/>
                  <w:szCs w:val="36"/>
                  <w:b/>
                  <w:bCs/>
                </w:rPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="BodyText">
                <w:name w:val="Body Text"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  {bodyIndent}
                  <w:jc w:val="both"/>
                </w:pPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="NoIndent">
                <w:name w:val="No Indent"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:ind w:firstLine="0"/>
                  {noIndentSpacing}
                  <w:jc w:val="both"/>
                </w:pPr>
              </w:style>

              <w:style w:type="character" w:styleId="FootnoteReference">
                <w:name w:val="footnote reference"/>
                <w:rPr><w:vertAlign w:val="superscript"/></w:rPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="FootnoteText">
                <w:name w:val="footnote text"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr><w:spacing w:line="240" w:lineRule="auto" w:after="0"/></w:pPr>
                <w:rPr><w:sz w:val="20"/></w:rPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="SceneBreak">
                <w:name w:val="Scene Break"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:jc w:val="center"/>
                  <w:spacing w:before="360" w:after="360"/>
                </w:pPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="Heading2">
                <w:name w:val="heading 2"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:outlineLvl w:val="1"/>
                  <w:ind w:firstLine="0"/>
                  <w:spacing w:before="360" w:after="180"/>
                  <w:keepNext/>
                </w:pPr>
                <w:rPr><w:b/><w:sz w:val="30"/><w:szCs w:val="30"/></w:rPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="Heading3">
                <w:name w:val="heading 3"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:outlineLvl w:val="2"/>
                  <w:ind w:firstLine="0"/>
                  <w:spacing w:before="280" w:after="140"/>
                  <w:keepNext/>
                </w:pPr>
                <w:rPr><w:b/><w:sz w:val="26"/><w:szCs w:val="26"/></w:rPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="Quote">
                <w:name w:val="Quote"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:ind w:left="720" w:right="720" w:firstLine="0"/>
                  <w:spacing w:before="180" w:after="180"/>
                </w:pPr>
                <w:rPr><w:i/></w:rPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="Verse">
                <w:name w:val="Verse"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:ind w:left="720" w:firstLine="0"/>
                  <w:jc w:val="left"/>
                </w:pPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="ListParagraph">
                <w:name w:val="List Paragraph"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:ind w:left="720" w:firstLine="0"/>
                  <w:contextualSpacing/>
                  <w:jc w:val="left"/>
                </w:pPr>
              </w:style>
            </w:styles>
            """;
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

    private static string GenerateDocxNumbering()
    {
        var bulletLevels = new StringBuilder();
        var decimalLevels = new StringBuilder();
        for (var level = 0; level < 9; level++)
        {
            var indent = 720 * (level + 1);
            bulletLevels.Append($"""
                  <w:lvl w:ilvl="{level}">
                    <w:start w:val="1"/>
                    <w:numFmt w:val="bullet"/>
                    <w:lvlText w:val="&#8226;"/>
                    <w:lvlJc w:val="left"/>
                    <w:pPr><w:ind w:left="{indent}" w:hanging="360"/></w:pPr>
                    <w:rPr><w:rFonts w:ascii="Symbol" w:hAnsi="Symbol" w:hint="default"/></w:rPr>
                  </w:lvl>
                """);
            decimalLevels.Append($"""
                  <w:lvl w:ilvl="{level}">
                    <w:start w:val="1"/>
                    <w:numFmt w:val="decimal"/>
                    <w:lvlText w:val="%{level + 1}."/>
                    <w:lvlJc w:val="left"/>
                    <w:pPr><w:ind w:left="{indent}" w:hanging="360"/></w:pPr>
                  </w:lvl>
                """);
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:abstractNum w:abstractNumId="0">
                <w:multiLevelType w:val="hybridMultilevel"/>
            {bulletLevels}
              </w:abstractNum>
              <w:abstractNum w:abstractNumId="1">
                <w:multiLevelType w:val="hybridMultilevel"/>
            {decimalLevels}
              </w:abstractNum>
              <w:num w:numId="{DocxBulletNumId}"><w:abstractNumId w:val="0"/></w:num>
              <w:num w:numId="{DocxNumberNumId}"><w:abstractNumId w:val="1"/></w:num>
            </w:numbering>
            """;
    }

    /// <summary>The numId a bulleted list paragraph points at.</summary>
    private const int DocxBulletNumId = 1;

    /// <summary>The numId a numbered list paragraph points at.</summary>
    private const int DocxNumberNumId = 2;

    /// <summary>The paragraph properties for one exported block: its Word style
    /// and, for a list item, the numbering it belongs to.</summary>
    private static string DocxParagraphProperties(ExportBlock block, bool firstInScene)
    {
        if (block.List != ListKind.None)
        {
            var numId = block.List == ListKind.Number ? DocxNumberNumId : DocxBulletNumId;
            return "<w:pStyle w:val=\"ListParagraph\"/>"
                   + $"<w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"{numId}\"/></w:numPr>";
        }

        var style = block.StyleId switch
        {
            "heading" => "Heading2",
            "subheading" => "Heading3",
            "blockquote" => "Quote",
            "poetry" => "Verse",
            // The first paragraph of a scene is not indented; typographic
            // convention, and what the exporter already did.
            _ => firstInScene ? "NoIndent" : "BodyText"
        };
        return $"<w:pStyle w:val=\"{style}\"/>";
    }

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

    /// <summary>
    /// Writes a DOCX laid out on the Normseite grid: every line hard-wrapped to
    /// the preset's column count, a forced page break every N lines, and a
    /// running header carrying the title and "Seite x von y".
    /// </summary>
    public static async Task WriteNormseitenDocxAsync(
        IReadOnlyList<NormseitenBlock> blocks,
        ExportOptions options,
        string outputPath)
    {
        var preset = options.ResolvePreset();
        var lines = NormseitenRenderer.RenderLines(blocks, preset.GridColumns);
        var metrics = NormseitenRenderer.Measure(lines, preset.GridLines);
        var pages = Math.Max(1, metrics.Pages);

        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        await WriteEntryAsync(zip, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
              <Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
            </Types>
            """);

        await WriteEntryAsync(zip, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);

        await WriteEntryAsync(zip, "word/_rels/document.xml.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/>
            </Relationships>
            """);

        var font = preset.BodyFontFamily;
        var halfPoints = (int)Math.Round(preset.BodyFontSizePt * 2);
        // Word measures exact line spacing in twentieths of a point.
        var exactLine = (int)Math.Round((preset.LineHeightPt > 0 ? preset.LineHeightPt : preset.BodyFontSizePt * 2) * 20);

        await WriteEntryAsync(zip, "word/styles.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:docDefaults>
                <w:rPrDefault>
                  <w:rPr>
                    <w:rFonts w:ascii="{font}" w:hAnsi="{font}" w:eastAsia="{font}" w:cs="{font}"/>
                    <w:sz w:val="{halfPoints}"/>
                    <w:szCs w:val="{halfPoints}"/>
                  </w:rPr>
                </w:rPrDefault>
                <w:pPrDefault>
                  <w:pPr>
                    <w:spacing w:before="0" w:after="0" w:line="{exactLine}" w:lineRule="exact"/>
                  </w:pPr>
                </w:pPrDefault>
              </w:docDefaults>
              <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
                <w:name w:val="Normal"/>
              </w:style>
              <w:style w:type="paragraph" w:styleId="Header">
                <w:name w:val="header"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:spacing w:before="0" w:after="0" w:line="240" w:lineRule="auto"/>
                </w:pPr>
                <w:rPr>
                  <w:sz w:val="20"/>
                  <w:szCs w:val="20"/>
                </w:rPr>
              </w:style>
            </w:styles>
            """);

        // Header: title flush left, page counter flush right against the text edge.
        var textWidthTwips = CmToTwips(preset.TextWidthCm);
        var headerTitle = string.IsNullOrWhiteSpace(options.Title) ? string.Empty : EscapeXml(options.Title);
        await WriteEntryAsync(zip, "word/header1.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:p>
                <w:pPr>
                  <w:pStyle w:val="Header"/>
                  <w:tabs><w:tab w:val="right" w:pos="{textWidthTwips}"/></w:tabs>
                </w:pPr>
                <w:r><w:t xml:space="preserve">{headerTitle}</w:t></w:r>
                <w:r><w:tab/><w:t xml:space="preserve">Seite </w:t></w:r>
                <w:r><w:fldChar w:fldCharType="begin"/></w:r>
                <w:r><w:instrText xml:space="preserve"> PAGE </w:instrText></w:r>
                <w:r><w:fldChar w:fldCharType="end"/></w:r>
                <w:r><w:t xml:space="preserve"> von {pages}</w:t></w:r>
              </w:p>
            </w:hdr>
            """);

        var body = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            var pageBreak = i > 0 && i % preset.GridLines == 0
                ? "<w:r><w:br w:type=\"page\"/></w:r>"
                : string.Empty;
            var content = lines[i].Length == 0
                ? string.Empty
                : $"<w:r><w:t xml:space=\"preserve\">{EscapeXml(lines[i])}</w:t></w:r>";
            body.Append($"<w:p>{pageBreak}{content}</w:p>");
        }

        await WriteEntryAsync(zip, "word/document.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <w:body>
                {body}
                <w:sectPr>
                  <w:headerReference w:type="default" r:id="rId2"/>
                  <w:pgSz w:w="{CmToTwips(preset.PageWidthCm)}" w:h="{CmToTwips(preset.PageHeightCm)}"/>
                  <w:pgMar w:top="{CmToTwips(preset.MarginTopCm)}" w:right="{CmToTwips(preset.MarginRightCm)}" w:bottom="{CmToTwips(preset.MarginBottomCm)}" w:left="{CmToTwips(preset.MarginLeftCm)}" w:header="{CmToTwips(preset.HeaderDistanceCm)}" w:footer="{CmToTwips(preset.HeaderDistanceCm)}" w:gutter="0"/>
                </w:sectPr>
              </w:body>
            </w:document>
            """);
    }
}
