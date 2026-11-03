using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SkiaSharp;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class PdfExportImageTests
{
    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png)]
    public async Task CodexPortraitsKeepTheir96DpiDisplaySize(SKEncodedImageFormat format)
    {
        using var dir = new TempDir();
        var path = dir.Combine("portrait");
        using (var bitmap = new SKBitmap(96, 96))
        {
            bitmap.Erase(SKColors.Blue);
            using var encoded = bitmap.Encode(format, 100);
            File.WriteAllBytes(path, encoded.ToArray());
        }
        var entities = Substitute.For<IEntityService>();
        entities.GetImageFullPath("portrait").Returns(path);
        entities.LoadCharactersAsync().Returns([new CharacterData
        {
            Name = "Mira", Images = [new EntityImage { Path = "portrait" }]
        }]);
        entities.LoadLocationsAsync().Returns([]);
        entities.LoadItemsAsync().Returns([]);
        entities.LoadLoreAsync().Returns([]);
        var output = dir.Combine("codex.pdf");
        await new ExportService(Substitute.For<IProjectService>(), entities)
            .ExportCodexPdfAsync(new ExportOptions { IncludeTitlePage = false }, output);

        using var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        var content = string.Join("\n", pdf.Pages.Cast<PdfPage>().Select(page =>
            System.Text.Encoding.ASCII.GetString(page.Contents.CreateSingleContent().Stream.UnfilteredValue)));
        // A 96-pixel portrait occupied one inch (72 points) before the dependency migration.
        Assert.Matches(@"72(?:\.0+)? 0 0 72(?:\.0+)? [\d.]+ [\d.]+ cm\s+/\w+ Do", content);
    }

    [Theory]
    [InlineData("cover", "png")]
    [InlineData("cover", "jpg")]
    [InlineData("scene", "png")]
    [InlineData("scene", "jpg")]
    [InlineData("codex", "png")]
    [InlineData("codex", "jpg")]
    [InlineData("cover", "webp")]
    [InlineData("scene", "webp")]
    [InlineData("codex", "webp")]
    [InlineData("cover", "gif")]
    [InlineData("scene", "gif")]
    [InlineData("codex", "gif")]
    [InlineData("scene", "bmp")]
    [InlineData("codex", "bmp")]
    public async Task PdfEmbedsImagesInsteadOfSilentlyDroppingThem(string location, string extension)
    {
        using var dir = new TempDir();
        var imagePath = dir.Combine($"picture.{extension}");
        if (extension == "gif")
            File.WriteAllBytes(imagePath, Convert.FromBase64String(
                "R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7"));
        else if (extension == "bmp")
        {
            using var writer = new BinaryWriter(File.Create(imagePath));
            writer.Write((ushort)0x4d42);
            writer.Write(58); // File size: headers plus one padded pixel row.
            writer.Write(0);
            writer.Write(54);
            writer.Write(40);
            writer.Write(1);
            writer.Write(1);
            writer.Write((ushort)1);
            writer.Write((ushort)24);
            writer.Write(0);
            writer.Write(4);
            writer.Write(new byte[16]);
            writer.Write(new byte[] { 200, 100, 20, 0 });
        }
        else using (var pixels = new SKBitmap(2, 3))
        {
            pixels.Erase(new SKColor(20, 100, 200, 100));
            var format = extension switch
            {
                "jpg" => SKEncodedImageFormat.Jpeg,
                "webp" => SKEncodedImageFormat.Webp,
                _ => SKEncodedImageFormat.Png
            };
            using var encoded = pixels.Encode(format, 100);
            File.WriteAllBytes(imagePath, encoded.ToArray());
        }

        var project = Substitute.For<IProjectService>();
        var entities = Substitute.For<IEntityService>();
        var chapter = new ChapterData { Title = "Chapter", Order = 1 };
        var scene = new SceneData { Title = "Scene", ChapterGuid = chapter.Guid, Order = 1 };
        project.GetChaptersOrdered().Returns([chapter]);
        project.GetScenesForChapter(chapter.Guid).Returns([scene]);
        project.ReadSceneContentAsync(chapter, scene).Returns(location == "scene"
            ? $"<p>Before</p><p><img src=\"{imagePath}\" /></p><p>After</p>"
            : "<p>Text</p>");
        entities.GetImageFullPath("portrait").Returns(imagePath);
        entities.LoadCharactersAsync().Returns([new CharacterData
        {
            Name = "Mira", Images = [new EntityImage { Path = "portrait" }]
        }]);
        entities.LoadLocationsAsync().Returns([]);
        entities.LoadItemsAsync().Returns([]);
        entities.LoadLoreAsync().Returns([]);

        var output = dir.Combine("book.pdf");
        await new ExportService(project, entities).ExportAsync(new ExportOptions
        {
            Format = location == "codex" ? ExportFormat.CodexPdf : ExportFormat.Pdf,
            Title = "Book",
            SelectedChapterGuids = [chapter.Guid],
            CoverImagePath = location == "cover" ? imagePath : ""
        }, output);

        using var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        var images = pdf.Internals.GetAllObjects().OfType<PdfDictionary>()
            .Where(item => item.Elements.GetName("/Subtype") == "/Image").ToList();
        Assert.NotEmpty(images);
        var width = extension is "gif" or "bmp" ? 1 : 2;
        var height = extension is "gif" or "bmp" ? 1 : 3;
        Assert.Contains(images, item => item.Elements.GetInteger("/Width") == width
            && item.Elements.GetInteger("/Height") == height);
        if (extension is "png" or "webp")
            Assert.Contains(images, item => item.Elements.ContainsKey("/SMask"));
    }
}
