using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Novalist.Core.Services;

/// <summary>
/// One paragraph read out of an imported file, with whatever structural hint the
/// source gave. <see cref="HeadingLevel"/> is 0 for body text, 1 for a top-level
/// heading, 2 for the next level down, and so on.
/// </summary>
public sealed class ImportedParagraph
{
    public string Text { get; init; } = string.Empty;
    public int HeadingLevel { get; init; }

    /// <summary>True when the paragraph is a scene-break ornament rather than
    /// prose - a line of asterisks, a horizontal rule, a lone hash.</summary>
    public bool IsSceneBreak { get; init; }
}

/// <summary>Everything read out of one manuscript file.</summary>
public sealed class ManuscriptDocument
{
    public IReadOnlyList<ImportedParagraph> Paragraphs { get; init; } = [];

    /// <summary>Format the reader recognised: "docx", "odt", "epub", "markdown",
    /// "text", "rtf", or "" when nothing could be read.</summary>
    public string Format { get; init; } = string.Empty;

    public bool IsEmpty => Paragraphs.Count == 0;
}

/// <summary>
/// Reads a manuscript out of the file formats writers actually arrive with.
///
/// The job here is only to recover paragraphs and their heading level -
/// splitting those into chapters and scenes is <see cref="ManuscriptSplitter"/>'s
/// problem. Keeping the two apart means a new format is a reader, not a rewrite
/// of the structure heuristics.
///
/// Every reader is tolerant: an unreadable or unrecognised file yields an empty
/// document rather than throwing, because the writer did not author it and
/// "nothing could be read" is the useful answer.
/// </summary>
public static partial class ManuscriptReader
{
    private static readonly XNamespace W =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Text =
        "urn:oasis:names:tc:opendocument:xmlns:text:1.0";

    [GeneratedRegex(@"^\s*(\*\s*){3,}\s*$|^\s*#\s*$|^\s*-{3,}\s*$|^\s*_{3,}\s*$", RegexOptions.Compiled)]
    private static partial Regex SceneBreakRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex TagRegex();

    /// <summary>Formats the importer can read, by extension.</summary>
    public static IReadOnlyList<string> SupportedExtensions { get; } =
        [".docx", ".odt", ".epub", ".md", ".markdown", ".txt", ".rtf"];

    /// <summary>
    /// Reads a manuscript. The format is chosen by extension; an extension the
    /// importer does not know yields an empty document.
    /// </summary>
    public static ManuscriptDocument Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new ManuscriptDocument();

