using System.IO.Compression;
using System.Text;
using Novalist.Core.Models;
using static Novalist.Core.Services.ExportArchive;

namespace Novalist.Core.Services;

public partial class ExportService
{
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
        await WriteEntryAsync(zip, "OEBPS/styles.css", GenerateEpubStylesheet(options));

        // Cover: the image itself plus the XHTML page that displays it. Both are
        // skipped when no cover is set or the file cannot be read, so a missing
        // cover can never fail an export.
        if (CoverMediaType(options.CoverImagePath) != null)
        {
            var ext = Path.GetExtension(options.CoverImagePath);
            await WriteBinaryEntryAsync(zip, $"OEBPS/cover{ext}", options.CoverImagePath);
            await WriteEntryAsync(zip, "OEBPS/cover.xhtml", GenerateCoverXhtml(options, ext));
        }

        // Title page
        if (options.IncludeTitlePage)
            await WriteEntryAsync(zip, "OEBPS/title.xhtml", GenerateTitlePageXhtml(options));

        // Matter pages get their own files, each carrying its kind as an
        // epub:type so a reader can style a copyright page as one.
        for (var i = 0; i < options.Matter.Count; i++)
            await WriteEntryAsync(zip, $"OEBPS/matter-{i + 1}.xhtml", GenerateMatterXhtml(options.Matter[i]));

        // Images used in the prose. Copied once each however many chapters
        // reference them, and named by position rather than by file name so a
        // path with spaces or non-ASCII cannot produce an unopenable package.
        var images = CollectProseImages(chapters);
        foreach (var (absolute, href) in images)
            await WriteBinaryEntryAsync(zip, $"OEBPS/{href}", absolute);

        // Chapter files
        for (var i = 0; i < chapters.Count; i++)
            await WriteEntryAsync(
                zip, $"OEBPS/chapter-{i + 1}.xhtml",
                GenerateChapterXhtml(chapters[i], options, i + 1, images));

