using System.IO.Compression;
using System.Text;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Reading a manuscript out of the formats writers arrive with, and splitting it
/// into chapters and scenes. Nothing here may throw on a file the writer did not
/// author - an unreadable file must read as "nothing found".
/// </summary>
public class ManuscriptImportTests
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string TextNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static string BuildDocx(TempDir dir, string body)
    {
        var path = dir.Combine("book.docx");
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        WriteEntry(zip, "word/document.xml",
            $"""<?xml version="1.0"?><w:document xmlns:w="{W}"><w:body>{body}</w:body></w:document>""");
        return path;
    }

    private static string DocxParagraph(string text, int heading = 0)
    {
        var style = heading > 0
            ? $"<w:pPr><w:pStyle w:val=\"Heading{heading}\"/></w:pPr>"
            : string.Empty;
        return $"<w:p>{style}<w:r><w:t>{text}</w:t></w:r></w:p>";
    }

    // ── Tolerance ──

    [Fact]
    public void Read_MissingFile_IsEmpty()
    {
        using var dir = new TempDir();
        Assert.True(ManuscriptReader.Read(dir.Combine("nope.docx")).IsEmpty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Read_BlankPath_IsEmpty(string path) =>
        Assert.True(ManuscriptReader.Read(path).IsEmpty);

    [Fact]
    public void Read_UnsupportedExtension_IsEmpty()
    {
        using var dir = new TempDir();
        var path = dir.Combine("book.pages");
        File.WriteAllText(path, "content");

        Assert.True(ManuscriptReader.Read(path).IsEmpty);
    }

    [Fact]
    public void Read_CorruptDocx_IsEmpty()
    {
        using var dir = new TempDir();
        var path = dir.Combine("book.docx");
        File.WriteAllText(path, "not a zip");

        Assert.True(ManuscriptReader.Read(path).IsEmpty);
    }

    [Fact]
    public void Read_DocxWithoutDocumentPart_IsEmpty()
    {
        using var dir = new TempDir();
        var path = dir.Combine("book.docx");
        using (var stream = File.Create(path))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            WriteEntry(zip, "word/styles.xml", "<styles/>");

        Assert.True(ManuscriptReader.Read(path).IsEmpty);
    }

    [Fact]
    public void SupportedExtensions_CoverTheCommonManuscriptFormats()
    {
        Assert.Contains(".docx", ManuscriptReader.SupportedExtensions);
        Assert.Contains(".odt", ManuscriptReader.SupportedExtensions);
        Assert.Contains(".epub", ManuscriptReader.SupportedExtensions);
        Assert.Contains(".md", ManuscriptReader.SupportedExtensions);
        Assert.Contains(".txt", ManuscriptReader.SupportedExtensions);
        Assert.Contains(".rtf", ManuscriptReader.SupportedExtensions);
    }

    // ── Word ──

    [Fact]
    public void Docx_HeadingsBecomeChaptersAndBodyBecomesScenes()
    {
        using var dir = new TempDir();
        var path = BuildDocx(dir,
            DocxParagraph("The Arrival", 1)
            + DocxParagraph("She stepped off the train.")
            + DocxParagraph("The Departure", 1)
            + DocxParagraph("She did not look back."));

        var plan = ManuscriptSplitter.Split(ManuscriptReader.Read(path));

        Assert.Equal("docx", plan.Format);
        Assert.Equal(2, plan.Chapters.Count);
        Assert.Equal("The Arrival", plan.Chapters[0].Title);
        Assert.Contains("She stepped off the train.", plan.Chapters[0].Scenes[0].Html);
    }

    [Fact]
    public void Docx_SecondLevelHeadingsBecomeScenes()
    {
        using var dir = new TempDir();
        var path = BuildDocx(dir,
            DocxParagraph("One", 1)
            + DocxParagraph("At the docks", 2)
            + DocxParagraph("Rain.")
            + DocxParagraph("At the inn", 2)
            + DocxParagraph("Warmth."));

        var plan = ManuscriptSplitter.Split(ManuscriptReader.Read(path));

        var chapter = Assert.Single(plan.Chapters);
        Assert.Equal(2, chapter.Scenes.Count);
        Assert.Equal("At the docks", chapter.Scenes[0].Title);
        Assert.Equal("At the inn", chapter.Scenes[1].Title);
    }

    [Fact]
    public void Docx_DeletedRunsAreNotImported()
    {
        using var dir = new TempDir();
        var path = BuildDocx(dir,
            "<w:p><w:r><w:t>Kept. </w:t></w:r>"
            + "<w:del><w:r><w:t>Cut long ago.</w:t></w:r></w:del></w:p>");

        var plan = ManuscriptSplitter.Split(ManuscriptReader.Read(path));

        // Importing tracked deletions would resurrect prose the author removed.
        Assert.DoesNotContain("Cut long ago", plan.Chapters[0].Scenes[0].Html);
        Assert.Contains("Kept.", plan.Chapters[0].Scenes[0].Html);
    }

    [Fact]
    public void Docx_HeadingStyleIsRecognisedRegardlessOfInterfaceLanguage()
    {
        // Word stores the style id as "Heading1" even in a German or Chinese UI.
        using var dir = new TempDir();
        var path = BuildDocx(dir, DocxParagraph("Kapitel Eins", 1) + DocxParagraph("Es regnete."));

        var plan = ManuscriptSplitter.Split(ManuscriptReader.Read(path));

        Assert.Equal("Kapitel Eins", Assert.Single(plan.Chapters).Title);
    }

    // ── OpenDocument ──

    [Fact]
    public void Odt_OutlineLevelsBecomeChapters()
    {
        using var dir = new TempDir();
        var path = dir.Combine("book.odt");
        using (var stream = File.Create(path))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "content.xml",
                $"""
                <?xml version="1.0"?>
                <office xmlns:text="{TextNs}">
                  <text:h text:outline-level="1">First</text:h>
                  <text:p>Body text.</text:p>
                  <text:h text:outline-level="1">Second</text:h>
                  <text:p>More body.</text:p>
                </office>
                """);
        }

        var plan = ManuscriptSplitter.Split(ManuscriptReader.Read(path));

        Assert.Equal("odt", plan.Format);
        Assert.Equal(2, plan.Chapters.Count);
    }

    [Fact]
    public void Odt_WithoutContentPart_IsEmpty()
    {
        using var dir = new TempDir();
        var path = dir.Combine("book.odt");
        using (var stream = File.Create(path))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            WriteEntry(zip, "styles.xml", "<styles/>");

        Assert.True(ManuscriptReader.Read(path).IsEmpty);
    }

    // ── EPUB ──

    [Fact]
    public void Epub_ReadsContentDocumentsInSpineOrder()
    {
        using var dir = new TempDir();
        var path = dir.Combine("book.epub");
        using (var stream = File.Create(path))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "META-INF/container.xml",
                """<?xml version="1.0"?><container><rootfiles><rootfile full-path="OEBPS/content.opf"/></rootfiles></container>""");
            // Deliberately declared out of alphabetical order: spine order wins.
            WriteEntry(zip, "OEBPS/content.opf",
                """
                <?xml version="1.0"?>
                <package>
                  <manifest>
                    <item id="b" href="second.xhtml"/>
                    <item id="a" href="first.xhtml"/>
                  </manifest>
                  <spine><itemref idref="b"/><itemref idref="a"/></spine>
                </package>
                """);
            WriteEntry(zip, "OEBPS/first.xhtml", "<html><body><h1>Alpha</h1><p>One.</p></body></html>");
            WriteEntry(zip, "OEBPS/second.xhtml", "<html><body><h1>Beta</h1><p>Two.</p></body></html>");
        }

        var plan = ManuscriptSplitter.Split(ManuscriptReader.Read(path));

        Assert.Equal("epub", plan.Format);
        Assert.Equal("Beta", plan.Chapters[0].Title);
        Assert.Equal("Alpha", plan.Chapters[1].Title);
    }

    [Fact]
    public void Epub_WithoutAReadablePackage_FallsBackToNameOrder()
    {
        using var dir = new TempDir();
        var path = dir.Combine("book.epub");
        using (var stream = File.Create(path))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "a.xhtml", "<html><body><h1>Alpha</h1><p>One.</p></body></html>");
            WriteEntry(zip, "b.xhtml", "<html><body><h1>Beta</h1><p>Two.</p></body></html>");
        }

        var plan = ManuscriptSplitter.Split(ManuscriptReader.Read(path));

        // Out of order beats importing nothing.
        Assert.Equal(2, plan.Chapters.Count);
        Assert.Equal("Alpha", plan.Chapters[0].Title);
    }

    [Fact]
    public void Epub_EntitiesAreDecoded()
    {
        using var dir = new TempDir();
        var path = dir.Combine("book.epub");
        using (var stream = File.Create(path))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            WriteEntry(zip, "a.xhtml", "<html><body><p>Tom &amp; Jerry said &quot;go&quot;.</p></body></html>");

        var plan = ManuscriptSplitter.Split(ManuscriptReader.Read(path));

        Assert.Contains("Tom &amp; Jerry", plan.Chapters[0].Scenes[0].Html);
    }

    // ── Markdown ──

    [Fact]
    public void Markdown_HashHeadingsSplitChaptersAndScenes()
    {
        var plan = ManuscriptSplitter.Split(ManuscriptReader.ReadMarkdown(
            "# One\n\n## Docks\n\nRain fell.\n\n## Inn\n\nWarmth.\n\n# Two\n\nEnd."));

        Assert.Equal("markdown", plan.Format);
        Assert.Equal(2, plan.Chapters.Count);
        Assert.Equal(2, plan.Chapters[0].Scenes.Count);
        Assert.Equal("Docks", plan.Chapters[0].Scenes[0].Title);
    }

    [Fact]
    public void Markdown_WrappedLinesRejoinIntoOneParagraph()
    {
        var plan = ManuscriptSplitter.Split(ManuscriptReader.ReadMarkdown(
            "# One\n\nShe walked\nacross the room\nand stopped.\n\nThen she left."));

        var html = plan.Chapters[0].Scenes[0].Html;
        Assert.Contains("<p>She walked across the room and stopped.</p>", html);
        Assert.Contains("<p>Then she left.</p>", html);
    }

    [Theory]
    [InlineData("***")]
    [InlineData("* * *")]
    [InlineData("---")]
    [InlineData("___")]
    [InlineData("#")]
    public void Markdown_OrnamentLinesSplitScenes(string ornament)
    {
        var plan = ManuscriptSplitter.Split(ManuscriptReader.ReadMarkdown(
            $"# One\n\nBefore.\n\n{ornament}\n\nAfter."));

        Assert.Equal(2, Assert.Single(plan.Chapters).Scenes.Count);
    }

    // ── Plain text ──

    [Fact]
    public void PlainText_ChapterHeadingsAreDetected()
    {
        var plan = ManuscriptSplitter.Split(ManuscriptReader.ReadPlainText(
            "Chapter 1\n\nShe stepped off the train.\n\nChapter 2\n\nShe did not look back."));

        Assert.Equal("text", plan.Format);
        Assert.Equal(2, plan.Chapters.Count);
    }

    [Fact]
    public void PlainText_BareNumeralsCountAsChapterHeadings()
    {
        var plan = ManuscriptSplitter.Split(ManuscriptReader.ReadPlainText(
            "1\n\nFirst chapter body.\n\n2\n\nSecond chapter body."));

        Assert.Equal(2, plan.Chapters.Count);
    }

    [Fact]
    public void PlainText_ProseIsNeverMistakenForAHeading()
    {
        var plan = ManuscriptSplitter.Split(ManuscriptReader.ReadPlainText(
            "Chapter one of her life was over.\n\nShe knew it."));

        // Ends in sentence punctuation, so it is prose, not a heading.
        var chapter = Assert.Single(plan.Chapters);
        Assert.Contains("Chapter one of her life", chapter.Scenes[0].Html);
    }

    [Fact]
    public void PlainText_WithoutAnyHeadings_BecomesOneChapter()
    {
        var plan = ManuscriptSplitter.Split(ManuscriptReader.ReadPlainText(
            "Just some prose.\n\nAnd more of it."));

        var chapter = Assert.Single(plan.Chapters);
        Assert.Equal("Chapter 1", chapter.Title);
    }

    // ── RTF ──

    [Fact]
    public void Rtf_ParagraphsAreRecovered()
    {
        var plan = ManuscriptSplitter.Split(ManuscriptReader.ReadRtf(
            @"{\rtf1\ansi\deff0 Chapter 1\par She stepped off the train.\par She did not look back.\par}"));

        Assert.Equal("rtf", plan.Format);
        var chapter = Assert.Single(plan.Chapters);
        Assert.Contains("She stepped off the train.", chapter.Scenes[0].Html);
        // Control words must not survive into the prose.
        Assert.DoesNotContain("rtf1", chapter.Scenes[0].Html);
        Assert.DoesNotContain("ansi", chapter.Scenes[0].Html);
    }

    [Fact]
    public void Rtf_TabsBecomeSpaces()
    {
        var plan = ManuscriptSplitter.Split(ManuscriptReader.ReadRtf(
            @"{\rtf1 \tab Indented line.\par}"));

        Assert.Contains("Indented line.", plan.Chapters[0].Scenes[0].Html);
    }

    // ── Splitting rules ──

    [Fact]
    public void Split_EmptyDocument_ProducesAnEmptyPlan()
    {
        var plan = ManuscriptSplitter.Split(new ManuscriptDocument());

        Assert.True(plan.IsEmpty);
        Assert.Equal(0, plan.SceneCount);
    }

    [Fact]
    public void Split_HeadingsWin_BodyTextIsNeverSecondGuessed()
    {
        // The file uses headings, so a body line reading "Chapter 2" is prose
        // the author wrote, not a structural signal.
        using var dir = new TempDir();
        var path = BuildDocx(dir,
            DocxParagraph("Real Chapter", 1)
            + DocxParagraph("Chapter 2")
            + DocxParagraph("was the hardest to write."));

        var plan = ManuscriptSplitter.Split(ManuscriptReader.Read(path));

        Assert.Single(plan.Chapters);
        Assert.Contains("Chapter 2", plan.Chapters[0].Scenes[0].Html);
    }

    [Fact]
    public void Split_VeryLongRun_IsBrokenIntoSeveralScenes()
    {
        var words = string.Join(" ", Enumerable.Repeat("word", 500));
        var paragraphs = string.Join("\n\n", Enumerable.Repeat(words, 20));

        var plan = ManuscriptSplitter.Split(ManuscriptReader.ReadPlainText(paragraphs));

        // 10,000 words must not become one unopenable scene.
        Assert.True(Assert.Single(plan.Chapters).Scenes.Count > 1);
    }

    [Fact]
    public void Split_CountsWordsPerScene()
    {
        var plan = ManuscriptSplitter.Split(ManuscriptReader.ReadPlainText(
            "Chapter 1\n\nOne two three four five."));

        Assert.Equal(5, plan.WordCount);
        Assert.Equal(5, plan.Chapters[0].Scenes[0].WordCount);
    }

    [Fact]
    public void Split_UntitledChaptersAndScenesGetNumbers()
    {
        var plan = ManuscriptSplitter.Split(ManuscriptReader.ReadPlainText("Body with no heading."));

        Assert.Equal("Chapter 1", plan.Chapters[0].Title);
        Assert.Equal("Scene 1", plan.Chapters[0].Scenes[0].Title);
    }

    [Fact]
    public void Split_HtmlInProseIsEscaped()
    {
        var plan = ManuscriptSplitter.Split(ManuscriptReader.ReadPlainText(
            "She wrote <b>bold</b> & moved on."));

        var html = plan.Chapters[0].Scenes[0].Html;
        Assert.Contains("&lt;b&gt;", html);
        Assert.Contains("&amp;", html);
    }

    [Theory]
    [InlineData("Chapter 1", true)]
    [InlineData("CHAPTER XII", true)]
    [InlineData("Kapitel 3", true)]
    [InlineData("第 3 章", true)]
    [InlineData("7", true)]
    [InlineData("xiv", true)]
    [InlineData("She stepped off the train.", false)]
    [InlineData("", false)]
    [InlineData("Chapter one of her life was over.", false)]
    public void LooksLikeChapterHeading_IsNarrow(string line, bool expected) =>
        Assert.Equal(expected, ManuscriptSplitter.LooksLikeChapterHeading(line));

    [Fact]
    public void LooksLikeChapterHeading_RejectsVeryLongLines()
    {
        var long_ = "Chapter " + new string('x', 120);
        Assert.False(ManuscriptSplitter.LooksLikeChapterHeading(long_));
    }

    // -- Reading from disk (the path every real import takes) --

    [Theory]
    [InlineData(".md", "markdown")]
    [InlineData(".markdown", "markdown")]
    public void Read_MarkdownFromDisk(string extension, string expectedFormat)
    {
        using var dir = new TempDir();
        var path = dir.Combine("book" + extension);
        File.WriteAllText(path, "# One\n\nProse here.");

        var document = ManuscriptReader.Read(path);

        Assert.Equal(expectedFormat, document.Format);
        Assert.NotEmpty(document.Paragraphs);
    }

    [Fact]
    public void Read_PlainTextFromDisk()
    {
        using var dir = new TempDir();
        var path = dir.Combine("book.txt");
        File.WriteAllText(path, "Chapter 1\n\nProse here.");

        Assert.Equal("text", ManuscriptReader.Read(path).Format);
    }

    [Fact]
    public void Read_RtfFromDisk()
    {
        using var dir = new TempDir();
        var path = dir.Combine("book.rtf");
        File.WriteAllText(path, @"{\rtf1\ansi She stepped off the train.\par}");

        var document = ManuscriptReader.Read(path);

        Assert.Equal("rtf", document.Format);
        Assert.Contains(document.Paragraphs, p => p.Text.Contains("She stepped off"));
    }

    [Fact]
    public void Epub_SpineEntryPointingAtAMissingFileIsSkipped()
    {
        using var dir = new TempDir();
        var path = dir.Combine("book.epub");
        using (var stream = File.Create(path))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "META-INF/container.xml",
                "<?xml version=\"1.0\"?><container><rootfiles><rootfile full-path=\"content.opf\"/></rootfiles></container>");
            WriteEntry(zip, "content.opf",
                "<?xml version=\"1.0\"?><package><manifest>"
                + "<item id=\"gone\" href=\"missing.xhtml\"/>"
                + "<item id=\"here\" href=\"present.xhtml\"/>"
                + "</manifest><spine><itemref idref=\"gone\"/><itemref idref=\"here\"/></spine></package>");
            WriteEntry(zip, "present.xhtml", "<html><body><h1>Real</h1><p>Text.</p></body></html>");
        }

        var plan = ManuscriptSplitter.Split(ManuscriptReader.Read(path));

        // A broken reference must not lose the chapters that do exist.
        Assert.Equal("Real", Assert.Single(plan.Chapters).Title);
    }

    [Fact]
    public void Epub_MalformedPackageFallsBackToNameOrder()
    {
        using var dir = new TempDir();
        var path = dir.Combine("book.epub");
        using (var stream = File.Create(path))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "META-INF/container.xml", "<container><unclosed>");
            WriteEntry(zip, "a.xhtml", "<html><body><h1>Alpha</h1><p>One.</p></body></html>");
        }

        var plan = ManuscriptSplitter.Split(ManuscriptReader.Read(path));

        Assert.Equal("Alpha", Assert.Single(plan.Chapters).Title);
    }

    [Fact]
    public void Epub_ValidPackageWithAnEmptySpine_FallsBackToNameOrder()
    {
        using var dir = new TempDir();
        var path = dir.Combine("book.epub");
        using (var stream = File.Create(path))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "META-INF/container.xml",
                "<?xml version=\"1.0\"?><container><rootfiles><rootfile full-path=\"content.opf\"/></rootfiles></container>");
            // Parses fine, but names no reading order.
            WriteEntry(zip, "content.opf",
                "<?xml version=\"1.0\"?><package><manifest/><spine/></package>");
            WriteEntry(zip, "a.xhtml", "<html><body><h1>Alpha</h1><p>One.</p></body></html>");
        }

        var plan = ManuscriptSplitter.Split(ManuscriptReader.Read(path));

        Assert.Equal("Alpha", Assert.Single(plan.Chapters).Title);
    }
}