        try
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".docx" => ReadDocx(path),
                ".odt" => ReadOdt(path),
                ".epub" => ReadEpub(path),
                ".md" or ".markdown" => ReadMarkdown(File.ReadAllText(path)),
                ".txt" => ReadPlainText(File.ReadAllText(path)),
                ".rtf" => ReadRtf(File.ReadAllText(path)),
                _ => new ManuscriptDocument()
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException
            or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return new ManuscriptDocument();
        }
    }

    // ── Word ──

    private static ManuscriptDocument ReadDocx(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("word/document.xml");
        if (entry == null)
            return new ManuscriptDocument();

        using var stream = entry.Open();
        var document = XDocument.Load(stream);

        var paragraphs = new List<ImportedParagraph>();
        foreach (var p in document.Descendants(W + "p"))
        {
            // Deleted runs are text the author already removed; importing them
            // would resurrect cut prose.
            var text = string.Concat(
                p.Descendants(W + "t")
                    .Where(t => !t.Ancestors(W + "del").Any())
                    .Select(t => t.Value));

            paragraphs.Add(Build(text, HeadingLevelFromDocxStyle(p)));
        }

        return Done(paragraphs, "docx");
    }

    /// <summary>
    /// Word's built-in heading styles are named "Heading1".."Heading9" in the
    /// style id regardless of the interface language, so this works on a German
    /// or Chinese Word document too.
    /// </summary>
    private static int HeadingLevelFromDocxStyle(XElement paragraph)
    {
        var style = paragraph
            .Element(W + "pPr")?
            .Element(W + "pStyle")?
            .Attribute(W + "val")?.Value;

        if (string.IsNullOrEmpty(style))
            return 0;

        var match = Regex.Match(style, @"^Heading(\d)$", RegexOptions.IgnoreCase);
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    // ── OpenDocument ──

    private static ManuscriptDocument ReadOdt(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("content.xml");
        if (entry == null)
            return new ManuscriptDocument();

        using var stream = entry.Open();
        var document = XDocument.Load(stream);

        var paragraphs = new List<ImportedParagraph>();
        foreach (var node in document.Descendants()
            .Where(e => e.Name == Text + "p" || e.Name == Text + "h"))
        {
            var level = node.Name == Text + "h"
                && int.TryParse((string?)node.Attribute(Text + "outline-level"), out var parsed)
                ? parsed
                : 0;
            paragraphs.Add(Build(node.Value, level));
        }

        return Done(paragraphs, "odt");
    }

    // ── EPUB ──

    private static ManuscriptDocument ReadEpub(string path)
    {
        using var zip = ZipFile.OpenRead(path);

        // Spine order matters: reading entries alphabetically would shuffle
        // chapter 10 in front of chapter 2.
        var documents = SpineOrder(zip);
        var paragraphs = new List<ImportedParagraph>();

        foreach (var name in documents)
        {
            var entry = zip.GetEntry(name);
            if (entry == null)
                continue;

            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            paragraphs.AddRange(ReadXhtmlBlocks(reader.ReadToEnd()));
        }

        return Done(paragraphs, "epub");
    }

    /// <summary>
    /// Content documents in spine order. Falls back to every .xhtml/.html entry
    /// sorted by name when the package cannot be read - out of order beats
    /// importing nothing.
    /// </summary>
    private static List<string> SpineOrder(ZipArchive zip)
    {
        try
        {
            var containerEntry = zip.GetEntry("META-INF/container.xml");
            if (containerEntry != null)
            {
                using var containerStream = containerEntry.Open();
                var container = XDocument.Load(containerStream);
                var opfPath = container.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "rootfile")?
                    .Attribute("full-path")?.Value;

                var opfEntry = opfPath == null ? null : zip.GetEntry(opfPath);
                if (opfEntry != null)
                {
                    using var opfStream = opfEntry.Open();
                    var opf = XDocument.Load(opfStream);
                    var baseDir = Path.GetDirectoryName(opfPath!)?.Replace('\\', '/') ?? string.Empty;

                    var manifest = opf.Descendants()
                        .Where(e => e.Name.LocalName == "item")
                        .ToDictionary(
                            e => (string?)e.Attribute("id") ?? string.Empty,
                            e => (string?)e.Attribute("href") ?? string.Empty);

                    var ordered = new List<string>();
                    foreach (var itemref in opf.Descendants().Where(e => e.Name.LocalName == "itemref"))
                    {
                        var id = (string?)itemref.Attribute("idref");
                        if (id != null && manifest.TryGetValue(id, out var href) && href.Length > 0)
                            ordered.Add(baseDir.Length > 0 ? $"{baseDir}/{href}" : href);
                    }

                    if (ordered.Count > 0)
                        return ordered;
                }
            }
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidDataException)
        {
            // Fall through to the name-sorted fallback.
        }

        return zip.Entries
            .Select(e => e.FullName)
            .Where(n => n.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase)
                || n.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Pulls block-level text out of XHTML without a DOM: headings keep their
    /// level, everything else is body text.
    /// </summary>
    private static IEnumerable<ImportedParagraph> ReadXhtmlBlocks(string html)
    {
        var blocks = Regex.Matches(
            html,
            @"<(h[1-6]|p|div)\b[^>]*>(?<body>.*?)</\1>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        foreach (Match block in blocks)
        {
            var tag = block.Groups[1].Value.ToLowerInvariant();
            var level = tag.Length == 2 && tag[0] == 'h' ? tag[1] - '0' : 0;
            var text = System.Net.WebUtility.HtmlDecode(TagRegex().Replace(block.Groups["body"].Value, string.Empty));
            yield return Build(text, level);
        }
    }

    // ── Markdown ──

    internal static ManuscriptDocument ReadMarkdown(string content)
    {
        var paragraphs = new List<ImportedParagraph>();
        var buffer = new StringBuilder();

        void FlushBuffer()
        {
            if (buffer.Length == 0)
                return;
            paragraphs.Add(Build(buffer.ToString(), 0));
            buffer.Clear();
        }

        foreach (var rawLine in SplitLines(content))
        {
            var line = rawLine.TrimEnd();

            if (line.Trim().Length == 0)
            {
                FlushBuffer();
                continue;
            }

            var heading = Regex.Match(line, @"^(#{1,6})\s+(.*)$");
            if (heading.Success)
            {
                FlushBuffer();
                paragraphs.Add(Build(heading.Groups[2].Value, heading.Groups[1].Value.Length));
                continue;
            }

            if (SceneBreakRegex().IsMatch(line))
            {
                FlushBuffer();
                paragraphs.Add(new ImportedParagraph { IsSceneBreak = true });
                continue;
            }

            // A wrapped paragraph continues until a blank line.
            if (buffer.Length > 0)
                buffer.Append(' ');
            buffer.Append(line.Trim());
        }

        FlushBuffer();
        return Done(paragraphs, "markdown");
    }

    // ── Plain text ──

    internal static ManuscriptDocument ReadPlainText(string content)
    {
        var paragraphs = new List<ImportedParagraph>();
        foreach (var rawLine in SplitLines(content))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            paragraphs.Add(SceneBreakRegex().IsMatch(line)
                ? new ImportedParagraph { IsSceneBreak = true }
                : Build(line, 0));
        }

        return Done(paragraphs, "text");
    }

    // ── RTF ──

    /// <summary>
    /// Paragraph-level RTF extraction: control words are dropped and <c>\par</c>
    /// ends a paragraph. Formatting is deliberately not recovered - an RTF
    /// manuscript imports as clean prose, which is what the structure heuristics
    /// need and what a writer would have to re-style anyway.
    /// </summary>
    internal static ManuscriptDocument ReadRtf(string content)
    {
        var text = new StringBuilder();
        var paragraphs = new List<ImportedParagraph>();

        void EndParagraph()
        {
            var line = text.ToString().Trim();
            text.Clear();
            if (line.Length == 0)
                return;

            paragraphs.Add(SceneBreakRegex().IsMatch(line)
                ? new ImportedParagraph { IsSceneBreak = true }
                : Build(line, 0));
        }

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];

            if (c == '\\')
            {
                var word = new StringBuilder();
                var j = i + 1;
                while (j < content.Length && char.IsLetter(content[j]))
                    word.Append(content[j++]);

                // A numeric parameter belongs to the control word.
                while (j < content.Length && (char.IsDigit(content[j]) || content[j] == '-'))
                    j++;
                if (j < content.Length && content[j] == ' ')
                    j++;

                var control = word.ToString();
                if (control is "par" or "pard" or "line")
                    EndParagraph();
                else if (control == "tab")
                    text.Append(' ');

                i = j - 1;
                continue;
            }

            if (c is '{' or '}' or '\r' or '\n')
                continue;

            text.Append(c);
        }

        EndParagraph();
        return Done(paragraphs, "rtf");
    }

    // ── Shared ──

    private static IEnumerable<string> SplitLines(string content) =>
        content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static ImportedParagraph Build(string text, int headingLevel)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return SceneBreakRegex().IsMatch(trimmed)
            ? new ImportedParagraph { IsSceneBreak = true }
            : new ImportedParagraph { Text = trimmed, HeadingLevel = headingLevel };
    }

    /// <summary>Drops the empty paragraphs every format produces and stamps the
    /// format that was read.</summary>
    private static ManuscriptDocument Done(List<ImportedParagraph> paragraphs, string format) =>
        new()
        {
            Paragraphs = paragraphs
                .Where(p => p.IsSceneBreak || p.Text.Length > 0)
                .ToList(),
            Format = format
        };
}
