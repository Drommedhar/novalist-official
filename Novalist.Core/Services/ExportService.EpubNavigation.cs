using System.IO.Compression;
using System.Text;
using Novalist.Core.Models;
using static Novalist.Core.Services.ExportArchive;

namespace Novalist.Core.Services;

public partial class ExportService
{
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
}
