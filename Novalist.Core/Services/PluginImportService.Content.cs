using System.Text.Json;
using System.Text.RegularExpressions;
using Markdig;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public partial class PluginImportService
{
    /// <summary>
    /// Remaps entity image paths from vault-relative to book-relative,
    /// and copies individually referenced images that weren't part of the bulk copy.
    /// </summary>
    private void RemapAndCopyEntityImages(
        IEnumerable<EntityImage> images,
        string vaultRoot,
        string sourceRootRelative,
        string sourceImageFolderName,
        string destImageFolderName,
        string destImageDir)
    {
        string[] imageExts = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".svg", ".webp"];

        foreach (var img in images)
        {
            if (string.IsNullOrEmpty(img.Path)) continue;

            var originalPath = img.Path;

            // Normalize slashes
            var imgPath = img.Path.Replace('\\', '/');

            // Strip the project subfolder prefix if present
            // e.g. "Novel/Images/pic.png" → "Images/pic.png"
            if (sourceRootRelative != "." && !string.IsNullOrEmpty(sourceRootRelative) &&
                imgPath.StartsWith(sourceRootRelative + "/", StringComparison.OrdinalIgnoreCase))
            {
                imgPath = imgPath[(sourceRootRelative.Length + 1)..];
            }

            // Remap the source image folder name to the destination folder name
            // e.g. if source uses "Bilder" but standalone uses "Images"
            if (!string.Equals(sourceImageFolderName, destImageFolderName, StringComparison.OrdinalIgnoreCase) &&
                imgPath.StartsWith(sourceImageFolderName + "/", StringComparison.OrdinalIgnoreCase))
            {
                imgPath = destImageFolderName + imgPath[sourceImageFolderName.Length..];
            }

            // If the path doesn't start with the image folder, it's a bare filename
            // (Obsidian resolves filenames globally). Search the already-copied
            // destination images directory for a matching filename.
            if (!imgPath.StartsWith(destImageFolderName + "/", StringComparison.OrdinalIgnoreCase))
            {
                var fileName = Path.GetFileName(imgPath);
                string? foundRelative = null;

                // Search the destination images directory recursively for the filename
                if (Directory.Exists(destImageDir))
                {
                    var matches = Directory.GetFiles(destImageDir, fileName, SearchOption.AllDirectories);
                    if (matches.Length > 0)
                    {
                        foundRelative = Path.Combine(destImageFolderName, Path.GetRelativePath(destImageDir, matches[0])).Replace('\\', '/');
                        LogLine($"[ImageRemap] '{originalPath}' → '{foundRelative}' (found in copied images)");
                    }
                }

                if (foundRelative != null)
                {
                    imgPath = foundRelative;
                }
                else
                {
                    // Fall back: try to find the file in the vault using the original path
                    var vaultFile = Path.Combine(vaultRoot, img.Path.Replace('/', Path.DirectorySeparatorChar));
                    LogLine($"[ImageRemap] '{originalPath}' → '{imgPath}' (not found in copied images, trying vault: {vaultFile} exists={File.Exists(vaultFile)})");
                    if (File.Exists(vaultFile))
                    {
                        var ext = Path.GetExtension(vaultFile).ToLowerInvariant();
                        if (imageExts.Contains(ext))
                        {
                            Directory.CreateDirectory(destImageDir);
                            var destFile = Path.Combine(destImageDir, Path.GetFileName(vaultFile));
                            if (!File.Exists(destFile))
                                File.Copy(vaultFile, destFile);
                            imgPath = destImageFolderName + "/" + Path.GetFileName(vaultFile);
                        }
                    }
                }
            }

            LogLine($"[ImageRemap] '{originalPath}' → '{imgPath}'");
            img.Path = imgPath;
        }
    }

    internal static string ConvertMarkdownToHtml(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return "<html><head></head><body><p></p></body></html>";

        var pipeline = new MarkdownPipelineBuilder().UseEmphasisExtras().Build();
        var bodyHtml = Markdown.ToHtml(markdown, pipeline);

        // The AvRichTextBox only supports <p> with inline <span style="..."> for
        // bold/italic/underline/strikethrough. Convert Markdig output accordingly.
        bodyHtml = SanitizeHtmlForRichTextBox(bodyHtml);

        return $"<html><head></head><body>{bodyHtml}</body></html>";
    }

    /// <summary>
    /// Converts Markdig HTML output to the subset supported by AvRichTextBox:
    /// only &lt;p&gt; elements with &lt;span style="..."&gt; for formatting.
    /// </summary>
    internal static string SanitizeHtmlForRichTextBox(string html)
    {
        // Convert <strong>...</strong> to <span style="font-weight:bold">...</span>
        html = Regex.Replace(html, @"<strong>(.*?)</strong>", @"<span style=""font-weight:bold"">$1</span>", RegexOptions.Singleline);

        // Convert <em>...</em> to <span style="font-style:italic">...</span>
        html = Regex.Replace(html, @"<em>(.*?)</em>", @"<span style=""font-style:italic"">$1</span>", RegexOptions.Singleline);

        // Convert <del>...</del> to <span style="text-decoration:line-through">...</span>
        html = Regex.Replace(html, @"<del>(.*?)</del>", @"<span style=""text-decoration:line-through"">$1</span>", RegexOptions.Singleline);

        // Convert list items to paragraphs: <li>...</li> → <p>...</p>
        // Prefix with bullet character for unordered lists
        html = Regex.Replace(html, @"<li>\s*<p>(.*?)</p>\s*</li>", "<p>\u2022 $1</p>", RegexOptions.Singleline);
        html = Regex.Replace(html, @"<li>(.*?)</li>", "<p>\u2022 $1</p>", RegexOptions.Singleline);

        // Strip list wrapper tags
        html = Regex.Replace(html, @"</?[uo]l[^>]*>", "", RegexOptions.IgnoreCase);

        // Convert headings to bold paragraphs
        html = Regex.Replace(html, @"<h[1-6][^>]*>(.*?)</h[1-6]>",
            @"<p><span style=""font-weight:bold"">$1</span></p>", RegexOptions.Singleline);

        // Convert <blockquote> content: unwrap the tag
        html = Regex.Replace(html, @"</?blockquote[^>]*>", "", RegexOptions.IgnoreCase);

        // Convert <br /> or <br> to empty paragraph boundary
        html = Regex.Replace(html, @"<br\s*/?>", "</p><p>", RegexOptions.IgnoreCase);

        // Strip any remaining unsupported tags (keep <p>, </p>, <span...>, </span>)
        html = Regex.Replace(html, @"<(?!/?p[ >/])(?!/?span[ >/])(?!/?br[ >/])[^>]+>", "", RegexOptions.IgnoreCase);

        // Wrap bare text inside <p> tags with <span> — AvRichTextBox only renders
        // <span>, <br>, and <img> inside paragraphs; bare text nodes are dropped.
        html = Regex.Replace(html, @"<p>(.*?)</p>", m =>
        {
            var inner = m.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(inner))
                return "<p></p>";
            // If content already consists entirely of span/br/img tags, leave it
            var stripped = Regex.Replace(inner, @"<span\b[^>]*>.*?</span>|<br\s*/?>|<img\b[^>]*/?>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (string.IsNullOrWhiteSpace(stripped))
                return m.Value;
            // Wrap runs of bare text between tags in <span>
            var wrapped = Regex.Replace(inner, @"(?<=>|^)([^<]+)(?=<|$)", "<span>$1</span>");
            return $"<p>{wrapped}</p>";
        }, RegexOptions.Singleline);

        // Clean up empty paragraphs and whitespace between tags
        html = Regex.Replace(html, @"<p>\s*</p>", "<p></p>");

        return html.Trim();
    }

    internal static string NonEmpty(string value, string fallback)
        => string.IsNullOrEmpty(value) ? fallback : value;

    internal static string? NullIfEmpty(string value)
        => string.IsNullOrEmpty(value) ? null : value;

    internal static ChapterStatus MapChapterStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "first-draft" => ChapterStatus.FirstDraft,
        "revised" => ChapterStatus.Revised,
        "edited" => ChapterStatus.Edited,
        "final" => ChapterStatus.Final,
        _ => ChapterStatus.Outline
    };

    internal static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    internal static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return sanitized.Trim();
    }

    private static void CopyImageFiles(string sourceDir, string destDir)
    {
        if (!Directory.Exists(sourceDir)) return;
        Directory.CreateDirectory(destDir);

        string[] imageExts = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".svg", ".webp"];
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (imageExts.Contains(ext))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                if (!File.Exists(destFile))
                    File.Copy(file, destFile);
            }
        }

        // Recurse into subdirectories
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var subName = Path.GetFileName(subDir);
            CopyImageFiles(subDir, Path.Combine(destDir, subName));
        }
    }

    private static async Task WriteEntityJsonAsync<T>(string path, T entity)
    {
        var json = JsonSerializer.Serialize(entity, JsonWriteOptions);
        await File.WriteAllTextAsync(path, json);
    }
}
