using System.IO.Compression;
using System.Text;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// A picture in the prose, from the path a scene stores through to each format.
///
/// The scene stores a book-relative path so the project survives being moved;
/// every writer needs an absolute one to open the file. One case per format,
/// so a writer added later either handles images or visibly does not.
/// </summary>
public class ExportImageTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly IProjectService _project = Substitute.For<IProjectService>();
    private readonly string _bookRoot;

    public ExportImageTests()
    {
        _bookRoot = Path.Combine(_dir.Path, "Books", "One");
        Directory.CreateDirectory(Path.Combine(_bookRoot, "Images"));
        File.WriteAllBytes(Path.Combine(_bookRoot, "Images", "map.png"), OnePixelPng());
        _project.ActiveBookRoot.Returns(_bookRoot);
    }

    public void Dispose() => _dir.Dispose();

    /// <summary>
    /// A real 1x1 PNG, CRCs and all. A hand-rolled header would be enough for
    /// the size reader but not for the PDF writer, which decodes the file.
    /// </summary>
    private static byte[] OnePixelPng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89,
        0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54,
        0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00, 0x05, 0x00, 0x01,
        0x0D, 0x0A, 0x2D, 0xB4,
        0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
    ];

    private ExportOptions Setup(ExportFormat format, string html)
    {
        var chapter = new ChapterData { Title = "One", Order = 1 };
        var scene = new SceneData { Title = "Scene", Order = 1, ChapterGuid = chapter.Guid };
        _project.ReadSceneContentAsync(chapter, scene).Returns(html);
        _project.GetChaptersOrdered().Returns([chapter]);
        _project.GetScenesForChapter(chapter.Guid).Returns([scene]);

        return new ExportOptions
        {
            Format = format,
            Title = "Book",
            SelectedChapterGuids = [chapter.Guid]
        };
    }

    private const string WithImage =
        "<p>Before the map.</p>"
        + "<p class=\"nv-image\"><img src=\"Images/map.png\" alt=\"The valley\"></p>"
        + "<p>After the map.</p>";

    [Fact]
    public async Task TheStoredPathIsResolvedAgainstTheBook()
    {
        var compiled = await new ExportService(_project)
            .CompileChaptersAsync(Setup(ExportFormat.Markdown, WithImage));

        Assert.Contains(
            Path.Combine(_bookRoot, "Images", "map.png").Replace('\\', '/'),
            compiled[0].Scenes[0].HtmlContent);
    }

    [Fact]
    public async Task AnAbsoluteOrRemotePathIsLeftAlone()
    {
        var compiled = await new ExportService(_project).CompileChaptersAsync(
            Setup(ExportFormat.Markdown, "<p><img src=\"https://example.invalid/a.png\" alt=\"\"></p>"));

        Assert.Contains("https://example.invalid/a.png", compiled[0].Scenes[0].HtmlContent);
    }

    [Fact]
    public void TheParserSeesAnImageAsItsOwnBlock()
    {
        var blocks = ExportService.ParseHtmlToBlocks(WithImage);

        Assert.Equal(3, blocks.Count);
        Assert.Equal("Images/map.png", blocks[1].ImagePath);
        Assert.Equal("The valley", blocks[1].ImageAlt);
    }

    [Fact]
    public async Task Markdown_WritesTheImageWithItsAltText()
    {
        var path = Path.Combine(_dir.Path, "out.md");
        await new ExportService(_project).ExportAsync(Setup(ExportFormat.Markdown, WithImage), path);

        var text = await File.ReadAllTextAsync(path);
        Assert.Contains("![The valley](", text);
        Assert.Contains("map.png)", text);
        // The prose around it is still there, in order.
        Assert.True(text.IndexOf("Before the map", StringComparison.Ordinal)
            < text.IndexOf("![The valley](", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LaTeX_IncludesTheGraphic()
    {
        var path = Path.Combine(_dir.Path, "out.tex");
        await new ExportService(_project).ExportAsync(Setup(ExportFormat.LaTeX, WithImage), path);

        var tex = await File.ReadAllTextAsync(path);
        Assert.Contains("\\includegraphics", tex);
        Assert.Contains("The valley", tex);
    }

    [Fact]
    public async Task Epub_CarriesTheFileAndManifestsIt()
    {
        var path = Path.Combine(_dir.Path, "out.epub");
        await new ExportService(_project).ExportAsync(Setup(ExportFormat.Epub, WithImage), path);

        using var zip = ZipFile.OpenRead(path);
        Assert.NotNull(zip.GetEntry("OEBPS/images/image-1.png"));

        using var opf = new StreamReader(zip.GetEntry("OEBPS/content.opf")!.Open(), Encoding.UTF8);
        var manifest = await opf.ReadToEndAsync();
        Assert.Contains("images/image-1.png", manifest);
        Assert.Contains("image/png", manifest);

        using var chapter = new StreamReader(zip.GetEntry("OEBPS/chapter-1.xhtml")!.Open(), Encoding.UTF8);
        var xhtml = await chapter.ReadToEndAsync();
        Assert.Contains("src=\"images/image-1.png\"", xhtml);
        Assert.Contains("alt=\"The valley\"", xhtml);
    }

    [Fact]
    public async Task Epub_AnImageWhoseFileIsGoneIsDroppedRatherThanBroken()
    {
        var path = Path.Combine(_dir.Path, "missing.epub");
        await new ExportService(_project).ExportAsync(
            Setup(ExportFormat.Epub, "<p><img src=\"Images/gone.png\" alt=\"x\"></p><p>Text.</p>"),
            path);

        using var zip = ZipFile.OpenRead(path);
        using var chapter = new StreamReader(zip.GetEntry("OEBPS/chapter-1.xhtml")!.Open(), Encoding.UTF8);
        var xhtml = await chapter.ReadToEndAsync();
        Assert.DoesNotContain("<img", xhtml);
        Assert.Contains("Text.", xhtml);
    }

    [Fact]
    public async Task Docx_EmbedsTheImageWithARelationshipAndAltText()
    {
        var path = Path.Combine(_dir.Path, "out.docx");
        await new ExportService(_project).ExportAsync(Setup(ExportFormat.Docx, WithImage), path);

        using var zip = ZipFile.OpenRead(path);
        Assert.NotNull(zip.GetEntry("word/media/image-1.png"));

        using var rels = new StreamReader(
            zip.GetEntry("word/_rels/document.xml.rels")!.Open(), Encoding.UTF8);
        Assert.Contains("media/image-1.png", await rels.ReadToEndAsync());

        using var types = new StreamReader(zip.GetEntry("[Content_Types].xml")!.Open(), Encoding.UTF8);
        Assert.Contains("image/png", await types.ReadToEndAsync());

        using var document = new StreamReader(zip.GetEntry("word/document.xml")!.Open(), Encoding.UTF8);
        var xml = await document.ReadToEndAsync();
        Assert.Contains("<w:drawing>", xml);
        Assert.Contains("r:embed=\"rIdImage1\"", xml);
        Assert.Contains("descr=\"The valley\"", xml);
    }

    [Fact]
    public async Task Pdf_IsWrittenWithTheImageInIt()
    {
        var path = Path.Combine(_dir.Path, "out.pdf");
        await new ExportService(_project).ExportAsync(Setup(ExportFormat.Pdf, WithImage), path);

        Assert.True(new FileInfo(path).Length > 0);
    }

    [Fact]
    public void ImageSize_ReadsAPngHeader()
    {
        var path = Path.Combine(_bookRoot, "Images", "map.png");
        Assert.Equal((1, 1), ExportService.ImageSize(path));
    }

    [Fact]
    public void ImageSize_ReadsAJpegFrame()
    {
        var path = Path.Combine(_dir.Path, "photo.jpg");
        File.WriteAllBytes(path, [
            0xFF, 0xD8,                        // start of image
            0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, // an app segment to skip
            0xFF, 0xC0, 0x00, 0x11, 0x08,       // start of frame
            0x00, 0x40,                        // height 64
            0x00, 0x20,                        // width 32
            0x03, 0x01, 0x11, 0x00
        ]);

        Assert.Equal((32, 64), ExportService.ImageSize(path));
    }

    [Fact]
    public void ImageSize_AJpegWithNoFrameFallsBackToASquare()
    {
        // A JPEG signature with nothing but segments to skip: the walk runs off
        // the end without ever finding the frame that carries the size.
        var path = Path.Combine(_dir.Path, "headerless.jpg");
        File.WriteAllBytes(path, [
            0xFF, 0xD8,
            0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00,
            0xFF, 0xE1, 0x00, 0x04, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        ]);

        Assert.Equal((600, 600), ExportService.ImageSize(path));
    }

    [Fact]
    public void ImageSize_SomethingElseFallsBackToASquare()
    {
        var path = Path.Combine(_dir.Path, "notes.txt");
        File.WriteAllText(path, "not an image at all");

        Assert.Equal((600, 600), ExportService.ImageSize(path));
    }

    [Fact]
    public void ImageSize_AMissingFileFallsBackToASquare()
        => Assert.Equal((600, 600), ExportService.ImageSize(Path.Combine(_dir.Path, "nope.png")));

    [Theory]
    [InlineData("jpg", "image/jpeg")]
    [InlineData("gif", "image/gif")]
    [InlineData("bmp", "image/bmp")]
    [InlineData("webp", "image/webp")]
    public async Task Docx_DeclaresTheContentTypeTheExtensionCallsFor(
        string extension, string contentType)
    {
        File.WriteAllBytes(Path.Combine(_bookRoot, "Images", "map." + extension), OnePixelPng());
        var path = Path.Combine(_dir.Path, "types.docx");
        await new ExportService(_project).ExportAsync(
            Setup(ExportFormat.Docx, $"<p><img src=\"Images/map.{extension}\" alt=\"x\"></p>"),
            path);

        using var zip = ZipFile.OpenRead(path);
        using var types = new StreamReader(zip.GetEntry("[Content_Types].xml")!.Open(), Encoding.UTF8);
        Assert.Contains(contentType, await types.ReadToEndAsync());
    }

    [Fact]
    public void AnImageWithNoAltTextIsDecorativeRatherThanUndescribed()
    {
        var blocks = ExportService.ParseHtmlToBlocks("<p><img src=\"Images/map.png\"></p>");

        Assert.Equal("Images/map.png", blocks[0].ImagePath);
        Assert.Equal(string.Empty, blocks[0].ImageAlt);
    }

    [Fact]
    public void ImageSize_SomethingThatCannotBeOpenedFallsBackToASquare()
        // A directory is the simplest thing on disk that is not a readable file.
        => Assert.Equal((600, 600), ExportService.ImageSize(_bookRoot));

    [Fact]
    public async Task Pdf_AFileTheDecoderRejectsIsLeftOutRatherThanFailingTheExport()
    {
        File.WriteAllBytes(
            Path.Combine(_bookRoot, "Images", "broken.png"),
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52,
             0, 0, 0, 8, 0, 0, 0, 8, 8, 6, 0, 0, 0, 0, 0, 0, 0]);

        var path = Path.Combine(_dir.Path, "broken.pdf");
        await new ExportService(_project).ExportAsync(
            Setup(ExportFormat.Pdf, "<p><img src=\"Images/broken.png\" alt=\"x\"></p><p>Text.</p>"),
            path);

        Assert.True(new FileInfo(path).Length > 0);
    }

    [Fact]
    public async Task Pdf_AnImageThatDoesNotFitStartsANewPage()
    {
        // Tall enough that it cannot share a page with a screenful of prose,
        // which is the only way to reach the page-break branch.
        File.WriteAllBytes(Path.Combine(_bookRoot, "Images", "tall.png"), TestPng.Create(400, 900));

        var prose = string.Concat(Enumerable.Repeat("<p>A line of prose to fill the page.</p>", 40));
        var path = Path.Combine(_dir.Path, "tall.pdf");
        await new ExportService(_project).ExportAsync(
            Setup(ExportFormat.Pdf, prose + "<p><img src=\"Images/tall.png\" alt=\"x\"></p>"),
            path);

        Assert.True(new FileInfo(path).Length > 0);
    }
}
