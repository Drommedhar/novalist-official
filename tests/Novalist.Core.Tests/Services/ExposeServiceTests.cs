using System.IO.Compression;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class ExposeServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly IProjectService _project = Substitute.For<IProjectService>();
    private readonly BookData _book = new() { Id = "b1", Name = "Frostschwur" };

    public ExposeServiceTests()
    {
        _project.ActiveBookRoot.Returns(_dir.Path);
        _project.ActiveBook.Returns(_book);
    }

    public void Dispose() => _dir.Dispose();

    private ExposeService Build() => new(_project);

    private string ExposePath() => Path.Combine(_dir.Path, ExposeService.FileName);

    // ── Path ──

    [Fact]
    public void GetExposePath_SitsAtTheBookRoot()
        => Assert.Equal(ExposePath(), Build().GetExposePath());

    [Fact]
    public void GetExposePath_NullWithoutAnOpenBook()
    {
        _project.ActiveBookRoot.Returns((string?)null);
        Assert.Null(Build().GetExposePath());
    }

    // ── Read / write ──

    [Fact]
    public async Task Get_MissingFileReadsAsEmpty()
    {
        var state = await Build().GetAsync();
        Assert.Equal(string.Empty, state.Html);
        Assert.Equal(0, state.Characters);
        Assert.Equal(0, state.Pages);
    }

    [Fact]
    public async Task Save_WritesTheFileAndCountsTheText()
    {
        var state = await Build().SaveAsync("<p>Eine Heldin bricht auf.</p>");

        Assert.True(File.Exists(ExposePath()));
        Assert.Equal("<p>Eine Heldin bricht auf.</p>", await File.ReadAllTextAsync(ExposePath()));
        Assert.Equal("Eine Heldin bricht auf.".Length, state.Characters);
        Assert.Equal(1, state.Lines);
        Assert.Equal(1, state.Pages);
    }

    [Fact]
    public async Task Save_RoundTripsThroughGet()
    {
        await Build().SaveAsync("<p>Erster Satz.</p>");
        var state = await Build().GetAsync();
        Assert.Equal("<p>Erster Satz.</p>", state.Html);
        Assert.Equal(1, state.Pages);
    }

    [Fact]
    public async Task Save_WithoutAnOpenBookStillCounts()
    {
        _project.ActiveBookRoot.Returns((string?)null);
        var state = await Build().SaveAsync("<p>Text.</p>");
        Assert.False(File.Exists(ExposePath()));
        Assert.Equal(5, state.Characters);
    }

    [Fact]
    public async Task Save_NullTextIsStoredAsEmpty()
    {
        var state = await Build().SaveAsync(null!);
        Assert.Equal(string.Empty, await File.ReadAllTextAsync(ExposePath()));
        Assert.Equal(0, state.Characters);
    }

    [Fact]
    public void Measure_CountsWithoutWriting()
    {
        var state = Build().Measure("<p>Zaehl mich.</p>");
        Assert.Equal("Zaehl mich.".Length, state.Characters);
        Assert.False(File.Exists(ExposePath()));
    }

    [Fact]
    public void Measure_NullTextIsEmpty()
        => Assert.Equal(0, Build().Measure(null!).Characters);

    [Fact]
    public void Measure_LongTextSpansSeveralNormseiten()
    {
        // 30 lines fill exactly one page, so 61 one-line paragraphs need three.
        var html = string.Concat(Enumerable.Range(0, 61).Select(i => $"<p>Satz Nummer {i}.</p>"));
        var state = Build().Measure(html);
        Assert.Equal(61, state.Lines);
        Assert.Equal(3, state.Pages);
    }

    // ── Line semantics ──
    // An exposé is line-oriented: consecutive paragraphs stay on adjacent grid
    // lines and only an empty paragraph opens a blank one. This is what makes
    // the export reproduce the source document line for line.

    [Fact]
    public void Blocks_ConsecutiveParagraphsStayOnAdjacentLines()
    {
        var state = Build().Measure("<p>Genre: Thriller</p><p>Schauplatz: Hillsford</p>");
        Assert.Equal(2, state.Lines);
    }

    [Fact]
    public void Blocks_EmptyParagraphOpensABlankLine()
    {
        var state = Build().Measure("<p>Eins</p><p></p><p>Zwei</p>");
        Assert.Equal(3, state.Lines);
    }

    [Fact]
    public void Blocks_ParagraphWithOnlyABreakIsBlank()
        => Assert.Equal(3, Build().Measure("<p>Eins</p><p><br></p><p>Zwei</p>").Lines);

    [Fact]
    public void Blocks_BreakInsideAParagraphSplitsTheLine()
        => Assert.Equal(2, Build().Measure("<p>Eins<br/>Zwei</p>").Lines);

    [Fact]
    public void Blocks_HeadingIsTheTitle_NoBlankLinesAroundIt()
    {
        // Title: upper-cased, and the following paragraph is the very next line.
        var state = Build().Measure("<p class=\"nv-style-heading\">Titel</p><p>Text</p>");
        Assert.Equal(2, state.Lines);
    }

    [Fact]
    public void Blocks_SubheadingIsASectionHeading_BlankLineEitherSide()
    {
        var state = Build().Measure("<p>Text</p><p class=\"nv-style-subheading\">Abschnitt</p><p>Mehr</p>");
        Assert.Equal(5, state.Lines); // Text, blank, ABSCHNITT, blank, Mehr
    }

    [Fact]
    public void Blocks_UnknownStyleClassIsBodyText()
        => Assert.Equal(1, Build().Measure("<p class=\"other nv-style-poetry\">Vers</p>").Lines);

    [Fact]
    public void Blocks_ClasslessAttributesAreBodyText()
        => Assert.Equal(1, Build().Measure("<p style=\"text-align:center\">Mitte</p>").Lines);

    [Fact]
    public void Blocks_ClassWithoutAStyleTokenIsBodyText()
        => Assert.Equal(1, Build().Measure("<p class=\"plain\">Text</p>").Lines);

    [Fact]
    public void Blocks_TextWithoutParagraphTagsStillCounts()
    {
        var state = Build().Measure("Nur eine Zeile");
        Assert.Equal(1, state.Lines);
        Assert.Equal("Nur eine Zeile".Length, state.Characters);
    }

    [Fact]
    public void Blocks_EntitiesAreDecodedAndMarkupDropped()
    {
        var state = Build().Measure("<p><b>Fett</b> &amp; kursiv</p>");
        Assert.Equal("Fett & kursiv".Length, state.Characters);
    }

    // ── Limits ──

    [Fact]
    public async Task SetLimits_PersistsOnTheBook()
    {
        var state = await Build().SetLimitsAsync(15000, 10);
        Assert.Equal(15000, _book.ExposeCharLimit);
        Assert.Equal(10, _book.ExposePageLimit);
        Assert.Equal(15000, state.CharLimit);
        Assert.Equal(10, state.PageLimit);
        await _project.Received(1).SaveProjectAsync();
    }

    [Fact]
    public async Task SetLimits_NegativeMeansNoLimit()
    {
        await Build().SetLimitsAsync(-5, -1);
        Assert.Equal(0, _book.ExposeCharLimit);
        Assert.Equal(0, _book.ExposePageLimit);
    }

    [Fact]
    public async Task SetLimits_WithoutAnOpenBookIsANoOp()
    {
        _project.ActiveBook.Returns((BookData?)null);
        var state = await Build().SetLimitsAsync(100, 2);
        Assert.Equal(0, state.CharLimit);
        Assert.Equal(0, state.PageLimit);
        await _project.DidNotReceive().SaveProjectAsync();
    }

    [Fact]
    public async Task Get_ReportsTheStoredLimits()
    {
        _book.ExposeCharLimit = 15000;
        _book.ExposePageLimit = 10;
        var state = await Build().GetAsync();
        Assert.Equal(15000, state.CharLimit);
        Assert.Equal(10, state.PageLimit);
    }

    // ── Export ──

    [Fact]
    public async Task Export_EmptyExposeWritesNothing()
    {
        var output = Path.Combine(_dir.Path, "expose.docx");
        Assert.False(await Build().ExportAsync(output, "Frostschwur"));
        Assert.False(File.Exists(output));
    }

    private async Task<string> ExportedDocumentXmlAsync(string html, string headerTitle = "Frostschwur")
    {
        await Build().SaveAsync(html);
        var output = Path.Combine(_dir.Path, "expose.docx");
        Assert.True(await Build().ExportAsync(output, headerTitle));

        using var zip = ZipFile.OpenRead(output);
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        return await reader.ReadToEndAsync();
    }

    [Fact]
    public async Task Export_WritesANormseitenDocx()
    {
        var doc = await ExportedDocumentXmlAsync(
            "<p class=\"nv-style-heading\">Titel</p><p></p>" +
            "<p class=\"nv-style-subheading\">Handlung</p><p>Eine Heldin bricht auf.</p>");

        Assert.Contains("TITEL", doc);
        Assert.Contains("HANDLUNG", doc);
        Assert.Contains("Eine Heldin bricht auf.", doc);
    }

    [Fact]
    public async Task Export_BodyCarriesOnlyTheDocumentsOwnText()
    {
        // The book title heads the running header, not the body: the exposé
        // supplies its own title line, exactly as the source document has it.
        var doc = await ExportedDocumentXmlAsync("<p>Nur der Text.</p>");
        Assert.Contains("Nur der Text.", doc);
        Assert.DoesNotContain("FROSTSCHWUR", doc);
    }

    [Fact]
    public async Task Export_UntitledStillWrites()
    {
        var doc = await ExportedDocumentXmlAsync("<p>Nur der Text.</p>", headerTitle: "");
        Assert.Contains("Nur der Text.", doc);
    }
}
