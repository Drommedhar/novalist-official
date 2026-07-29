using System.IO.Compression;
using System.Text;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Paragraph styles and lists reaching every export format.
///
/// The failure this guards against is subtle: the styles existed in the editor
/// and two writers honoured them, so it looked implemented while DOCX and EPUB
/// quietly flattened a heading into body text. Every format is asserted here so
/// a style added later is either handled everywhere or visibly missing in one
/// place.
/// </summary>
public class ExportBlockStyleTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly IProjectService _project = Substitute.For<IProjectService>();

    public void Dispose() => _dir.Dispose();

    private const string Styled =
        "<p class=\"nv-style-heading\">A Heading</p>"
        + "<p class=\"nv-style-subheading\">A Subheading</p>"
        + "<p class=\"nv-style-blockquote\">A quoted line.</p>"
        + "<p class=\"nv-style-poetry\">A verse line.</p>"
        + "<p>Ordinary prose.</p>"
        + "<ul><li>First bullet</li><li>Second bullet</li></ul>"
        + "<ol><li>First number</li><li>Second number</li></ol>";

    private async Task<string> ExportTextAsync(ExportFormat format, string html = Styled)
    {
        var path = Path.Combine(_dir.Path, "out." + format);
        var (service, options) = Setup(html, format);
        await service.ExportAsync(options, path);
        return await File.ReadAllTextAsync(path);
    }

    private async Task<string> ExportZipEntryAsync(
        ExportFormat format, string entry, string html = Styled)
    {
        var path = Path.Combine(_dir.Path, "out." + format);
        var (service, options) = Setup(html, format);
        await service.ExportAsync(options, path);
        using var zip = ZipFile.OpenRead(path);
        using var reader = new StreamReader(zip.GetEntry(entry)!.Open(), Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    /// <summary>The chapter has to be named in the selection or the exporter
    /// compiles nothing and every assertion below fails for the wrong reason.</summary>
    private (ExportService Service, ExportOptions Options) Setup(string html, ExportFormat format)
    {
        var chapter = new ChapterData { Title = "Chapter One", Order = 1 };
        var scene = new SceneData { Title = "Scene", Order = 1, ChapterGuid = chapter.Guid };
        _project.ReadSceneContentAsync(chapter, scene).Returns(html);
        _project.GetChaptersOrdered().Returns([chapter]);
        _project.GetScenesForChapter(chapter.Guid).Returns([scene]);
        return (
            new ExportService(_project),
            new ExportOptions { Format = format, SelectedChapterGuids = [chapter.Guid] });
    }

    // ── The parser ──

    [Fact]
    public void Blocks_CarryTheirStyleAndListKind()
    {
        var blocks = ExportService.ParseHtmlToBlocks(Styled);

        Assert.Equal("heading", blocks[0].StyleId);
        Assert.Equal("subheading", blocks[1].StyleId);
        Assert.Equal("blockquote", blocks[2].StyleId);
        Assert.Equal("poetry", blocks[3].StyleId);
        Assert.Null(blocks[4].StyleId);
        Assert.Equal(ListKind.Bullet, blocks[5].List);
        Assert.Equal(ListKind.Number, blocks[7].List);
    }

    [Fact]
    public void Blocks_KeepDocumentOrderAcrossListsAndParagraphs()
    {
        var blocks = ExportService.ParseHtmlToBlocks(
            "<p>before</p><ul><li>item</li></ul><p>after</p>");

        Assert.Equal(["before", "item", "after"], blocks.Select(b => b.Text));
    }

    [Fact]
    public void Blocks_AListItemOutsideAnyListStillReadsAsABullet()
    {
        // Pasted markup is not always well formed, and a stray item silently
        // becoming body text would lose the writer's structure.
        var blocks = ExportService.ParseHtmlToBlocks("<li>orphan</li>");

        Assert.Equal(ListKind.Bullet, blocks.Single().List);
    }

    [Fact]
    public void Blocks_ContentWithNoBlockMarkupIsOneParagraph()
    {
        var blocks = ExportService.ParseHtmlToBlocks("just some text");

        Assert.Equal("just some text", blocks.Single().Text);
        Assert.Null(blocks.Single().StyleId);
    }

    [Fact]
    public void Blocks_EmptyContentYieldsNothing()
    {
        Assert.Empty(ExportService.ParseHtmlToBlocks(""));
        Assert.Empty(ExportService.ParseHtmlToBlocks("   "));
    }

    [Fact]
    public void Blocks_ABlankParagraphIsDropped()
    {
        Assert.Empty(ExportService.ParseHtmlToBlocks("<p></p><p>   </p>"));
    }

    // ── Markdown ──

    [Fact]
    public async Task Markdown_WritesEveryStyleAndBothListKinds()
    {
        var text = await ExportTextAsync(ExportFormat.Markdown);

        Assert.Contains("# A Heading", text);
        Assert.Contains("## A Subheading", text);
        Assert.Contains("> A quoted line.", text);
        Assert.Contains("    A verse line.", text);
        Assert.Contains("- First bullet", text);
        Assert.Contains("1. First number", text);
        Assert.Contains("2. Second number", text);
    }

    [Fact]
    public async Task Markdown_ASecondListStartsCountingAgain()
    {
        var text = await ExportTextAsync(
            ExportFormat.Markdown, "<ol><li>one</li></ol><p>break</p><ol><li>one again</li></ol>");

        Assert.Contains("1. one\n", text.Replace("\r\n", "\n"));
        Assert.Contains("1. one again", text);
    }

    // ── LaTeX ──

    [Fact]
    public async Task Latex_WritesEveryStyleAndWrapsListsInOneEnvironment()
    {
        var text = await ExportTextAsync(ExportFormat.LaTeX);

        Assert.Contains("\\section*{A Heading}", text);
        Assert.Contains("\\subsection*{A Subheading}", text);
        Assert.Contains("\\begin{quote}A quoted line.\\end{quote}", text);
        Assert.Contains("\\begin{verse}A verse line.\\end{verse}", text);
        // One environment around the run, not one per item.
        Assert.Equal(1, CountOf(text, "\\begin{itemize}"));
        Assert.Equal(1, CountOf(text, "\\end{itemize}"));
        Assert.Equal(1, CountOf(text, "\\begin{enumerate}"));
        Assert.Contains("\\item First bullet", text);
    }

    [Fact]
    public async Task Latex_AListRunningToTheEndOfASceneIsStillClosed()
    {
        // An unclosed environment does not merely look wrong, it fails to
        // compile - the writer would get no PDF at all.
        var text = await ExportTextAsync(ExportFormat.LaTeX, "<ul><li>last thing</li></ul>");

        Assert.Equal(CountOf(text, "\\begin{itemize}"), CountOf(text, "\\end{itemize}"));
    }

    private static int CountOf(string haystack, string needle)
    {
        var count = 0;
        var at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }
        return count;
    }

    // ── EPUB ──

    [Fact]
    public async Task Epub_UsesRealHeadingBlockquoteAndListElements()
    {
        var xhtml = await ExportZipEntryAsync(ExportFormat.Epub, "OEBPS/chapter-1.xhtml");

        Assert.Contains("<h2>A Heading</h2>", xhtml);
        Assert.Contains("<h3>A Subheading</h3>", xhtml);
        Assert.Contains("<blockquote><p>A quoted line.</p></blockquote>", xhtml);
        Assert.Contains("<p class=\"poetry\">A verse line.</p>", xhtml);
        Assert.Contains("<ul>", xhtml);
        Assert.Contains("<li>First bullet</li>", xhtml);
        Assert.Contains("<ol>", xhtml);
    }

    [Fact]
    public async Task Epub_ClosesAListThatEndsAScene()
    {
        var xhtml = await ExportZipEntryAsync(ExportFormat.Epub, "OEBPS/chapter-1.xhtml", "<ul><li>x</li></ul>");

        Assert.Contains("</ul>", xhtml);
    }

    [Fact]
    public async Task Epub_StylesTheNewBlocksInItsStylesheet()
    {
        // A heading element with no rule is a heading the reading system styles
        // however it likes, which is not the same as it looking right.
        var css = await ExportZipEntryAsync(ExportFormat.Epub, "OEBPS/styles.css");

        Assert.Contains("blockquote", css);
        Assert.Contains("p.poetry", css);
        Assert.Contains("ul, ol", css);
    }

    // ── DOCX ──

    [Fact]
    public async Task Docx_MapsEveryStyleToAWordStyle()
    {
        var document = await ExportZipEntryAsync(ExportFormat.Docx, "word/document.xml");

        Assert.Contains("<w:pStyle w:val=\"Heading2\"/>", document);
        Assert.Contains("<w:pStyle w:val=\"Heading3\"/>", document);
        Assert.Contains("<w:pStyle w:val=\"Quote\"/>", document);
        Assert.Contains("<w:pStyle w:val=\"Verse\"/>", document);
    }

    [Fact]
    public async Task Docx_DefinesEveryStyleItReferences()
    {
        // A pStyle pointing at a style that is not defined silently renders as
        // Normal, which is exactly the flattening this feature is fixing.
        var styles = await ExportZipEntryAsync(ExportFormat.Docx, "word/styles.xml");

        foreach (var id in new[] { "Heading2", "Heading3", "Quote", "Verse", "ListParagraph" })
            Assert.Contains($"w:styleId=\"{id}\"", styles);
    }

    [Fact]
    public async Task Docx_ListsUseRealNumberingRatherThanTypedBullets()
    {
        var document = await ExportZipEntryAsync(ExportFormat.Docx, "word/document.xml");

        Assert.Contains("<w:numId w:val=\"1\"/>", document);
        Assert.Contains("<w:numId w:val=\"2\"/>", document);
        // Not a literal bullet character in the text.
        Assert.DoesNotContain("<w:t>•", document);
    }

    [Fact]
    public async Task Docx_ShipsTheNumberingPartItPointsAt()
    {
        // Word treats a numPr referencing a missing numbering part as a corrupt
        // file and refuses to open it.
        var numbering = await ExportZipEntryAsync(ExportFormat.Docx, "word/numbering.xml");

        Assert.Contains("w:numId=\"1\"", numbering);
        Assert.Contains("w:numId=\"2\"", numbering);
        Assert.Contains("w:numFmt w:val=\"bullet\"", numbering);
        Assert.Contains("w:numFmt w:val=\"decimal\"", numbering);
    }

    [Fact]
    public async Task Docx_DeclaresTheNumberingPartInThePackage()
    {
        var contentTypes = await ExportZipEntryAsync(ExportFormat.Docx, "[Content_Types].xml");
        var rels = await ExportZipEntryAsync(ExportFormat.Docx, "word/_rels/document.xml.rels");

        Assert.Contains("/word/numbering.xml", contentTypes);
        Assert.Contains("Target=\"numbering.xml\"", rels);
    }

    [Fact]
    public async Task Docx_OrdinaryProseIsStillBodyText()
    {
        var document = await ExportZipEntryAsync(ExportFormat.Docx, "word/document.xml", "<p>a</p><p>b</p>");

        Assert.Contains("<w:pStyle w:val=\"NoIndent\"/>", document);
        Assert.Contains("<w:pStyle w:val=\"BodyText\"/>", document);
    }
}
