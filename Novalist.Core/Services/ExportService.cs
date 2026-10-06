using System.Text;
using Novalist.Core.Models;
using Novalist.Core.Utilities;
using static Novalist.Core.Services.ExportProse;

namespace Novalist.Core.Services;

/// <summary>
/// Compiles selected project content and dispatches it to the format writers.
/// </summary>
public partial class ExportService
{
    private const string SceneBreakText = "* * *";

    private readonly IProjectService _projectService;
    private readonly IEntityService? _entityService;

    public ExportService(IProjectService projectService, IEntityService? entityService = null)
    {
        _projectService = projectService;
        _entityService = entityService;
    }

    /// <summary>
    /// Which book a compile pass is reading, and how to reach its scenes.
    ///
    /// Everything below used to go straight to the active book. A box set has
    /// to read a volume nobody has open, and switching the active book mid-run
    /// would mutate state the watcher and the UI are both reading.
    /// </summary>
    /// <param name="SelectAll">
    /// True for a book the writer did not tick chapters for. The chapter list
    /// in the Export view belongs to the open book, so filtering a further
    /// volume against it would drop every chapter it has.
    /// </param>
    private sealed record VolumeSource(
        BookData? Book,
        List<ChapterData> Chapters,
        Func<string, List<SceneData>> ScenesFor,
        Func<ChapterData, SceneData, Task<string>> ReadScene,
        bool SelectAll = false);

    /// <summary>
    /// The by-line under a volume's heading, or null when the volume is by
    /// whoever wrote the rest of the project - repeating the project's author
    /// over every volume of a series says nothing and reads as a mistake.
    /// </summary>
    private static string? AuthorLine(BookData book, ExportOptions options)
        => !string.IsNullOrWhiteSpace(book.Author)
            && !string.Equals(book.Author.Trim(), (options.Author ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase)
            ? book.Author.Trim()
            : null;

    /// <summary>The open book, which is what every export read before volumes existed.</summary>
    private VolumeSource ActiveSource() => new(
        _projectService.ActiveBook,
        _projectService.GetChaptersOrdered(),
        _projectService.GetScenesForChapter,
        _projectService.ReadSceneContentAsync);

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
            case ExportFormat.FinalDraft:
                await ExportToFinalDraftAsync(chapters, options, outputPath);
                break;
            case ExportFormat.LaTeX:
                await ExportToLatexAsync(chapters, options, outputPath);
                break;
            case ExportFormat.Codex:
                await ExportCodexAsync(options, outputPath);
                break;
            case ExportFormat.CodexPdf:
                await ExportCodexPdfAsync(options, outputPath);
                break;
        }
    }

    /// <summary>
    /// The line printed at the top of every page.
    ///
    /// A layout can author it with placeholders. Empty falls back to the
    /// submission convention - surname and short title - which is what every
    /// manuscript export printed when this could not be authored at all.
    /// </summary>
    internal static string RunningHead(ExportOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ResolvedRunningHead))
            return options.ResolvedRunningHead.Trim();

        var surname = !string.IsNullOrWhiteSpace(options.Author)
            ? options.Author.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last()
            : string.Empty;
        // A long title runs into the page number, so it is cut rather than
        // allowed to collide with it.
        var shortTitle = options.Title.Length > 30 ? options.Title[..27] + "..." : options.Title;
        return $"{surname} / {shortTitle.ToUpperInvariant()}";
    }

    /// <summary>
    /// A scene's content as ordered blocks, each carrying its paragraph style
    /// and whether it is a list item.
    ///
    /// Every writer parses from here rather than from raw HTML, so a style added
    /// in the editor reaches DOCX, EPUB, Markdown and LaTeX the same way instead
    /// of being honoured by whichever exporter happened to grow a case for it.
    /// </summary>
    internal static List<ExportBlock> ParseHtmlToBlocks(
        string html, IReadOnlyDictionary<string, string>? footnotes = null)
        => ExportProse.ParseHtmlToBlocks(html, footnotes);

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

    // ─── Normseiten (German standard pages) ──────────────────────────

    /// <summary>
    /// Turns editor HTML into Normseite blocks. Paragraphs carrying the
    /// editor's heading / subheading style become headings; everything else is
    /// body text, with a blank line between paragraphs.
    /// </summary>
    public static List<NormseitenBlock> HtmlToNormseitenBlocks(string html)
        => ExportProse.HtmlToNormseitenBlocks(html);

    /// <summary>Blocks for a whole-manuscript Normseiten export.</summary>
    /// <summary>
    /// Splits an opening line into its initial letter, the words that follow
    /// in small capitals, and the rest. Returns null when there is nothing to
    /// set - an opener that begins with punctuation or a number is left alone
    /// rather than dropped into a quotation mark.
    /// </summary>
    internal static (string Initial, string LeadIn, string Tail)? SplitOpener(
        string text, int leadInWords)
    {
        if (string.IsNullOrWhiteSpace(text) || !char.IsLetter(text[0])) return null;

        var initial = text[..1];
        var after = text[1..];
        if (leadInWords <= 0) return (initial, string.Empty, after);

        // The lead-in runs to the end of the nth word, counting the initial's
        // own word as the first.
        var index = 0;
        var words = 0;
        while (index < after.Length && words < leadInWords)
        {
            while (index < after.Length && !char.IsWhiteSpace(after[index])) index++;
            words++;
            if (words < leadInWords)
            {
                while (index < after.Length && char.IsWhiteSpace(after[index])) index++;
            }
        }
        return (initial, after[..index], after[index..]);
    }

    /// <summary>
    /// Rewrites the book-relative image paths a scene stores into absolute
    /// ones. The writers each have to open the file; resolving once here means
    /// none of them needs to know where a book keeps its images.
    /// </summary>
    private string ResolveImagePaths(string html)
    {
        if (string.IsNullOrEmpty(html) || !html.Contains("<img", StringComparison.OrdinalIgnoreCase))
            return html;

        var bookRoot = _projectService.ActiveBookRoot;
        if (bookRoot == null) return html;

        return ImageTagRegex().Replace(html, match =>
        {
            var src = HtmlAttribute(match.Groups["attrs"].Value, "src");
            if (src.Length == 0 || src.Contains("://", StringComparison.Ordinal) || Path.IsPathRooted(src))
                return match.Value;
            var absolute = Path.GetFullPath(Path.Combine(bookRoot, src));
            return match.Value.Replace(src, absolute.Replace(Path.DirectorySeparatorChar, '/'));
        });
    }
}
