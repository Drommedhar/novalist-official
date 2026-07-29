using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// The print spec reaching the PDF.
///
/// A page size and a margin that live in a preset and never change a byte of
/// the file are the exact failure this project has shipped before: a setting
/// wired to nothing looks identical to a setting that works.
/// </summary>
public class ExportPrintSpecTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly IProjectService _project = Substitute.For<IProjectService>();

    public void Dispose() => _dir.Dispose();

    /// <summary>
    /// Enough prose to run to several pages, so page geometry matters.
    ///
    /// The layout lives on the book rather than on the caller: every export
    /// path reads the presets off the open project, which is the wiring this
    /// has to go through to prove anything.
    /// </summary>
    private ExportService BuildBook(out string chapterGuid, PrintSpec? spec, int paragraphs = 40)
    {
        var ch = new ChapterData { Title = "One", Order = 1 };
        var sc = new SceneData { Title = "S", Order = 1, ChapterGuid = ch.Guid };
        var body = string.Concat(Enumerable.Repeat(
            "<p>The bell rang once and the town heard it and did nothing at all about it. </p>",
            paragraphs));
        _project.ReadSceneContentAsync(ch, sc).Returns(body);
        _project.GetChaptersOrdered().Returns([ch]);
        _project.GetScenesForChapter(ch.Guid).Returns([sc]);
        _project.ActiveBook.Returns(new BookData
        {
            ExportPresets = [ExportPresets.All.First() with { Id = PresetId, Print = spec }]
        });
        chapterGuid = ch.Guid;
        return new ExportService(_project);
    }

    private const string PresetId = "print-test";

    private async Task<string> ExportAsync(PrintSpec? spec, string name = "book.pdf")
    {
        var sut = BuildBook(out var guid, spec);
        var path = _dir.Combine(name);
        await sut.ExportAsync(new ExportOptions
        {
            Format = ExportFormat.Pdf,
            Title = "T",
            SelectedChapterGuids = [guid],
            PresetId = PresetId
        }, path);
        return path;
    }

    private static (double Width, double Height) FirstPageSize(string path)
    {
        using var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(
            path, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.ReadOnly);
        var page = doc.Pages[0];
        return (page.Width.Inch, page.Height.Inch);
    }

    private static int PageCount(string path)
    {
        using var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(
            path, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.ReadOnly);
        return doc.PageCount;
    }

    [Fact]
    public async Task NoPrintSpec_KeepsTheManuscriptPage()
    {
        var (width, height) = FirstPageSize(await ExportAsync(null, "manuscript.pdf"));

        Assert.Equal(8.5, width, 2);
        Assert.Equal(11.0, height, 2);
    }

    [Fact]
    public async Task TheTrimSizeReachesTheFile()
    {
        var path = await ExportAsync(new PrintSpec
        {
            TrimWidthInches = 6,
            TrimHeightInches = 9
        }, "trade.pdf");

        var (width, height) = FirstPageSize(path);
        Assert.Equal(6.0, width, 2);
        Assert.Equal(9.0, height, 2);
    }

    [Fact]
    public async Task BleedMakesTheSheetBiggerThanTheTrim()
    {
        var path = await ExportAsync(new PrintSpec
        {
            TrimWidthInches = 6,
            TrimHeightInches = 9,
            BleedInches = 0.125
        }, "bleed.pdf");

        var (width, height) = FirstPageSize(path);
        Assert.Equal(6.25, width, 2);
        Assert.Equal(9.25, height, 2);
    }

    [Fact]
    public async Task BleedMarksWhereThePrinterCuts()
    {
        // A file that does not say where the cut goes is the commonest reason
        // a print job comes back.
        var path = await ExportAsync(new PrintSpec
        {
            TrimWidthInches = 6,
            TrimHeightInches = 9,
            BleedInches = 0.125
        }, "boxes.pdf");

        using var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(
            path, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.ReadOnly);
        var trim = doc.Pages[0].TrimBox;
        Assert.Equal(6.0, (trim.X2 - trim.X1) / 72.0, 2);
        Assert.Equal(9.0, (trim.Y2 - trim.Y1) / 72.0, 2);
    }

    [Fact]
    public async Task WithoutBleedNoBoxesAreWritten()
    {
        // With the two boxes equal the entries carry nothing a reader does not
        // already have from the media box.
        var path = await ExportAsync(new PrintSpec { TrimWidthInches = 6 }, "nobleed.pdf");

        using var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(
            path, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.ReadOnly);
        // An unset box reads as empty. A reader falls back to the media box,
        // which is what "the sheet is the page" means.
        Assert.Equal(0, doc.Pages[0].TrimBox.Width, 2);
    }

    [Fact]
    public async Task AWiderGutterFitsLessOnThePage()
    {
        // The proof that the margin reaches the layout rather than only the
        // page box: the same prose has to run longer.
        var narrow = await ExportAsync(new PrintSpec
        {
            TrimWidthInches = 6,
            TrimHeightInches = 9,
            GutterFromPageCount = false,
            GutterInches = 0
        }, "narrow.pdf");
        var wide = await ExportAsync(new PrintSpec
        {
            TrimWidthInches = 6,
            TrimHeightInches = 9,
            GutterFromPageCount = false,
            GutterInches = 1.5,
            MarginInsideInches = 1.2
        }, "wide.pdf");

        Assert.True(PageCount(wide) > PageCount(narrow));
    }

    [Fact]
    public async Task TheFileIsStillAPdf()
    {
        var path = await ExportAsync(new PrintSpec { TrimWidthInches = 5.5, TrimHeightInches = 8.5 });
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public async Task AnAutoGutterProducesAFileForALongBook()
    {
        // The two-pass render: the gutter changes the measure, the measure
        // changes the page count, and the page count changes the gutter.
        var sut = BuildBook(out var guid, new PrintSpec
        {
            TrimWidthInches = 4.25,
            TrimHeightInches = 6.87,
            GutterFromPageCount = true
        }, paragraphs: 1600);
        var path = _dir.Combine("long.pdf");

        await sut.ExportAsync(new ExportOptions
        {
            Format = ExportFormat.Pdf,
            Title = "T",
            SelectedChapterGuids = [guid],
            PresetId = PresetId
        }, path);

        // Long enough that the gutter step changes between the two passes,
        // which is the whole reason the second pass exists.
        Assert.True(PageCount(path) > 150, $"only {PageCount(path)} pages");
    }

    // ── Widows and orphans ──

    [Theory]
    // Plenty of room and plenty of paragraph: nothing to fix.
    [InlineData(10, 0, 800, false)]
    // Only room for one line here, and the paragraph is longer than the rule.
    [InlineData(10, 780, 800, true)]
    // Room for three of its four lines, so one would be carried over alone.
    [InlineData(4, 740, 800, true)]
    // Short enough to land whole wherever it goes.
    [InlineData(2, 780, 800, false)]
    public void BreaksBadly_MovesAParagraphThatWouldStrandALine(
        int lineCount, double y, double pageBottom, bool expected)
        => Assert.Equal(expected, ExportService.BreaksBadly(
            new PrintSpec(), lineCount, y, lineSpacing: 20, pageBottom: pageBottom));

    [Fact]
    public void BreaksBadly_IsOffWhenTheWriterTurnedItOff()
        => Assert.False(ExportService.BreaksBadly(
            new PrintSpec { AvoidWidowsAndOrphans = false }, 10, 780, 20, 800));

    [Fact]
    public void BreaksBadly_ARuleOfOneLineIsNoRule()
        // "Never leave fewer than one line" is satisfied by every break there
        // is, so it is off rather than a no-op that still costs a page break.
        => Assert.False(ExportService.BreaksBadly(
            new PrintSpec { MinLinesTogether = 1 }, 10, 780, 20, 800));

    [Fact]
    public void BreaksBadly_OnAPageWithNoRoomLeftTheNormalBreakHandlesIt()
        => Assert.False(ExportService.BreaksBadly(new PrintSpec(), 10, 900, 20, 800));
}
