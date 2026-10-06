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
}
