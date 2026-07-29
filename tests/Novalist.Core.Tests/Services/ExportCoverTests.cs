using System.IO.Compression;
using System.Text;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// The cover reaching the exported file, and dc:language following the book's
/// writing language instead of a hardcoded "en".
/// </summary>
public class ExportCoverTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly IProjectService _project = Substitute.For<IProjectService>();

    public void Dispose() => _dir.Dispose();

    /// <summary>One chapter with one scene, enough for every format to produce a file.</summary>
    private ExportService BuildWithOneChapter(out string chapterGuid)
    {
        var ch = new ChapterData { Title = "One", Order = 1 };
        var sc = new SceneData { Title = "S", Order = 1, ChapterGuid = ch.Guid };
        _project.ReadSceneContentAsync(ch, sc).Returns("<p>text</p>");
        _project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        _project.GetScenesForChapter(ch.Guid).Returns(new List<SceneData> { sc });
        chapterGuid = ch.Guid;
        return new ExportService(_project);
    }

    /// <summary>Smallest valid PNG: a 1x1 opaque pixel.</summary>
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static string WriteCover(TempDir dir, string name)
    {
        var path = dir.Combine(name);
        File.WriteAllBytes(path, OnePixelPng);
        return path;
    }

    private static string ReadEntry(string epubPath, string entry)
    {
        using var zip = ZipFile.OpenRead(epubPath);
        var found = zip.GetEntry(entry);
        Assert.NotNull(found);
        using var reader = new StreamReader(found!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private async Task<string> ExportEpubAsync(ExportOptions options)
    {
        var sut = BuildWithOneChapter(out var guid);
        options.Format = ExportFormat.Epub;
        options.SelectedChapterGuids = [guid];
        var outPath = _dir.Combine("book.epub");
        await sut.ExportAsync(options, outPath);
        return outPath;
    }

    private async Task<string> ExportPdfAsync(ExportOptions options, string fileName)
    {
        var sut = BuildWithOneChapter(out var guid);
        options.Format = ExportFormat.Pdf;
        options.SelectedChapterGuids = [guid];
        var outPath = _dir.Combine(fileName);
        await sut.ExportAsync(options, outPath);
        return outPath;
    }

    [Fact]
    public async Task Epub_WithCover_WritesImageManifestEntryAndCoverPage()
    {
        var options = new ExportOptions
        {
            Title = "My Novel",
            Author = "A Writer",
            CoverImagePath = WriteCover(_dir, "cover.png")
        };

        var epub = await ExportEpubAsync(options);

        using (var zip = ZipFile.OpenRead(epub))
        {
            Assert.NotNull(zip.GetEntry("OEBPS/cover.png"));
            Assert.NotNull(zip.GetEntry("OEBPS/cover.xhtml"));
        }

        var opf = ReadEntry(epub, "OEBPS/content.opf");
        Assert.Contains("properties=\"cover-image\"", opf);
        Assert.Contains("media-type=\"image/png\"", opf);
        // EPUB 2 pointer that Kindle and several retailers still read.
        Assert.Contains("<meta name=\"cover\" content=\"cover-image\"/>", opf);
        // Cover comes first in the spine, ahead of the title page.
        var coverRef = opf.IndexOf("idref=\"cover\"", StringComparison.Ordinal);
        var titleRef = opf.IndexOf("idref=\"title\"", StringComparison.Ordinal);
        Assert.True(coverRef >= 0 && coverRef < titleRef);
    }

    [Fact]
    public async Task Epub_WithoutCover_HasNoCoverEntries()
    {
        var epub = await ExportEpubAsync(new ExportOptions { Title = "No Cover" });

        using var zip = ZipFile.OpenRead(epub);
        Assert.Null(zip.GetEntry("OEBPS/cover.xhtml"));
        Assert.DoesNotContain("cover-image", ReadEntry(epub, "OEBPS/content.opf"));
    }

    [Fact]
    public async Task Epub_MissingCoverFile_ExportsWithoutFailing()
    {
        var options = new ExportOptions
        {
            Title = "Gone",
            CoverImagePath = _dir.Combine("does-not-exist.png")
        };

        var epub = await ExportEpubAsync(options);

        Assert.True(File.Exists(epub));
        using var zip = ZipFile.OpenRead(epub);
        Assert.Null(zip.GetEntry("OEBPS/cover.xhtml"));
    }

    [Fact]
    public async Task Epub_UnsupportedCoverExtension_IsIgnored()
    {
        var bmp = _dir.Combine("cover.bmp");
        File.WriteAllBytes(bmp, OnePixelPng);

        var epub = await ExportEpubAsync(new ExportOptions { Title = "T", CoverImagePath = bmp });

        using var zip = ZipFile.OpenRead(epub);
        Assert.Null(zip.GetEntry("OEBPS/cover.bmp"));
    }

    [Fact]
    public async Task Epub_LanguageFollowsTheBook()
    {
        var epub = await ExportEpubAsync(new ExportOptions { Title = "Roman", Language = "de" });

        Assert.Contains("<dc:language>de</dc:language>", ReadEntry(epub, "OEBPS/content.opf"));
    }

    [Fact]
    public async Task Epub_BlankLanguageFallsBackToEnglish()
    {
        var epub = await ExportEpubAsync(new ExportOptions { Title = "T", Language = "  " });

        Assert.Contains("<dc:language>en</dc:language>", ReadEntry(epub, "OEBPS/content.opf"));
    }

    [Theory]
    [InlineData("de-low", "de")]
    [InlineData("de-guillemet", "de")]
    [InlineData("EN", "en")]
    [InlineData("zh-CN", "zh")]
    [InlineData("pt", "pt")]
    [InlineData(null, "en")]
    [InlineData("", "en")]
    [InlineData("   ", "en")]
    [InlineData("123", "en")]
    [InlineData("toolongtag", "en")]
    public void NormalizeLanguageTag_ReducesToPrimarySubtag(string? given, string expected) =>
        Assert.Equal(expected, ExportService.NormalizeLanguageTag(given));

    [Theory]
    [InlineData("cover.jpg", "image/jpeg")]
    [InlineData("cover.jpeg", "image/jpeg")]
    [InlineData("cover.PNG", "image/png")]
    [InlineData("cover.gif", "image/gif")]
    [InlineData("cover.webp", "image/webp")]
    public void CoverMediaType_MapsKnownExtensions(string name, string expected)
    {
        Assert.Equal(expected, ExportService.CoverMediaType(WriteCover(_dir, name)));
    }

    [Fact]
    public void CoverMediaType_RejectsUnusableCovers()
    {
        Assert.Null(ExportService.CoverMediaType(null));
        Assert.Null(ExportService.CoverMediaType(""));
        Assert.Null(ExportService.CoverMediaType("   "));
        Assert.Null(ExportService.CoverMediaType(_dir.Combine("missing.png")));
        Assert.Null(ExportService.CoverMediaType(WriteCover(_dir, "cover.bmp")));
    }

    [Fact]
    public async Task Pdf_WithCover_AddsAPageAheadOfTheTitlePage()
    {
        var withCover = await ExportPdfAsync(
            new ExportOptions
            {
                Title = "T",
                IncludeTitlePage = true,
                CoverImagePath = WriteCover(_dir, "cover.png")
            },
            "book.pdf");
        var noCover = await ExportPdfAsync(
            new ExportOptions { Title = "T", IncludeTitlePage = true }, "plain.pdf");

        Assert.Equal(PdfPageCount(noCover) + 1, PdfPageCount(withCover));
    }

    [Fact]
    public async Task Pdf_UndecodableCover_StillProducesTheBook()
    {
        var broken = _dir.Combine("broken.png");
        File.WriteAllText(broken, "this is not a png");

        var outPath = await ExportPdfAsync(
            new ExportOptions { Title = "T", CoverImagePath = broken }, "broken.pdf");

        Assert.True(new FileInfo(outPath).Length > 0);
    }

    private static int PdfPageCount(string path)
    {
        using var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(path, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.InformationOnly);
        return doc.PageCount;
    }

    [Fact]
    public async Task SuggestedEditsNeverReachAnExportedBook()
    {
        // An export is a finished book. An insertion nobody rejected is in it,
        // a deletion nobody accepted is not, and the markup itself belongs on
        // no page anywhere.
        var ch = new ChapterData { Title = "One", Order = 1 };
        var sc = new SceneData { Title = "S", Order = 1, ChapterGuid = ch.Guid };
        _project.ReadSceneContentAsync(ch, sc).Returns(
            "<p>The bell rang " +
            Novalist.Core.Utilities.TrackedChanges.Deletion("d", "once", "Mira", "now") +
            Novalist.Core.Utilities.TrackedChanges.Insertion("i", "twice", "Mira", "now") +
            ".</p>");
        _project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        _project.GetScenesForChapter(ch.Guid).Returns(new List<SceneData> { sc });

        var outPath = _dir.Combine("suggested.md");
        await new ExportService(_project).ExportAsync(new ExportOptions
        {
            Format = ExportFormat.Markdown,
            Title = "T",
            SelectedChapterGuids = [ch.Guid]
        }, outPath);

        var text = await File.ReadAllTextAsync(outPath);
        Assert.Contains("twice", text);
        Assert.DoesNotContain("once", text);
        Assert.DoesNotContain("<ins", text);
        Assert.DoesNotContain("<del", text);
    }
}
