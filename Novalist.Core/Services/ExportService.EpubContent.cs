using System.IO.Compression;
using System.Text;
using Novalist.Core.Models;
using static Novalist.Core.Services.ExportArchive;

namespace Novalist.Core.Services;

public partial class ExportService
{
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
        AppendChapterScenesXhtml(bodyHtml, chapter, options, footnoteDefs, images);

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

    private static void AppendChapterScenesXhtml(StringBuilder bodyHtml, ChapterExportContent chapter, ExportOptions options, List<string> footnoteDefs, IReadOnlyDictionary<string, string>? images)
    {
        var preset = options.ResolvePreset();
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

    }
}