        // Navigation
        await WriteEntryAsync(zip, "OEBPS/nav.xhtml", GenerateNavXhtml(chapters, options));
        await WriteEntryAsync(zip, "OEBPS/toc.ncx", GenerateTocNcx(chapters, options, bookId));
        await WriteEntryAsync(
            zip, "OEBPS/content.opf",
            GenerateContentOpf(chapters, options, bookId, modifiedDate, images));
    }

    /// <summary>
    /// Reduces a writing-language setting to a BCP-47 primary subtag fit for
    /// <c>dc:language</c>. The quote-style presets carry typographic variants
    /// ("de-low", "de-guillemet") that are not language tags, so only the part
    /// before the first hyphen is kept. Falls back to "en" on anything unusable.
    /// </summary>
    public static string NormalizeLanguageTag(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return "en";

        var primary = language.Trim().Split('-')[0].ToLowerInvariant();
        return primary.Length is >= 2 and <= 3 && primary.All(char.IsAsciiLetter) ? primary : "en";
    }

    /// <summary>
    /// EPUB media type for a cover image, or null when there is no usable cover:
    /// no path set, the file is gone, or the extension is not one readers accept.
    /// Callers use null as "export without a cover" rather than as an error.
    /// </summary>
    internal static string? CoverMediaType(string? coverPath)
    {
        if (string.IsNullOrWhiteSpace(coverPath) || !File.Exists(coverPath))
            return null;

        return Path.GetExtension(coverPath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => null
        };
    }

    /// <summary>
    /// Full-bleed cover page. Uses svg preserveAspectRatio rather than a plain
    /// img so the image scales to the reader's screen without distortion, which
    /// is the shape Kindle and Apple Books both expect.
    /// </summary>
    private static string GenerateCoverXhtml(ExportOptions options, string extension)
    {
        // $$ raw string: interpolation holes are {{...}}, so the CSS braces below
        // stay literal instead of being parsed as format specifiers.
        return $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
            <head>
              <title>{{EscapeXml(options.Title)}}</title>
              <style type="text/css">
                body { margin: 0; padding: 0; text-align: center; }
                svg { height: 100%; width: 100%; }
              </style>
            </head>
            <body epub:type="cover">
              <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"
                   version="1.1" viewBox="0 0 100 160" preserveAspectRatio="xMidYMid meet">
                <image width="100" height="160" xlink:href="cover{{extension}}"/>
              </svg>
            </body>
            </html>
            """;
    }

    private static string GenerateEpubStylesheet(ExportOptions options)
    {
        var preset = options.ResolvePreset();
        // Appended rather than merged, so the writer's rules win by cascade
        // order - which is the only way they can override anything above.
        var extra = string.IsNullOrWhiteSpace(preset.EbookCss)
            ? string.Empty
            : "\n\n/* From your export layout */\n" + preset.EbookCss;
        // Concatenated rather than interpolated: the stylesheet below is full of
        // CSS braces, every one of which an interpolated string reads as a hole.
        return BaseEpubStylesheet + extra;
    }

    private const string BaseEpubStylesheet = """
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

            p.chapter-subtitle {
              text-align: center;
              text-indent: 0;
              font-style: italic;
              margin-top: -0.6em;
              margin-bottom: 1.6em;
            }

            span.drop-cap {
              float: left;
              font-size: 3.2em;
              line-height: 0.85;
              padding-right: 0.06em;
            }

            span.lead-in {
              font-variant: small-caps;
            }

            p.prose-image {
              text-align: center;
              text-indent: 0;
              margin: 1.5em 0;
            }

            p.prose-image img {
              max-width: 100%;
            }

            h2, h3 {
              text-align: left;
              margin-top: 1.6em;
              margin-bottom: 0.6em;
            }

            blockquote {
              margin: 1em 2em;
              font-style: italic;
            }

            blockquote p {
              text-align: left;
              text-indent: 0;
            }

            /* Verse keeps its own line breaks and is never justified, which
               would stretch a short line across the page. */
            p.poetry {
              margin: 0 0 0.2em 2em;
              text-align: left;
              text-indent: 0;
              white-space: pre-wrap;
            }

            ul, ol {
              margin: 0.8em 0 0.8em 2em;
              padding: 0;
            }

            li {
              margin-bottom: 0.3em;
              text-align: left;
            }

            p.series, p.publisher {
              text-align: center;
              font-style: italic;
              margin: 0.4em 0;
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

    /// <summary>
    /// The chapter's opening paragraph, with the initial set as a drop cap and
    /// the words after it in small capitals. Anything the splitter will not
    /// take - markup first, a number, an opening quotation mark - is returned
    /// untouched rather than wrapped into something odd.
    /// </summary>
    private static string OpenerXhtml(string content, bool isOpener, ExportPreset preset)
    {
        if (!isOpener || !preset.DropCap) return content;
        var split = SplitOpener(content, preset.LeadInSmallCapsWords);
        if (split == null) return content;

        var (initial, leadIn, tail) = split.Value;
        var lead = leadIn.Length > 0 ? $"<span class=\"lead-in\">{leadIn}</span>" : string.Empty;
        return $"<span class=\"drop-cap\">{initial}</span>{lead}{tail}";
    }

    /// <summary>
    /// The chapter's heading block: nothing at all when the chapter hides it,
    /// otherwise the title and, under it, whatever subtitle it carries.
    /// </summary>
    private static string ChapterHeadingXhtml(
        ChapterExportContent chapter, ExportPreset preset, int number)
    {
        if (chapter.HideHeading) return string.Empty;

        var heading = $"    <h1 class=\"chapter-title\">{EscapeXml(chapter.Heading)}</h1>";
        return string.IsNullOrWhiteSpace(chapter.Subtitle)
            ? heading
            : heading + $"\n    <p class=\"chapter-subtitle\">{EscapeXml(chapter.Subtitle)}</p>";
    }

    /// <summary>
    /// Every prose image in the book, mapped to the href it will have inside
    /// the package. Files that are missing are left out rather than producing
    /// a manifest entry pointing at nothing.
    /// </summary>
    private static Dictionary<string, string> CollectProseImages(
        List<ChapterExportContent> chapters)
    {
        var images = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scene in chapters.SelectMany(c => c.Scenes))
        {
            foreach (var block in ParseHtmlToBlocks(scene.HtmlContent, scene.Footnotes))
            {
                if (block.ImagePath == null || images.ContainsKey(block.ImagePath)) continue;
                if (!File.Exists(block.ImagePath)) continue;
                var ext = Path.GetExtension(block.ImagePath).ToLowerInvariant();
                images[block.ImagePath] = $"images/image-{images.Count + 1}{ext}";
            }
        }
        return images;
    }

    private static string GenerateChapterXhtml(
        ChapterExportContent chapter, ExportOptions options, int number,
        IReadOnlyDictionary<string, string>? images = null)
    {
        var preset = options.ResolvePreset();
        var bodyHtml = new StringBuilder();
        // Notes are collected as the chapter is laid out and written as asides
        // at its end, which is where a reading system looks for the target of
        // a noteref it has to pop up.
        var footnoteDefs = new List<string>();
        for (var si = 0; si < chapter.Scenes.Count; si++)
        {
            if (si > 0)
                bodyHtml.AppendLine(
                    $"    <p class=\"scene-break\">{EscapeXml(options.ResolvedSeparator)}</p>");

            var scene = chapter.Scenes[si];
            // Off for a novel, where an ornament is the whole separator; on for
            // a collection, where the titles are how a reader navigates.
            if (preset.ShowSceneTitles && !string.IsNullOrWhiteSpace(scene.Title))
                bodyHtml.AppendLine(
                    $"    <h3 class=\"scene-title\" id=\"scene-{si + 1}\">"
                    + $"{EscapeXml(scene.Title)}</h3>");
            else if (options.EffectiveTocDepth >= 2)
                // Somewhere for a contents entry to land when the layout prints
                // no scene heading - which is every built-in layout. Without
                // this, choosing "chapters and scenes" would silently do
                // nothing on a novel.
                bodyHtml.AppendLine($"    <span id=\"scene-{si + 1}\"></span>");

            var isFirst = si == 0;
            var openList = ListKind.None;

            foreach (var block in ParseHtmlToBlocks(scene.HtmlContent, scene.Footnotes))
            {
                if (block.ImagePath != null)
                {
                    // An image whose file has gone is dropped rather than
                    // written as a broken reference the reader has to see.
                    if (images == null || !images.TryGetValue(block.ImagePath, out var href)) continue;
                    bodyHtml.AppendLine(
                        $"    <p class=\"prose-image\"><img src=\"{EscapeXml(href)}\" alt=\"{EscapeXml(block.ImageAlt)}\"/></p>");
                    continue;
                }

                var content = SegmentsToXhtml(block.Segments, footnoteDefs);

                if (block.List != openList)
                {
                    if (openList != ListKind.None)
                        bodyHtml.AppendLine(openList == ListKind.Number ? "    </ol>" : "    </ul>");
                    if (block.List != ListKind.None)
                        bodyHtml.AppendLine(block.List == ListKind.Number ? "    <ol>" : "    <ul>");
                    openList = block.List;
                }

                if (block.List != ListKind.None)
                {
                    bodyHtml.AppendLine($"      <li>{content}</li>");
                    isFirst = false;
                    continue;
                }

                // Real heading and blockquote elements rather than styled
                // paragraphs, so a reading system's navigation and its quote
                // styling both work on them.
                bodyHtml.AppendLine(block.StyleId switch
                {
                    "heading" => $"    <h2>{content}</h2>",
                    "subheading" => $"    <h3>{content}</h3>",
                    "blockquote" => $"    <blockquote><p>{content}</p></blockquote>",
                    "poetry" => $"    <p class=\"poetry\">{content}</p>",
                    _ => $"    <p{(isFirst ? " class=\"no-indent\"" : "")}>{OpenerXhtml(content, isFirst && si == 0, preset)}</p>"
                });
                isFirst = false;
            }

            if (openList != ListKind.None)
                bodyHtml.AppendLine(openList == ListKind.Number ? "    </ol>" : "    </ul>");
        }

        if (footnoteDefs.Count > 0)
        {
            bodyHtml.AppendLine("    <section epub:type=\"footnotes\" class=\"footnotes\">");
            for (var n = 1; n <= footnoteDefs.Count; n++)
                bodyHtml.AppendLine(
                    $"      <aside epub:type=\"footnote\" id=\"fn{n}\"><p>"
                    + $"<a href=\"#fnref{n}\">{n}.</a> {EscapeXml(footnoteDefs[n - 1])}</p></aside>");
            bodyHtml.AppendLine("    </section>");
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" xml:lang="{EscapeXml(options.Language)}">
            <head>
              <meta charset="UTF-8"/>
              <title>{EscapeXml(chapter.Title)}</title>
              <link rel="stylesheet" type="text/css" href="styles.css"/>
            </head>
            <body>
              <section epub:type="chapter">
                {ChapterHeadingXhtml(chapter, preset, number)}
            {bodyHtml}
              </section>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// EPUB 3 notes: the anchor becomes a <c>noteref</c> link, and the note
    /// itself an <c>aside</c> a reader can show as a popup. Numbering runs
    /// across the chapter file the notes are collected into.
    /// </summary>
    private static string SegmentsToXhtml(
        List<InlineSegment> segments, List<string>? footnoteDefs = null)
    {
        var sb = new StringBuilder();
        foreach (var seg in segments)
        {
            if (seg.FootnoteText != null)
            {
                if (footnoteDefs == null) continue;
                footnoteDefs.Add(seg.FootnoteText);
                var n = footnoteDefs.Count;
                sb.Append($"<a class=\"noteref\" epub:type=\"noteref\" id=\"fnref{n}\" href=\"#fn{n}\"><sup>{n}</sup></a>");
                continue;
            }
            var text = EscapeXml(seg.Text);
            if (seg.Strike) text = $"<s>{text}</s>";
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

    /// <summary>
    /// One matter page as XHTML. The kind rides along as an <c>epub:type</c> and
    /// a class, which is what lets a reader or a stylesheet set a copyright page
    /// differently from an epigraph without guessing from the heading.
    /// </summary>
    private static string GenerateMatterXhtml(MatterExportContent matter)
    {
        var cssClass = "matter matter-" + matter.Kind.ToLowerInvariant();
        var heading = string.IsNullOrEmpty(matter.Title)
            ? string.Empty
            : $"<h1 class=\"matter-title\">{EscapeXml(matter.Title)}</h1>";

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
            <head>
              <title>{EscapeXml(string.IsNullOrEmpty(matter.Title) ? matter.Kind : matter.Title)}</title>
              <link rel="stylesheet" type="text/css" href="styles.css"/>
            </head>
            <body class="{cssClass}" epub:type="{EpubTypeFor(matter.Kind)}">
              {heading}
              {matter.HtmlContent}
            </body>
            </html>
            """;
    }

    /// <summary>
    /// EPUB structural semantics vocabulary name for a matter kind. Kinds with
    /// no standard term fall back to "frontmatter"/"backmatter", which is always
    /// valid.
    /// </summary>
    private static string EpubTypeFor(string kind) => kind switch
    {
        "HalfTitle" => "halftitlepage",
        "TitlePage" => "titlepage",
        "Copyright" => "copyright-page",
        "Dedication" => "dedication",
        "Epigraph" => "epigraph",
        "TableOfContents" => "toc",
        "Foreword" => "foreword",
        "Preface" => "preface",
        "Prologue" => "prologue",
        "Epilogue" => "epilogue",
        "Afterword" => "afterword",
        "Acknowledgments" => "acknowledgments",
        _ => "frontmatter"
    };

    private static string GenerateTitlePageXhtml(ExportOptions options)
    {
        var authorHtml = !string.IsNullOrWhiteSpace(options.Author)
            ? $"<p class=\"author\">{EscapeXml(options.Author)}</p>"
            : "";

        // "Book Two of The Ravens" under the title, the way a printed series
        // states it. Only when there is a series to state.
        var seriesHtml = string.IsNullOrWhiteSpace(options.Publishing.SeriesName)
            ? ""
            : $"<p class=\"series\">{EscapeXml(SeriesLine(options.Publishing))}</p>";

        var publisherHtml = string.IsNullOrWhiteSpace(options.Publishing.Publisher)
            ? ""
            : $"<p class=\"publisher\">{EscapeXml(options.Publishing.Publisher.Trim())}</p>";

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" xml:lang="{EscapeXml(options.Language)}">
            <head>
              <meta charset="UTF-8"/>
              <title>{EscapeXml(options.Title)}</title>
              <link rel="stylesheet" type="text/css" href="styles.css"/>
            </head>
            <body>
              <div class="title-page" epub:type="titlepage">
                <h1>{EscapeXml(options.Title)}</h1>
                {seriesHtml}
                {authorHtml}
                {publisherHtml}
              </div>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// The series line for a title page. "The Ravens, Book 2" when there is a
    /// position, the series name alone when there is not - a book whose place in
    /// its series the writer has not decided still belongs to the series.
    /// </summary>
    internal static string SeriesLine(Models.PublishingMetadata publishing)
    {
        var name = publishing.SeriesName.Trim();
        var position = publishing.SeriesPosition.Trim();
        return position.Length > 0 ? $"{name}, Book {position}" : name;
    }

    private static string GenerateNavXhtml(List<ChapterExportContent> chapters, ExportOptions options)
    {
        var items = new StringBuilder();
        if (options.IncludeTitlePage)
            items.AppendLine(
                $"      <li><a href=\"title.xhtml\">{EscapeXml(Label(options, "titlePage", "Title Page"))}</a></li>");

        // Only matter the writer marked for the contents is listed. A copyright
        // page in the table of contents is a mistake, not a feature.
        void ListMatter(string placement)
        {
            for (var i = 0; i < options.Matter.Count; i++)
            {
                var matter = options.Matter[i];
                if (!matter.InTableOfContents
                    || !string.Equals(matter.Placement, placement, StringComparison.Ordinal))
                    continue;

                var label = string.IsNullOrEmpty(matter.Title)
                    ? SpaceCamelCase(matter.Kind)
                    : matter.Title;
                items.AppendLine($"      <li><a href=\"matter-{i + 1}.xhtml\">{EscapeXml(label)}</a></li>");
            }
        }

        ListMatter("Front");

        // A box set nests: the volume divider is the parent and every chapter
        // after it belongs to it, until the next volume. Without this a reader
        // gets eighty chapters in one flat list and no way to tell where one
        // book ends.
        var inVolume = false;
        for (var i = 0; i < chapters.Count; i++)
        {
            var href = $"chapter-{i + 1}.xhtml";
            var indent = inVolume && !chapters[i].IsVolume ? "    " : string.Empty;

            if (chapters[i].IsVolume)
            {
                if (inVolume) CloseVolume(items);
                items.AppendLine($"      <li><a href=\"{href}\">{EscapeXml(chapters[i].Title)}</a>");
                items.AppendLine("        <ol>");
                inVolume = true;
                continue;
            }

            var nested = NavigableScenes(chapters[i], options);
            items.Append($"{indent}      <li><a href=\"{href}\">{EscapeXml(chapters[i].Title)}</a>");
            if (nested.Count > 0)
            {
                items.AppendLine();
                items.AppendLine($"{indent}        <ol>");
                foreach (var (number, title) in nested)
                    items.AppendLine(
                        $"{indent}          <li><a href=\"{href}#scene-{number}\">{EscapeXml(title)}</a></li>");
                items.AppendLine($"{indent}        </ol>");
                items.AppendLine($"{indent}      </li>");
            }
            else
            {
                items.AppendLine("</li>");
            }
        }
        if (inVolume) CloseVolume(items);

        ListMatter("Back");

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" xml:lang="{EscapeXml(options.Language)}">
            <head>
              <meta charset="UTF-8"/>
              <title>{EscapeXml(TocHeading(options))}</title>
            </head>
            <body>
              <nav epub:type="toc" id="toc">
                <h1>{EscapeXml(TocHeading(options))}</h1>
                <ol>
            {items}
                </ol>
              </nav>
            </body>
            </html>
            """;
    }

    /// <summary>Ends a volume's nested list in the contents.</summary>
    private static void CloseVolume(StringBuilder items)
    {
        items.AppendLine("        </ol>");
        items.AppendLine("      </li>");
    }

    /// <summary>
    /// The heading the contents page carries. English only when the writer said
    /// nothing, because a hardcoded "Table of Contents" on a German book is the
    /// one line of a book nobody can edit.
    /// </summary>
    private static string TocHeading(ExportOptions options)
        => string.IsNullOrWhiteSpace(options.TocTitle)
            ? Label(options, "tableOfContents", "Table of Contents")
            : options.TocTitle.Trim();

    /// <summary>
    /// The scenes of a chapter that belong in the contents, as (number, title).
    ///
    /// A titled scene qualifies whether or not the layout prints that title:
    /// writers name scenes in the binder for themselves, and those names are
    /// exactly what belongs in a contents list. An untitled scene is skipped -
    /// there is nothing to call it, and "Scene 3" is noise, not navigation.
    /// </summary>
    private static List<(int Number, string Title)> NavigableScenes(
        ChapterExportContent chapter, ExportOptions options)
    {
        var listed = new List<(int, string)>();
        if (options.EffectiveTocDepth < 2) return listed;

        for (var i = 0; i < chapter.Scenes.Count; i++)
            if (!string.IsNullOrWhiteSpace(chapter.Scenes[i].Title))
                listed.Add((i + 1, chapter.Scenes[i].Title));
        return listed;
    }

    private static string GenerateTocNcx(List<ChapterExportContent> chapters, ExportOptions options, string bookId)
    {
        var navPoints = new StringBuilder();
        var playOrder = 1;

        if (options.IncludeTitlePage)
        {
            navPoints.AppendLine($"""
                    <navPoint id="title" playOrder="{playOrder}">
                      <navLabel><text>{EscapeXml(Label(options, "titlePage", "Title Page"))}</text></navLabel>
                      <content src="title.xhtml"/>
                    </navPoint>
                """);
            playOrder++;
        }

        var deepest = 1;
        for (var i = 0; i < chapters.Count; i++)
        {
            var nested = NavigableScenes(chapters[i], options);
            var inner = new StringBuilder();
            foreach (var (number, title) in nested)
            {
                playOrder++;
                deepest = 2;
                inner.AppendLine($"""
                        <navPoint id="chapter-{i + 1}-scene-{number}" playOrder="{playOrder}">
                          <navLabel><text>{EscapeXml(title)}</text></navLabel>
                          <content src="chapter-{i + 1}.xhtml#scene-{number}"/>
                        </navPoint>
                    """);
            }

            navPoints.AppendLine($"""
                    <navPoint id="chapter-{i + 1}" playOrder="{playOrder - nested.Count}">
                      <navLabel><text>{EscapeXml(chapters[i].Title)}</text></navLabel>
                      <content src="chapter-{i + 1}.xhtml"/>
                    {inner.ToString().TrimEnd()}
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
                <meta name="dtb:depth" content="{deepest}"/>
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

    /// <summary>
    /// What a reading system and a retailer need to know about how accessible
    /// this book is. Declared from what the file actually contains rather than
    /// asserted: a book with an undescribed picture says so, because claiming
    /// alt text that is not there is worse than claiming nothing.
    /// </summary>
    private static string AccessibilityMetadataXml(IReadOnlyDictionary<string, string>? images)
    {
        var hasImages = images is { Count: > 0 };
        var features = new List<string> { "structuralNavigation", "tableOfContents" };
        if (hasImages) features.Add("alternativeText");

        var lines = new List<string>
        {
            "    <meta property=\"schema:accessMode\">textual</meta>"
        };
        if (hasImages) lines.Add("    <meta property=\"schema:accessMode\">visual</meta>");
        lines.Add("    <meta property=\"schema:accessModeSufficient\">textual</meta>");
        foreach (var feature in features)
            lines.Add($"    <meta property=\"schema:accessibilityFeature\">{feature}</meta>");
        lines.Add("    <meta property=\"schema:accessibilityHazard\">none</meta>");
        lines.Add(
            "    <meta property=\"schema:accessibilitySummary\">"
            + (hasImages
                ? "Reflowable text with a table of contents. Images carry the descriptions the author wrote."
                : "Reflowable text with a table of contents, and no images to describe.")
            + "</meta>");
        return string.Join("\n", lines);
    }

    private static string GenerateContentOpf(
        List<ChapterExportContent> chapters,
        ExportOptions options,
        string bookId,
        string modifiedDate,
        IReadOnlyDictionary<string, string>? images = null)
    {
        var manifestItems = new StringBuilder();
        var spineItems = new StringBuilder();

        manifestItems.AppendLine("    <item id=\"css\" href=\"styles.css\" media-type=\"text/css\"/>");
        manifestItems.AppendLine("    <item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>");
        manifestItems.AppendLine("    <item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\"/>");

        // Prose images are manifested but never spined: they are shown inside a
        // chapter, not read as a document of their own.
        var imageIndex = 0;
        foreach (var href in images?.Values ?? [])
        {
            imageIndex++;
            var media = CoverMediaType(href) ?? "image/png";
            manifestItems.AppendLine(
                $"    <item id=\"prose-image-{imageIndex}\" href=\"{href}\" media-type=\"{media}\"/>");
        }

        if (options.IncludeTitlePage)
        {
            manifestItems.AppendLine("    <item id=\"title\" href=\"title.xhtml\" media-type=\"application/xhtml+xml\"/>");
            spineItems.AppendLine("    <itemref idref=\"title\"/>");
        }

        // Front matter precedes the story in the spine; back matter follows it.
        void AppendMatter(string placement)
        {
            for (var i = 0; i < options.Matter.Count; i++)
            {
                if (!string.Equals(options.Matter[i].Placement, placement, StringComparison.Ordinal))
                    continue;

                var matterId = $"matter-{i + 1}";
                manifestItems.AppendLine(
                    $"    <item id=\"{matterId}\" href=\"{matterId}.xhtml\" media-type=\"application/xhtml+xml\"/>");
                spineItems.AppendLine($"    <itemref idref=\"{matterId}\"/>");
            }
        }

        AppendMatter("Front");

        for (var i = 0; i < chapters.Count; i++)
        {
            var id = $"chapter-{i + 1}";
            manifestItems.AppendLine($"    <item id=\"{id}\" href=\"{id}.xhtml\" media-type=\"application/xhtml+xml\"/>");
            spineItems.AppendLine($"    <itemref idref=\"{id}\"/>");
        }

        AppendMatter("Back");

        var authorXml = !string.IsNullOrWhiteSpace(options.Author)
            ? $"<dc:creator>{EscapeXml(options.Author)}</dc:creator>"
            : "";

        var coverMetaXml = "";
        if (CoverMediaType(options.CoverImagePath) is { } coverMedia)
        {
            var ext = Path.GetExtension(options.CoverImagePath);
            manifestItems.AppendLine(
                $"    <item id=\"cover-image\" href=\"cover{ext}\" media-type=\"{coverMedia}\" properties=\"cover-image\"/>");
            manifestItems.AppendLine(
                "    <item id=\"cover\" href=\"cover.xhtml\" media-type=\"application/xhtml+xml\"/>");
            // The cover page goes first in the spine, ahead of the title page.
            spineItems.Insert(0, "    <itemref idref=\"cover\" linear=\"no\"/>" + Environment.NewLine);
            // EPUB 2 style pointer: EPUB 3 uses properties="cover-image", but
            // Kindle and several retailers still key off this meta tag.
            coverMetaXml = "<meta name=\"cover\" content=\"cover-image\"/>";
        }

        var language = string.IsNullOrWhiteSpace(options.Language) ? "en" : options.Language.Trim();

        // An ISBN is the identifier a retailer keys on, so when there is one it
        // becomes the package's unique identifier rather than sitting beside a
        // generated UUID that means nothing outside this file.
        var publishing = options.Publishing;
        var isbn = publishing.NormalizedIsbn();
        var identifierXml = isbn.Length > 0
            ? $"<dc:identifier id=\"BookId\">urn:isbn:{EscapeXml(isbn)}</dc:identifier>"
            : $"<dc:identifier id=\"BookId\">{EscapeXml(bookId)}</dc:identifier>";

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <package version="3.0" xmlns="http://www.idpf.org/2007/opf" unique-identifier="BookId">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:schema="http://schema.org/">
                {identifierXml}
                <dc:title>{EscapeXml(options.Title)}</dc:title>
                {authorXml}
                <dc:language>{EscapeXml(language)}</dc:language>
            {PublishingMetadataXml(publishing)}
                {coverMetaXml}
                <meta property="dcterms:modified">{modifiedDate}</meta>
            {AccessibilityMetadataXml(images)}
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

    /// <summary>
    /// The optional Dublin Core elements, plus the EPUB 3 collection markup that
    /// states a book's place in its series.
    ///
    /// Series is the one that is not a plain element: EPUB 3 expresses it as a
    /// <c>belongs-to-collection</c> meta refined by <c>collection-type</c> and
    /// <c>group-position</c>. Retailers that do not read it fall back to the
    /// title, which is why the book still reads correctly without it.
    /// </summary>
    private static string PublishingMetadataXml(Models.PublishingMetadata publishing)
    {
        if (!publishing.HasAny) return string.Empty;

        var sb = new StringBuilder();
        void Element(string name, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                sb.AppendLine($"    <dc:{name}>{EscapeXml(value.Trim())}</dc:{name}>");
        }

        Element("publisher", publishing.Publisher);
        Element("description", publishing.Description);
        Element("rights", publishing.Rights);
        Element("date", publishing.PublicationDate);

        foreach (var subject in publishing.Subjects)
            Element("subject", subject);

        if (!string.IsNullOrWhiteSpace(publishing.SeriesName))
        {
            sb.AppendLine(
                $"    <meta property=\"belongs-to-collection\" id=\"series\">{EscapeXml(publishing.SeriesName.Trim())}</meta>");
            sb.AppendLine(
                "    <meta refines=\"#series\" property=\"collection-type\">series</meta>");
            if (!string.IsNullOrWhiteSpace(publishing.SeriesPosition))
                sb.AppendLine(
                    $"    <meta refines=\"#series\" property=\"group-position\">{EscapeXml(publishing.SeriesPosition.Trim())}</meta>");
        }

        // An ISBN promoted to the package identifier is still worth stating as
        // its own element: some ingestion pipelines look for the scheme-tagged
        // form rather than parsing the urn.
        var isbn = publishing.NormalizedIsbn();
        if (isbn.Length > 0)
            sb.AppendLine(
                $"    <dc:identifier opf:scheme=\"ISBN\" xmlns:opf=\"http://www.idpf.org/2007/opf\">{EscapeXml(isbn)}</dc:identifier>");

        return sb.ToString().TrimEnd();
    }
}
