using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public enum ExportFormat
{
    Epub,
    Docx,
    Pdf,
    Markdown
}

public class ExportOptions
{
    public ExportFormat Format { get; set; } = ExportFormat.Epub;
    public bool IncludeTitlePage { get; set; } = true;
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public bool SmfPreset { get; set; }
    public List<string> SelectedChapterGuids { get; set; } = [];
}

public class ChapterExportContent
{
    public string Title { get; set; } = string.Empty;
    public int Order { get; set; }
    public List<SceneExportContent> Scenes { get; set; } = [];
}

public class SceneExportContent
{
    public string Title { get; set; } = string.Empty;
    public int Order { get; set; }
    public string HtmlContent { get; set; } = string.Empty;
}

/// <summary>
/// Internal representation of a text segment with formatting metadata.
/// </summary>
internal sealed class InlineSegment
{
    public string Text { get; set; } = string.Empty;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
}

/// <summary>
/// Provides export functionality for Novalist projects.
/// Supports EPUB, DOCX, PDF, and Markdown output formats.
/// </summary>
public partial class ExportService
{
    private const string SceneBreakText = "* * *";

    private readonly IProjectService _projectService;

    public ExportService(IProjectService projectService)
    {
        _projectService = projectService;
    }

    /// <summary>
    /// Compile chapter and scene data for export.
    /// </summary>
    public async Task<List<ChapterExportContent>> CompileChaptersAsync(ExportOptions options)
    {
        var chapters = _projectService.GetChaptersOrdered()
            .Where(c => options.SelectedChapterGuids.Contains(c.Guid))
            .OrderBy(c => c.Order)
            .ToList();

        var result = new List<ChapterExportContent>();

        foreach (var chapter in chapters)
        {
            var scenes = _projectService.GetScenesForChapter(chapter.Guid);
            var sceneContents = new List<SceneExportContent>();

            foreach (var scene in scenes)
            {
                var html = await _projectService.ReadSceneContentAsync(chapter, scene);
                sceneContents.Add(new SceneExportContent
                {
                    Title = scene.Title,
                    Order = scene.Order,
                    HtmlContent = html
                });
            }

            result.Add(new ChapterExportContent
            {
                Title = chapter.Title,
                Order = chapter.Order,
                Scenes = sceneContents
            });
        }

        return result;
    }

    /// <summary>
    /// Export the project to the specified format and write to a file.
    /// </summary>
    public async Task ExportAsync(ExportOptions options, string outputPath)
    {
        var chapters = await CompileChaptersAsync(options);

        switch (options.Format)
        {
            case ExportFormat.Epub:
                await ExportToEpubAsync(chapters, options, outputPath);
                break;
            case ExportFormat.Docx:
                await ExportToDocxAsync(chapters, options, outputPath);
                break;
            case ExportFormat.Pdf:
                ExportToPdf(chapters, options, outputPath);
                break;
            case ExportFormat.Markdown:
                await ExportToMarkdownAsync(chapters, options, outputPath);
                break;
        }
    }

    // ─── HTML Processing ─────────────────────────────────────────────

    /// <summary>
    /// Extract plain-text paragraphs from scene HTML content.
    /// Returns a list of paragraphs with inline formatting preserved as segments.
    /// </summary>
    private static List<List<InlineSegment>> ParseHtmlToParagraphs(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return [];

        var paragraphs = new List<List<InlineSegment>>();

        // The AvRichTextBox stores content as HTML with <p> elements.
        // Split by paragraph tags, then parse inline formatting.
        var pRegex = ParagraphRegex();
        var matches = pRegex.Matches(html);

        if (matches.Count == 0)
        {
            // Fallback: treat entire content as one paragraph
            var stripped = StripHtml(html);
            if (!string.IsNullOrWhiteSpace(stripped))
                paragraphs.Add([new InlineSegment { Text = stripped.Trim() }]);
            return paragraphs;
        }

        foreach (Match match in matches)
        {
            var innerHtml = match.Groups[1].Value;
            var segments = ParseInlineFormatting(innerHtml);
            if (segments.Count > 0 && segments.Any(s => !string.IsNullOrWhiteSpace(s.Text)))
                paragraphs.Add(segments);
        }

        return paragraphs;
    }

