using System.IO.Compression;
using System.Text;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Front- and back-matter conventions, and how a matter page is set in each
/// export format. The conventions are the whole point of typing these pages:
/// a dedication and a foreword are not laid out the same way.
/// </summary>
public class BookMatterTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly IProjectService _project = Substitute.For<IProjectService>();

    public void Dispose() => _dir.Dispose();

    // ── Conventions ──

    [Theory]
    [InlineData(BookMatterKind.HalfTitle, false)]
    [InlineData(BookMatterKind.TitlePage, false)]
    [InlineData(BookMatterKind.Copyright, false)]
    [InlineData(BookMatterKind.Dedication, false)]
    [InlineData(BookMatterKind.Epigraph, false)]
    [InlineData(BookMatterKind.Foreword, true)]
    [InlineData(BookMatterKind.Preface, true)]
    [InlineData(BookMatterKind.Prologue, true)]
    [InlineData(BookMatterKind.Epilogue, true)]
    [InlineData(BookMatterKind.Afterword, true)]
    [InlineData(BookMatterKind.Acknowledgments, true)]
    [InlineData(BookMatterKind.AboutTheAuthor, true)]
    [InlineData(BookMatterKind.AlsoBy, true)]
    [InlineData(BookMatterKind.TableOfContents, true)]
    [InlineData(BookMatterKind.Custom, true)]
    public void ShowsHeadingByDefault_FollowsPublishingConvention(BookMatterKind kind, bool expected) =>
        Assert.Equal(expected, BookMatterElement.ShowsHeadingByDefault(kind));

    [Theory]
    [InlineData(BookMatterKind.Epilogue, BookMatterPlacement.Back)]
    [InlineData(BookMatterKind.Afterword, BookMatterPlacement.Back)]
    [InlineData(BookMatterKind.Acknowledgments, BookMatterPlacement.Back)]
    [InlineData(BookMatterKind.AboutTheAuthor, BookMatterPlacement.Back)]
    [InlineData(BookMatterKind.AlsoBy, BookMatterPlacement.Back)]
    [InlineData(BookMatterKind.HalfTitle, BookMatterPlacement.Front)]
    [InlineData(BookMatterKind.Copyright, BookMatterPlacement.Front)]
    [InlineData(BookMatterKind.Dedication, BookMatterPlacement.Front)]
    [InlineData(BookMatterKind.Prologue, BookMatterPlacement.Front)]
    [InlineData(BookMatterKind.Custom, BookMatterPlacement.Front)]
    public void DefaultPlacement_PutsEachKindWhereItBelongs(
        BookMatterKind kind, BookMatterPlacement expected) =>
        Assert.Equal(expected, BookMatterElement.DefaultPlacement(kind));

    [Theory]
    [InlineData(BookMatterKind.Foreword, true)]
    [InlineData(BookMatterKind.Preface, true)]
    [InlineData(BookMatterKind.Prologue, true)]
    [InlineData(BookMatterKind.Epilogue, true)]
    [InlineData(BookMatterKind.Afterword, true)]
    [InlineData(BookMatterKind.Acknowledgments, true)]
    [InlineData(BookMatterKind.AboutTheAuthor, true)]
    [InlineData(BookMatterKind.Copyright, false)]
    [InlineData(BookMatterKind.HalfTitle, false)]
    [InlineData(BookMatterKind.Dedication, false)]
    [InlineData(BookMatterKind.Epigraph, false)]
    [InlineData(BookMatterKind.TitlePage, false)]
    [InlineData(BookMatterKind.TableOfContents, false)]
    [InlineData(BookMatterKind.AlsoBy, false)]
    [InlineData(BookMatterKind.Custom, false)]
    public void ListedInTableOfContentsByDefault_MatchesConvention(
        BookMatterKind kind, bool expected) =>
        Assert.Equal(expected, BookMatterElement.ListedInTableOfContentsByDefault(kind));

    [Fact]
    public void NewElement_GetsAnId() =>
        Assert.NotEmpty(new BookMatterElement().Id);

    // ── Heading resolution ──

    [Fact]
    public void ResolveMatterTitle_ExplicitTitleWins() =>
        Assert.Equal(
            "For Rachel",
            ExportService.ResolveMatterTitle(
                new BookMatterElement { Kind = BookMatterKind.Dedication, Title = "  For Rachel  " }));

    [Fact]
    public void ResolveMatterTitle_HeadingLessKindGetsNone() =>
        Assert.Equal(
            string.Empty,
            ExportService.ResolveMatterTitle(new BookMatterElement { Kind = BookMatterKind.Dedication }));

    [Fact]
    public void ResolveMatterTitle_HeadingKindGetsItsName() =>
        Assert.Equal(
            "Foreword",
            ExportService.ResolveMatterTitle(new BookMatterElement { Kind = BookMatterKind.Foreword }));

    [Theory]
    [InlineData("AboutTheAuthor", "About The Author")]
    [InlineData("AlsoBy", "Also By")]
    [InlineData("Foreword", "Foreword")]
    [InlineData("", "")]
    public void SpaceCamelCase_SplitsOnCapitals(string input, string expected) =>
        Assert.Equal(expected, ExportService.SpaceCamelCase(input));

    // ── Reaching the exported file ──

    private ExportService BuildWithMatter(List<BookMatterElement> matter, out string chapterGuid)
    {
        var chapter = new ChapterData { Title = "One", Order = 1 };
        var scene = new SceneData { Id = "s1", Title = "S", Order = 1, ChapterGuid = chapter.Guid };
        _project.ReadSceneContentAsync(chapter, scene).Returns("<p>Story.</p>");
        _project.GetChaptersOrdered().Returns(new List<ChapterData> { chapter });
        _project.GetScenesForChapter(chapter.Guid).Returns(new List<SceneData> { scene });
        _project.ActiveBook.Returns(new BookData { Matter = matter });
        chapterGuid = chapter.Guid;
        return new ExportService(_project);
    }

    private async Task<string> ExportAsync(List<BookMatterElement> matter, ExportFormat format, string ext)
    {
        var sut = BuildWithMatter(matter, out var guid);
        var outPath = _dir.Combine("book" + ext);
        await sut.ExportAsync(
            new ExportOptions { Format = format, Title = "T", SelectedChapterGuids = [guid] },
            outPath);
        return outPath;
    }

    private static string ReadEntry(string archive, string entry)
    {
        using var zip = ZipFile.OpenRead(archive);
        using var reader = new StreamReader(zip.GetEntry(entry)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static BookMatterElement Element(
        BookMatterKind kind, string content, BookMatterPlacement? placement = null, bool inToc = false) =>
        new()
        {
            Kind = kind,
            Content = content,
            Placement = placement ?? BookMatterElement.DefaultPlacement(kind),
            InTableOfContents = inToc
        };

    [Fact]
    public async Task Epub_MatterPageCarriesItsEpubType()
    {
        var epub = await ExportAsync(
            [Element(BookMatterKind.Copyright, "<p>All rights reserved.</p>")],
            ExportFormat.Epub, ".epub");

        var page = ReadEntry(epub, "OEBPS/matter-1.xhtml");
        Assert.Contains("epub:type=\"copyright-page\"", page);
        Assert.Contains("matter-copyright", page);
    }

    [Theory]
    [InlineData(BookMatterKind.HalfTitle, "halftitlepage")]
    [InlineData(BookMatterKind.TitlePage, "titlepage")]
    [InlineData(BookMatterKind.Dedication, "dedication")]
    [InlineData(BookMatterKind.Epigraph, "epigraph")]
    [InlineData(BookMatterKind.TableOfContents, "toc")]
    [InlineData(BookMatterKind.Foreword, "foreword")]
    [InlineData(BookMatterKind.Preface, "preface")]
    [InlineData(BookMatterKind.Prologue, "prologue")]
    [InlineData(BookMatterKind.Epilogue, "epilogue")]
    [InlineData(BookMatterKind.Afterword, "afterword")]
    [InlineData(BookMatterKind.Acknowledgments, "acknowledgments")]
    [InlineData(BookMatterKind.AboutTheAuthor, "frontmatter")]
    [InlineData(BookMatterKind.AlsoBy, "frontmatter")]
    [InlineData(BookMatterKind.Custom, "frontmatter")]
    public async Task Epub_EveryKindMapsToAValidEpubType(BookMatterKind kind, string expected)
    {
        var epub = await ExportAsync([Element(kind, "<p>Text.</p>")], ExportFormat.Epub, ".epub");

        Assert.Contains($"epub:type=\"{expected}\"", ReadEntry(epub, "OEBPS/matter-1.xhtml"));
    }

    [Fact]
    public async Task Epub_FrontMatterPrecedesAndBackMatterFollowsTheStory()
    {
        var epub = await ExportAsync(
            [
                Element(BookMatterKind.Dedication, "<p>For R.</p>"),
                Element(BookMatterKind.Acknowledgments, "<p>Thanks.</p>")
            ],
            ExportFormat.Epub, ".epub");

        var opf = ReadEntry(epub, "OEBPS/content.opf");
        var frontAt = opf.IndexOf("idref=\"matter-1\"", StringComparison.Ordinal);
        var chapterAt = opf.IndexOf("idref=\"chapter-1\"", StringComparison.Ordinal);
        var backAt = opf.IndexOf("idref=\"matter-2\"", StringComparison.Ordinal);

        Assert.True(frontAt < chapterAt);
        Assert.True(backAt > chapterAt);
    }

    [Fact]
    public async Task Epub_OnlyTocMarkedPagesAreListed()
    {
        var epub = await ExportAsync(
            [
                Element(BookMatterKind.Copyright, "<p>Small print.</p>"),
                Element(BookMatterKind.Foreword, "<p>Context.</p>", inToc: true),
                Element(BookMatterKind.Acknowledgments, "<p>Thanks.</p>", inToc: true)
            ],
            ExportFormat.Epub, ".epub");

        var nav = ReadEntry(epub, "OEBPS/nav.xhtml");
        Assert.Contains("Foreword", nav);
        Assert.Contains("Acknowledgments", nav);
        Assert.DoesNotContain("Copyright", nav);
    }

    [Fact]
    public async Task Epub_TocLabelUsesTheExplicitTitleWhenThereIsOne()
    {
        var element = Element(BookMatterKind.Custom, "<p>Text.</p>", inToc: true);
        element.Title = "A Note on Sources";

        var nav = ReadEntry(
            await ExportAsync([element], ExportFormat.Epub, ".epub"), "OEBPS/nav.xhtml");

        Assert.Contains("A Note on Sources", nav);
    }

    [Fact]
    public async Task Epub_ExcludedAndEmptyPagesAreLeftOut()
    {
        var excluded = Element(BookMatterKind.Dedication, "<p>For R.</p>");
        excluded.Included = false;

        var epub = await ExportAsync(
            [excluded, Element(BookMatterKind.Epigraph, "   ")],
            ExportFormat.Epub, ".epub");

        using var zip = ZipFile.OpenRead(epub);
        Assert.Null(zip.GetEntry("OEBPS/matter-1.xhtml"));
    }

    [Fact]
    public async Task Docx_MatterIsWrittenAroundTheStory()
    {
        var docx = await ExportAsync(
            [
                Element(BookMatterKind.Dedication, "<p>For R.</p>"),
                Element(BookMatterKind.Acknowledgments, "<p>Thanks.</p>")
            ],
            ExportFormat.Docx, ".docx");

        var document = ReadEntry(docx, "word/document.xml");
        var frontAt = document.IndexOf("For R.", StringComparison.Ordinal);
        var storyAt = document.IndexOf("Story.", StringComparison.Ordinal);
        var backAt = document.IndexOf("Thanks.", StringComparison.Ordinal);

        Assert.True(frontAt < storyAt);
        Assert.True(backAt > storyAt);
    }

    [Fact]
    public async Task Docx_HeadingKindsPrintAHeadingAndHeadingLessOnesDoNot()
    {
        var docx = await ExportAsync(
            [
                Element(BookMatterKind.Dedication, "<p>For R.</p>"),
                Element(BookMatterKind.Foreword, "<p>Context.</p>")
            ],
            ExportFormat.Docx, ".docx");

        var document = ReadEntry(docx, "word/document.xml");
        Assert.Contains("Foreword", document);
        Assert.DoesNotContain(">Dedication<", document);
    }

    [Fact]
    public async Task Docx_MatterStartsOnANewPage()
    {
        var docx = await ExportAsync(
            [Element(BookMatterKind.Dedication, "<p>For R.</p>")],
            ExportFormat.Docx, ".docx");

        Assert.Contains("pageBreakBefore", ReadEntry(docx, "word/document.xml"));
    }

    [Fact]
    public async Task Epub_NoMatter_ProducesTheSameBookAsBefore()
    {
        var epub = await ExportAsync([], ExportFormat.Epub, ".epub");

        using var zip = ZipFile.OpenRead(epub);
        Assert.Null(zip.GetEntry("OEBPS/matter-1.xhtml"));
        Assert.NotNull(zip.GetEntry("OEBPS/chapter-1.xhtml"));
    }
}
