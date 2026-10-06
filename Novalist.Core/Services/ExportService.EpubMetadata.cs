using System.IO.Compression;
using System.Text;
using Novalist.Core.Models;
using static Novalist.Core.Services.ExportArchive;

namespace Novalist.Core.Services;

public partial class ExportService
{
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
        var (manifestItems, spineItems) = ContentManifest(chapters, options, images);

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

    private static (StringBuilder Manifest, StringBuilder Spine) ContentManifest(List<ChapterExportContent> chapters, ExportOptions options, IReadOnlyDictionary<string, string>? images)
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

        return (manifestItems, spineItems);
    }
}