    /// <summary>
    /// Parse inline formatting (bold, italic, underline) from HTML content.
    /// </summary>
    private static List<InlineSegment> ParseInlineFormatting(string html)
    {
        var segments = new List<InlineSegment>();
        ParseInlineRecursive(html, false, false, segments);
        return segments;
    }

    private static void ParseInlineRecursive(string html, bool bold, bool italic, List<InlineSegment> segments)
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
                    segments.Add(new InlineSegment { Text = text, Bold = bold, Italic = italic });
                break;
            }

            // Text before tag
            if (tagStart > pos)
            {
                var text = WebUtility.HtmlDecode(html[pos..tagStart]);
                if (!string.IsNullOrEmpty(text))
                    segments.Add(new InlineSegment { Text = text, Bold = bold, Italic = italic });
            }

            var tagEnd = html.IndexOf('>', tagStart);
            if (tagEnd < 0) break;

            var tag = html[(tagStart + 1)..tagEnd].Trim().ToLowerInvariant();
            pos = tagEnd + 1;

            // Self-closing tags
            if (tag is "br" or "br/" or "br /")
            {
                segments.Add(new InlineSegment { Text = "\n", Bold = bold, Italic = italic });
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

            switch (tagName)
            {
                case "b" or "strong":
                    ParseInlineRecursive(innerContent, true, italic, segments);
                    break;
                case "i" or "em":
                    ParseInlineRecursive(innerContent, bold, true, segments);
                    break;
                case "u":
                    // Underline treated as regular text in export (no underline in most book formats)
                    ParseInlineRecursive(innerContent, bold, italic, segments);
                    break;
                case "span":
                    // Spans may carry style info but for export we just recurse
                    ParseInlineRecursive(innerContent, bold, italic, segments);
                    break;
                default:
                    // Unknown tag - just extract text
                    ParseInlineRecursive(innerContent, bold, italic, segments);
                    break;
            }
        }
    }

    private static int FindMatchingCloseTag(string html, int startPos, string tagName)
    {
        var depth = 1;
        var pos = startPos;
        var openPattern = $"<{tagName}";
        var closePattern = $"</{tagName}>";

        while (pos < html.Length && depth > 0)
        {
            var nextOpen = html.IndexOf(openPattern, pos, StringComparison.OrdinalIgnoreCase);
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
    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        var text = Regex.Replace(html, "<[^>]+>", string.Empty);
        return WebUtility.HtmlDecode(text);
    }

    /// <summary>
    /// Get plain text from a chapter's scenes, suitable for PDF/stat use.
    /// </summary>
    private static string GetChapterPlainText(ChapterExportContent chapter)
    {
        var sb = new StringBuilder();
        foreach (var scene in chapter.Scenes)
        {
            var paragraphs = ParseHtmlToParagraphs(scene.HtmlContent);
            foreach (var para in paragraphs)
            {
                foreach (var seg in para)
                    sb.Append(seg.Text);
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    // ─── XML/HTML Escaping ───────────────────────────────────────────

    private static string EscapeXml(string str)
    {
        return str
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    private static string GenerateUuid()
    {
        return Guid.NewGuid().ToString();
    }

    private static string SanitizeFilename(string name)
    {
        return Regex.Replace(name, @"[^a-zA-Z0-9\-_\s]", "").Replace(' ', '_');
    }

    // ─── EPUB Export ─────────────────────────────────────────────────

    private static async Task ExportToEpubAsync(
        List<ChapterExportContent> chapters,
        ExportOptions options,
        string outputPath)
    {
        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        // mimetype - must be first, stored without compression
        var mimetypeEntry = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        await using (var w = new StreamWriter(mimetypeEntry.Open(), Encoding.ASCII))
            await w.WriteAsync("application/epub+zip");

        var bookId = $"urn:uuid:{GenerateUuid()}";
        var modifiedDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        // META-INF/container.xml
        await WriteEntryAsync(zip, "META-INF/container.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles>
                <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
              </rootfiles>
            </container>
            """);

        // Stylesheet
        await WriteEntryAsync(zip, "OEBPS/styles.css", GenerateEpubStylesheet());

        // Title page
        if (options.IncludeTitlePage)
            await WriteEntryAsync(zip, "OEBPS/title.xhtml", GenerateTitlePageXhtml(options));

        // Chapter files
        for (var i = 0; i < chapters.Count; i++)
            await WriteEntryAsync(zip, $"OEBPS/chapter-{i + 1}.xhtml", GenerateChapterXhtml(chapters[i]));

        // Navigation
        await WriteEntryAsync(zip, "OEBPS/nav.xhtml", GenerateNavXhtml(chapters, options));
        await WriteEntryAsync(zip, "OEBPS/toc.ncx", GenerateTocNcx(chapters, options, bookId));
        await WriteEntryAsync(zip, "OEBPS/content.opf", GenerateContentOpf(chapters, options, bookId, modifiedDate));
    }

    private static async Task WriteEntryAsync(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        await using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        await writer.WriteAsync(content);
    }

    private static string GenerateEpubStylesheet()
    {
        return """
            @page { margin: 1in; }

            body {
              font-family: Georgia, "Times New Roman", Times, serif;
              line-height: 1.5;
              margin: 1em;
              padding: 0;
            }

            h1.chapter-title {
              font-size: 1.5em;
              text-align: center;
              font-weight: bold;
              margin-top: 3em;
              margin-bottom: 2em;
            }

            p {
              margin-top: 0;
              margin-bottom: 0.8em;
              text-align: justify;
              orphans: 2;
              widows: 2;
            }

            p.scene-break {
              text-align: center;
              margin-top: 1.5em;
              margin-bottom: 1.5em;
            }

            div.title-page {
              text-align: center;
              padding-top: 30%;
            }

            div.title-page h1 {
              font-size: 2em;
              font-weight: bold;
              margin-bottom: 1em;
              text-indent: 0;
            }

            div.title-page p.author {
              font-size: 1.2em;
              font-style: italic;
              text-indent: 0;
            }
            """;
    }

    private static string GenerateChapterXhtml(ChapterExportContent chapter)
    {
        var bodyHtml = new StringBuilder();
        for (var si = 0; si < chapter.Scenes.Count; si++)
        {
            if (si > 0)
                bodyHtml.AppendLine($"    <p class=\"scene-break\">{SceneBreakText}</p>");

            var scene = chapter.Scenes[si];
            var paragraphs = ParseHtmlToParagraphs(scene.HtmlContent);
            var isFirst = si == 0;

            foreach (var para in paragraphs)
            {
                var content = SegmentsToXhtml(para);
                var cls = isFirst ? " class=\"no-indent\"" : "";
                bodyHtml.AppendLine($"    <p{cls}>{content}</p>");
                isFirst = false;
            }
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" xml:lang="en">
            <head>
              <meta charset="UTF-8"/>
              <title>{EscapeXml(chapter.Title)}</title>
              <link rel="stylesheet" type="text/css" href="styles.css"/>
            </head>
            <body>
              <section epub:type="chapter">
                <h1 class="chapter-title">{EscapeXml(chapter.Title)}</h1>
            {bodyHtml}
              </section>
            </body>
            </html>
            """;
    }

    private static string SegmentsToXhtml(List<InlineSegment> segments)
    {
        var sb = new StringBuilder();
        foreach (var seg in segments)
        {
            var text = EscapeXml(seg.Text);
            if (seg.Bold && seg.Italic)
                sb.Append($"<strong><em>{text}</em></strong>");
            else if (seg.Bold)
                sb.Append($"<strong>{text}</strong>");
            else if (seg.Italic)
                sb.Append($"<em>{text}</em>");
            else
                sb.Append(text);
        }
        return sb.ToString();
    }

    private static string GenerateTitlePageXhtml(ExportOptions options)
    {
        var authorHtml = !string.IsNullOrWhiteSpace(options.Author)
            ? $"<p class=\"author\">{EscapeXml(options.Author)}</p>"
            : "";

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" xml:lang="en">
            <head>
              <meta charset="UTF-8"/>
              <title>{EscapeXml(options.Title)}</title>
              <link rel="stylesheet" type="text/css" href="styles.css"/>
            </head>
            <body>
              <div class="title-page" epub:type="titlepage">
                <h1>{EscapeXml(options.Title)}</h1>
                {authorHtml}
              </div>
            </body>
            </html>
            """;
    }

    private static string GenerateNavXhtml(List<ChapterExportContent> chapters, ExportOptions options)
    {
        var items = new StringBuilder();
        if (options.IncludeTitlePage)
            items.AppendLine("      <li><a href=\"title.xhtml\">Title Page</a></li>");

        for (var i = 0; i < chapters.Count; i++)
            items.AppendLine($"      <li><a href=\"chapter-{i + 1}.xhtml\">{EscapeXml(chapters[i].Title)}</a></li>");

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" xml:lang="en">
            <head>
              <meta charset="UTF-8"/>
              <title>Table of Contents</title>
            </head>
            <body>
              <nav epub:type="toc" id="toc">
                <h1>Table of Contents</h1>
                <ol>
            {items}
                </ol>
              </nav>
            </body>
            </html>
            """;
    }

    private static string GenerateTocNcx(List<ChapterExportContent> chapters, ExportOptions options, string bookId)
    {
        var navPoints = new StringBuilder();
        var playOrder = 1;

        if (options.IncludeTitlePage)
        {
            navPoints.AppendLine($"""
                    <navPoint id="title" playOrder="{playOrder}">
                      <navLabel><text>Title Page</text></navLabel>
                      <content src="title.xhtml"/>
                    </navPoint>
                """);
            playOrder++;
        }

        for (var i = 0; i < chapters.Count; i++)
        {
            navPoints.AppendLine($"""
                    <navPoint id="chapter-{i + 1}" playOrder="{playOrder}">
                      <navLabel><text>{EscapeXml(chapters[i].Title)}</text></navLabel>
                      <content src="chapter-{i + 1}.xhtml"/>
                    </navPoint>
                """);
            playOrder++;
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE ncx PUBLIC "-//NISO//DTD ncx 2005-1//EN" "http://www.daisy.org/z3986/2005/ncx-2005-1.dtd">
            <ncx version="2005-1" xmlns="http://www.daisy.org/z3986/2005/ncx/">
              <head>
                <meta name="dtb:uid" content="{EscapeXml(bookId)}"/>
                <meta name="dtb:depth" content="1"/>
                <meta name="dtb:totalPageCount" content="0"/>
                <meta name="dtb:maxPageNumber" content="0"/>
              </head>
              <docTitle><text>{EscapeXml(options.Title)}</text></docTitle>
              <navMap>
            {navPoints}
              </navMap>
            </ncx>
            """;
    }

    private static string GenerateContentOpf(
        List<ChapterExportContent> chapters,
        ExportOptions options,
        string bookId,
        string modifiedDate)
    {
        var manifestItems = new StringBuilder();
        var spineItems = new StringBuilder();

        manifestItems.AppendLine("    <item id=\"css\" href=\"styles.css\" media-type=\"text/css\"/>");
        manifestItems.AppendLine("    <item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>");
        manifestItems.AppendLine("    <item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\"/>");

        if (options.IncludeTitlePage)
        {
            manifestItems.AppendLine("    <item id=\"title\" href=\"title.xhtml\" media-type=\"application/xhtml+xml\"/>");
            spineItems.AppendLine("    <itemref idref=\"title\"/>");
        }

        for (var i = 0; i < chapters.Count; i++)
        {
            var id = $"chapter-{i + 1}";
            manifestItems.AppendLine($"    <item id=\"{id}\" href=\"{id}.xhtml\" media-type=\"application/xhtml+xml\"/>");
            spineItems.AppendLine($"    <itemref idref=\"{id}\"/>");
        }

        var authorXml = !string.IsNullOrWhiteSpace(options.Author)
            ? $"<dc:creator>{EscapeXml(options.Author)}</dc:creator>"
            : "";

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <package version="3.0" xmlns="http://www.idpf.org/2007/opf" unique-identifier="BookId">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="BookId">{EscapeXml(bookId)}</dc:identifier>
                <dc:title>{EscapeXml(options.Title)}</dc:title>
                {authorXml}
                <dc:language>en</dc:language>
                <meta property="dcterms:modified">{modifiedDate}</meta>
              </metadata>
              <manifest>
            {manifestItems}
              </manifest>
              <spine toc="ncx">
            {spineItems}
              </spine>
            </package>
            """;
    }

    // ─── DOCX Export ─────────────────────────────────────────────────

    private static async Task ExportToDocxAsync(
        List<ChapterExportContent> chapters,
        ExportOptions options,
        string outputPath)
    {
        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        var smf = options.SmfPreset;

        // [Content_Types].xml
        var contentTypesExtra = smf
            ? "\n  <Override PartName=\"/word/header1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml\"/>"
            : "";

        await WriteEntryAsync(zip, "[Content_Types].xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>{contentTypesExtra}
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
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>{headerRel}
            </Relationships>
            """);

        // word/styles.xml
        await WriteEntryAsync(zip, "word/styles.xml", GenerateDocxStyles(options));

        // SMF header
        if (smf)
        {
            var surname = !string.IsNullOrWhiteSpace(options.Author)
                ? options.Author.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last()
                : "";
            var shortTitle = options.Title.Length > 30 ? options.Title[..27] + "..." : options.Title;
            var headerText = $"{surname} / {shortTitle.ToUpperInvariant()}";

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

        // Build document body
        var body = new StringBuilder();

        if (options.IncludeTitlePage)
        {
            body.Append($"<w:p><w:pPr><w:pStyle w:val=\"Title\"/></w:pPr><w:r><w:t>{EscapeXml(options.Title)}</w:t></w:r></w:p>");
            if (!string.IsNullOrWhiteSpace(options.Author))
                body.Append($"<w:p><w:pPr><w:pStyle w:val=\"Subtitle\"/></w:pPr><w:r><w:t>{EscapeXml(options.Author)}</w:t></w:r></w:p>");
        }

        for (var i = 0; i < chapters.Count; i++)
        {
            var chapter = chapters[i];
            var needsPageBreak = i > 0 || options.IncludeTitlePage;

            // Chapter heading
            if (needsPageBreak)
                body.Append($"<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/><w:pageBreakBefore/></w:pPr><w:r><w:t>{EscapeXml(chapter.Title)}</w:t></w:r></w:p>");
            else
                body.Append($"<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>{EscapeXml(chapter.Title)}</w:t></w:r></w:p>");

            // Scenes
            for (var si = 0; si < chapter.Scenes.Count; si++)
            {
                // Scene break between scenes
                if (si > 0)
                {
                    body.Append($"<w:p><w:pPr><w:pStyle w:val=\"SceneBreak\"/></w:pPr><w:r><w:t>{SceneBreakText}</w:t></w:r></w:p>");
                }

                var scene = chapter.Scenes[si];
                var paragraphs = ParseHtmlToParagraphs(scene.HtmlContent);
                var isFirstPara = si == 0;

                foreach (var para in paragraphs)
                {
                    var style = isFirstPara ? "NoIndent" : "BodyText";
                    var runs = SegmentsToDocxRuns(para);
                    body.Append($"<w:p><w:pPr><w:pStyle w:val=\"{style}\"/></w:pPr>{runs}</w:p>");
                    isFirstPara = false;
                }
            }
        }

        // Section properties
        var sectPrHeader = smf
            ? "<w:headerReference w:type=\"default\" r:id=\"rId2\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"/>"
            : "";

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

    private static string GenerateDocxStyles(ExportOptions options)
    {
        var smf = options.SmfPreset;
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

              <w:style w:type="paragraph" w:styleId="SceneBreak">
                <w:name w:val="Scene Break"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:jc w:val="center"/>
                  <w:spacing w:before="360" w:after="360"/>
                </w:pPr>
              </w:style>
            </w:styles>
            """;
    }

    private static string SegmentsToDocxRuns(List<InlineSegment> segments)
    {
        var sb = new StringBuilder();
        foreach (var seg in segments)
        {
            var rPr = "";
            if (seg.Bold || seg.Italic)
            {
                var b = seg.Bold ? "<w:b/><w:bCs/>" : "";
                var i = seg.Italic ? "<w:i/><w:iCs/>" : "";
                rPr = $"<w:rPr>{b}{i}</w:rPr>";
            }
            sb.Append($"<w:r>{rPr}<w:t xml:space=\"preserve\">{EscapeXml(seg.Text)}</w:t></w:r>");
        }
        return sb.ToString();
    }

    // ─── PDF Export ──────────────────────────────────────────────────

    private static void ExportToPdf(
        List<ChapterExportContent> chapters,
        ExportOptions options,
        string outputPath)
    {
        var smf = options.SmfPreset;
        var doc = new PdfSharpCore.Pdf.PdfDocument();
        doc.Info.Title = options.Title;
        if (!string.IsNullOrWhiteSpace(options.Author))
            doc.Info.Author = options.Author;

        var pageWidth = PdfSharpCore.Drawing.XUnit.FromInch(8.5);
        var pageHeight = PdfSharpCore.Drawing.XUnit.FromInch(11);
        var margin = PdfSharpCore.Drawing.XUnit.FromInch(1);
        var textWidth = pageWidth - 2 * margin;

        var bodyFontName = smf ? "Courier New" : "Times New Roman";
        var fontSize = 12.0;
        var lineSpacing = smf ? fontSize * 2 : fontSize * 1.5;
        var paragraphGap = smf ? 0.0 : fontSize * 0.8;
        var indent = smf ? PdfSharpCore.Drawing.XUnit.FromInch(0.5) : PdfSharpCore.Drawing.XUnit.FromInch(0.35);
        var chapterTopMargin = smf ? PdfSharpCore.Drawing.XUnit.FromInch(3) : PdfSharpCore.Drawing.XUnit.FromInch(2);

        var bodyFont = new PdfSharpCore.Drawing.XFont(bodyFontName, fontSize);
        var boldFont = new PdfSharpCore.Drawing.XFont(bodyFontName, fontSize, PdfSharpCore.Drawing.XFontStyle.Bold);
        var italicFont = new PdfSharpCore.Drawing.XFont(bodyFontName, fontSize, PdfSharpCore.Drawing.XFontStyle.Italic);
        var boldItalicFont = new PdfSharpCore.Drawing.XFont(bodyFontName, fontSize, PdfSharpCore.Drawing.XFontStyle.BoldItalic);

        var pageNumber = 0;
        var headerY = margin / 2;

        var surname = !string.IsNullOrWhiteSpace(options.Author)
            ? options.Author.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last()
            : "";
        var shortTitle = options.Title.Length > 30 ? options.Title[..27] + "..." : options.Title;

        PdfSharpCore.Drawing.XGraphics NewPage(out double y)
        {
            var page = doc.AddPage();
            page.Width = pageWidth;
            page.Height = pageHeight;
            pageNumber++;
            var gfx = PdfSharpCore.Drawing.XGraphics.FromPdfPage(page);

            if (smf && pageNumber > 1)
            {
                var headerText = $"{surname} / {shortTitle.ToUpperInvariant()} / {pageNumber}";
                var hw = gfx.MeasureString(headerText, new PdfSharpCore.Drawing.XFont(bodyFontName, 10));
                gfx.DrawString(headerText,
                    new PdfSharpCore.Drawing.XFont(bodyFontName, 10),
                    PdfSharpCore.Drawing.XBrushes.Black,
                    new PdfSharpCore.Drawing.XPoint(pageWidth - margin - hw.Width, headerY));
            }

            y = margin + lineSpacing;
            return gfx;
        }

        // Title page
        if (options.IncludeTitlePage)
        {
            var tp = doc.AddPage();
            tp.Width = pageWidth;
            tp.Height = pageHeight;
            pageNumber++;
            var gfx = PdfSharpCore.Drawing.XGraphics.FromPdfPage(tp);

            if (smf)
            {
                if (!string.IsNullOrWhiteSpace(options.Author))
                {
                    gfx.DrawString(options.Author, bodyFont, PdfSharpCore.Drawing.XBrushes.Black,
                        new PdfSharpCore.Drawing.XPoint(margin, margin));
                }

                var centerY = pageHeight / 2;
                var titleUpper = options.Title.ToUpperInvariant();
                var titleW = gfx.MeasureString(titleUpper, bodyFont);
                gfx.DrawString(titleUpper, bodyFont, PdfSharpCore.Drawing.XBrushes.Black,
                    new PdfSharpCore.Drawing.XPoint((pageWidth - titleW.Width) / 2, centerY + lineSpacing));

                if (!string.IsNullOrWhiteSpace(options.Author))
                {
                    var byLine = $"by {options.Author}";
                    var byW = gfx.MeasureString(byLine, bodyFont);
                    gfx.DrawString(byLine, bodyFont, PdfSharpCore.Drawing.XBrushes.Black,
                        new PdfSharpCore.Drawing.XPoint((pageWidth - byW.Width) / 2, centerY - lineSpacing));
                }
            }
            else
            {
                var titleFont = new PdfSharpCore.Drawing.XFont(bodyFontName, 24, PdfSharpCore.Drawing.XFontStyle.Bold);
                var titleW = gfx.MeasureString(options.Title, titleFont);
                gfx.DrawString(options.Title, titleFont, PdfSharpCore.Drawing.XBrushes.Black,
                    new PdfSharpCore.Drawing.XPoint((pageWidth - titleW.Width) / 2, pageHeight * 0.6));

                if (!string.IsNullOrWhiteSpace(options.Author))
                {
                    var authorFont = new PdfSharpCore.Drawing.XFont(bodyFontName, 16, PdfSharpCore.Drawing.XFontStyle.Italic);
                    var authorW = gfx.MeasureString(options.Author, authorFont);
                    gfx.DrawString(options.Author, authorFont, PdfSharpCore.Drawing.XBrushes.Black,
                        new PdfSharpCore.Drawing.XPoint((pageWidth - authorW.Width) / 2, pageHeight * 0.6 - 36));
                }
            }
        }

        // Chapters
        foreach (var chapter in chapters)
        {
            var gfx = NewPage(out var y);
            y = margin + chapterTopMargin;

            // Chapter title
            var chTitleFont = smf ? bodyFont : boldFont;
            var chTitleSize = smf ? fontSize : 18;
            var chTitleFontActual = smf ? bodyFont : new PdfSharpCore.Drawing.XFont(bodyFontName, chTitleSize, PdfSharpCore.Drawing.XFontStyle.Bold);
            var chTitleText = smf ? chapter.Title.ToUpperInvariant() : chapter.Title;
            var ctW = gfx.MeasureString(chTitleText, chTitleFontActual);
            gfx.DrawString(chTitleText, chTitleFontActual, PdfSharpCore.Drawing.XBrushes.Black,
                new PdfSharpCore.Drawing.XPoint((pageWidth - ctW.Width) / 2, y));
            y += lineSpacing * 2;

            // Scenes
            for (var si = 0; si < chapter.Scenes.Count; si++)
            {
                // Scene break
                if (si > 0)
                {
                    y += lineSpacing;
                    if (y > pageHeight - margin - lineSpacing)
                    {
                        gfx.Dispose();
                        gfx = NewPage(out y);
                    }

                    var sbW = gfx.MeasureString(SceneBreakText, bodyFont);
                    gfx.DrawString(SceneBreakText, bodyFont, PdfSharpCore.Drawing.XBrushes.Black,
                        new PdfSharpCore.Drawing.XPoint((pageWidth - sbW.Width) / 2, y));
                    y += lineSpacing;
                }

                var scene = chapter.Scenes[si];
                var paragraphs = ParseHtmlToParagraphs(scene.HtmlContent);
                var isFirstPara = si == 0;

                foreach (var para in paragraphs)
                {
                    var plainText = string.Concat(para.Select(s => s.Text));
                    var paraIndent = smf && !isFirstPara ? (double)indent : 0.0;
                    var lines = WordWrap(plainText, bodyFont, gfx, textWidth - paraIndent);

                    foreach (var line in lines)
                    {
                        if (y > pageHeight - margin - lineSpacing)
                        {
                            gfx.Dispose();
                            gfx = NewPage(out y);
                        }

                        gfx.DrawString(line, bodyFont, PdfSharpCore.Drawing.XBrushes.Black,
                            new PdfSharpCore.Drawing.XPoint(margin + paraIndent, y));
                        paraIndent = 0; // Only indent first line
                        y += lineSpacing;
                    }

                    isFirstPara = false;
                    if (paragraphGap > 0) y += paragraphGap;
                }
            }

            gfx.Dispose();
        }

        doc.Save(outputPath);
    }

    private static List<string> WordWrap(
        string text,
        PdfSharpCore.Drawing.XFont font,
        PdfSharpCore.Drawing.XGraphics gfx,
        double maxWidth)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var currentLine = "";

        foreach (var word in words)
        {
            var testLine = string.IsNullOrEmpty(currentLine) ? word : $"{currentLine} {word}";
            var testWidth = gfx.MeasureString(testLine, font).Width;
            if (testWidth > maxWidth && !string.IsNullOrEmpty(currentLine))
            {
                lines.Add(currentLine);
                currentLine = word;
            }
            else
            {
                currentLine = testLine;
            }
        }

        if (!string.IsNullOrEmpty(currentLine))
            lines.Add(currentLine);

        return lines;
    }

    // ─── Markdown Export ─────────────────────────────────────────────

    private static async Task ExportToMarkdownAsync(
        List<ChapterExportContent> chapters,
        ExportOptions options,
        string outputPath)
    {
        var sb = new StringBuilder();

        if (options.IncludeTitlePage)
        {
            sb.AppendLine($"# {options.Title}");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(options.Author))
            {
                sb.AppendLine($"*{options.Author}*");
                sb.AppendLine();
            }
            sb.AppendLine("---");
            sb.AppendLine();
        }

        for (var i = 0; i < chapters.Count; i++)
        {
            var chapter = chapters[i];

            if (i > 0 || options.IncludeTitlePage)
            {
                sb.AppendLine();
                sb.AppendLine("<div style=\"page-break-after: always;\"></div>");
                sb.AppendLine();
            }

            sb.AppendLine($"## {chapter.Title}");
            sb.AppendLine();

            for (var si = 0; si < chapter.Scenes.Count; si++)
            {
                if (si > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"<p style=\"text-align: center; margin: 1.5em 0;\">{SceneBreakText}</p>");
                    sb.AppendLine();
                }

                var scene = chapter.Scenes[si];
                var paragraphs = ParseHtmlToParagraphs(scene.HtmlContent);

                foreach (var para in paragraphs)
                {
                    var text = SegmentsToMarkdown(para);
                    sb.AppendLine(text);
                    sb.AppendLine();
                }
            }
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8);
    }

    private static string SegmentsToMarkdown(List<InlineSegment> segments)
    {
        var sb = new StringBuilder();
        foreach (var seg in segments)
        {
            if (seg.Bold && seg.Italic)
                sb.Append($"***{seg.Text}***");
            else if (seg.Bold)
                sb.Append($"**{seg.Text}**");
            else if (seg.Italic)
                sb.Append($"*{seg.Text}*");
            else
                sb.Append(seg.Text);
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"<p[^>]*>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphRegex();
}
