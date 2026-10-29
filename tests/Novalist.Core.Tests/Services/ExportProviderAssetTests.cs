using System.IO.Compression;
using System.Text;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using NSubstitute;
using Xunit;

namespace Novalist.Core.Tests.Services;

public sealed class ExportProviderAssetTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly IProjectService _project = Substitute.For<IProjectService>();
    private readonly IEntityService _entities = Substitute.For<IEntityService>();
    private readonly ChapterData _chapter = new() { Title = "Chapter" };
    private readonly SceneData _scene = new() { Title = "Scene" };
    private readonly string _image;
    private string BookRoot => _temp.Combine("provider", "Book");

    public ExportProviderAssetTests()
    {
        _project.ActiveBookRoot.Returns(BookRoot);
        _project.GetChaptersOrdered().Returns([_chapter]);
        _project.GetScenesForChapter(_chapter.Guid).Returns([_scene]);
        _image = _temp.Combine("pixel.png");
        File.WriteAllBytes(_image, TestPng.Create(1, 1));
        _entities.LoadCharactersAsync().Returns([]);
        _entities.LoadLocationsAsync().Returns([]);
        _entities.LoadItemsAsync().Returns([]);
        _entities.LoadLoreAsync().Returns([]);
        _entities.GetImageFullPath(Arg.Any<string>()).Returns(call => Path.Combine(BookRoot, call.Arg<string>()));
    }

    public void Dispose() => _temp.Dispose();

    private ExportOptions Options(ExportFormat format, string html)
    {
        _project.ReadSceneContentAsync(_chapter, _scene).Returns(html);
        return new ExportOptions { Format = format, Title = "Book", SelectedChapterGuids = [_chapter.Guid] };
    }

    [Fact]
    public async Task EpubMaterializesEscapedImageSourcesWithoutChangingOtherAttributes()
    {
        var options = Options(ExportFormat.Epub,
            "<p><img alt=\"Map &amp; valley\" src=\"Images/a&amp;b.png\"></p>"
            + "<p><img src='Images/another.png' alt='Second'></p>");
        options.CoverImagePath = Path.Combine(BookRoot, "cover.png");
        var requested = new List<string>();
        var service = new ExportService(_project, materializeAsset: path =>
        {
            requested.Add(path);
            return Task.FromResult(_image);
        });
        var output = _temp.Combine("book.epub");

        await service.ExportAsync(options, output);

        Assert.Contains(Path.Combine(BookRoot, "Images", "a&b.png").Replace('\\', '/'), requested.Select(path => path.Replace('\\', '/')));
        using var zip = ZipFile.OpenRead(output);
        Assert.NotNull(zip.GetEntry("OEBPS/cover.png"));
        Assert.NotNull(zip.GetEntry("OEBPS/images/image-1.png"));
        Assert.Contains("alt=\"Map &amp; valley\"", ReadEntry(zip, "OEBPS/chapter-1.xhtml"));
        Assert.Contains("alt=\"Second\"", ReadEntry(zip, "OEBPS/chapter-1.xhtml"));
        Assert.Equal(Path.Combine(BookRoot, "cover.png"), options.CoverImagePath);
    }

    [Fact]
    public async Task MissingAndRemoteImageSourcesDoNotBecomeLocalAssets()
    {
        var options = Options(ExportFormat.Epub,
            "<p><img alt='Missing'></p><p><img src=''></p><p><img src='https://example.test/image.png'></p>");
        var service = new ExportService(_project, materializeAsset: _ => throw new InvalidOperationException("No local source"));
        var output = _temp.Combine("book.epub");

        await service.ExportAsync(options, output);

        using var zip = ZipFile.OpenRead(output);
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName.Contains("images/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DocxMaterializesItsReferenceTemplateAndRestoresTheOption()
    {
        var template = _temp.Combine("template.docx");
        using (var zip = ZipFile.Open(template, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("word/styles.xml").Open()))
            writer.Write("<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:style w:type=\"paragraph\" w:styleId=\"ProviderStyle\"/></w:styles>");
        var options = Options(ExportFormat.Docx, "<p>Prose</p>");
        options.ReferenceDocPath = Path.Combine(BookRoot, "template.docx");
        var output = _temp.Combine("book.docx");

        await new ExportService(_project, materializeAsset: _ => Task.FromResult(template)).ExportAsync(options, output);

        using var document = ZipFile.OpenRead(output);
        Assert.Contains("ProviderStyle", ReadEntry(document, "word/styles.xml"));
        Assert.Equal(Path.Combine(BookRoot, "template.docx"), options.ReferenceDocPath);
    }

    [Fact]
    public async Task FailedWriterRestoresTheOriginalCoverOption()
    {
        var options = Options(ExportFormat.Epub, "<p>Prose</p>");
        options.CoverImagePath = Path.Combine(BookRoot, "cover.png");
        var service = new ExportService(_project, materializeAsset: _ => Task.FromResult(_image));

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => service.ExportAsync(options, _temp.Combine("missing", "book.epub")));

        Assert.Equal(Path.Combine(BookRoot, "cover.png"), options.CoverImagePath);
    }

    [Theory]
    [InlineData(ExportFormat.Codex)]
    [InlineData(ExportFormat.CodexPdf)]
    public async Task CodexMaterializesSelectedImagesWithoutMutatingEntities(ExportFormat format)
    {
        var character = new CharacterData
        {
            Name = "Mira", Images = [new() { Path = "Images/map.png", Name = "Map" }, new() { Path = "Images/map.png", Name = "Again" }, new()]
        };
        _entities.LoadCharactersAsync().Returns([character, new CharacterData { Name = "Excluded", Images = [new() { Path = "Images/excluded.png" }] }]);
        var options = new ExportOptions { Format = format, SelectedEntityKeys = [$"character:{character.Id}"] };
        var service = new ExportService(_project, _entities, path =>
        {
            if (path != Path.Combine(BookRoot, "Images/map.png")) throw new IOException("Unselected image read");
            return Task.FromResult(_image);
        });
        var output = _temp.Combine(format == ExportFormat.Codex ? "codex.md" : "codex.pdf");

        if (format == ExportFormat.Codex) await service.ExportCodexAsync(options, output);
        else await service.ExportCodexPdfAsync(options, output);

        var text = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(output));
        Assert.Contains(format == ExportFormat.Codex ? "![Map](codex_images/pixel.png)" : "/Subtype /Image", text);
        Assert.Equal("Images/map.png", character.Images[0].Path);
    }

    [Theory]
    [InlineData(ExportFormat.Markdown)]
    [InlineData(ExportFormat.LaTeX)]
    public async Task TextExportsDoNotMaterializeDisposableAssetPaths(ExportFormat format)
    {
        var options = Options(format, "<p><img src=\"Images/map.png\" alt=\"Map\"></p>");
        var output = _temp.Combine("book.txt");

        await new ExportService(_project, materializeAsset: _ => throw new InvalidOperationException("Not a self-contained format"))
            .ExportAsync(options, output);

        Assert.Contains(Path.Combine(BookRoot, "Images", "map.png").Replace('\\', '/'), await File.ReadAllTextAsync(output));
    }

    private static string ReadEntry(ZipArchive zip, string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }
}
