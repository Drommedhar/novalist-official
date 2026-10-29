using System.IO.Compression;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Core.Models;
using Novalist.Core.Services;
using NSubstitute;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class ProviderExportTests
{
    private static readonly byte[] Image = Convert.FromHexString(
        "89504E470D0A1A0A0000000D49484452000000010000000108060000001F15C4890000000A49444154789C63000100000500010D0A2DB40000000049454E44AE426082");

    [Theory]
    [InlineData("Epub", "OEBPS/images/image-1.png")]
    [InlineData("Docx", "word/media/image-1.png")]
    public async Task BinaryExportIncludesProviderImageBytes(string format, string entry)
    {
        using var fixture = await Fixture.CreateAsync();
        var output = fixture.Temp.Combine("book.zip");

        Assert.True((await fixture.Export.RunAsync(format, output, "Novel", "Writer", false, [fixture.Chapter.Guid])).Success);

        using var zip = ZipFile.OpenRead(output);
        Assert.Equal(Image, await ReadEntryAsync(zip, entry));
        if (format == "Epub") Assert.Equal(Image, await ReadEntryAsync(zip, "OEBPS/cover.png"));
        Assert.Equal(Fixture.Prose, await fixture.Workspace.Projects.ReadSceneContentAsync(fixture.Chapter, fixture.Scene));
        Assert.Equal("Images/map.png", fixture.Workspace.Projects.ActiveBook!.CoverImage);
    }

    [Fact]
    public async Task CodexCopiesProviderImagesIntoDurableSidecarFiles()
    {
        using var fixture = await Fixture.CreateAsync();
        var entities = new EntityService(fixture.Workspace.Projects);
        var character = new CharacterData { Name = "Mira", Images = [new EntityImage { Path = "Images/map.png", Name = "Portrait" }] };
        await entities.SaveCharacterAsync(character);
        var output = fixture.Temp.Combine("codex.md");

        await fixture.Export.RunAsync("Codex", output, "Codex", "Writer", false, []);

        Assert.Contains("![Portrait](codex_images/map.png)", await File.ReadAllTextAsync(output));
        Assert.Equal(Image, await File.ReadAllBytesAsync(fixture.Temp.Combine("codex_images", "map.png")));
        Assert.Equal("Images/map.png", (await entities.LoadCharactersAsync()).Single().Images.Single().Path);
    }

    [Theory]
    [InlineData("Markdown")]
    [InlineData("LaTeX")]
    public async Task TextExportsNeverReferenceTemporaryAssetFiles(string format)
    {
        using var fixture = await Fixture.CreateAsync();
        var output = fixture.Temp.Combine("book.txt");

        await fixture.Export.RunAsync(format, output, "Novel", "Writer", false, [fixture.Chapter.Guid]);

        var text = await File.ReadAllTextAsync(output);
        Assert.Contains(fixture.AssetPath.Replace('\\', '/'), text);
        Assert.DoesNotContain("novalist-export-", text);
    }

    [Fact]
    public async Task ProviderReadFailureFailsExportInsteadOfDroppingTheImage()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Files.ReadBytesAsync(Arg.Any<string>()).Returns(Task.FromException<byte[]>(new IOException("Provider offline")));

        await Assert.ThrowsAsync<IOException>(() => fixture.Export.RunAsync("Epub", fixture.Temp.Combine("book.epub"), "Novel", "Writer", false, [fixture.Chapter.Guid]));
    }

    [Theory]
    [InlineData("Pdf")]
    [InlineData("CodexPdf")]
    public async Task PdfExportsEmbedProviderImages(string format)
    {
        using var fixture = await Fixture.CreateAsync();
        await new EntityService(fixture.Workspace.Projects).SaveCharacterAsync(new CharacterData
        {
            Name = "Mira", Images = [new EntityImage { Path = "Images/map.png", Name = "Portrait" }]
        });
        var output = fixture.Temp.Combine("book.pdf");

        await fixture.Export.RunAsync(format, output, "Novel", "Writer", false, [fixture.Chapter.Guid]);

        Assert.Contains("/Subtype /Image", System.Text.Encoding.Latin1.GetString(await File.ReadAllBytesAsync(output)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportOptionsKeepTheirOriginalAssetPathsAfterSuccessOrFailure(bool fail)
    {
        using var fixture = await Fixture.CreateAsync();
        using var assets = new ExportAssetStaging(fixture.Files);
        var options = new ExportOptions { Format = ExportFormat.Epub, CoverImagePath = fixture.AssetPath, SelectedChapterGuids = [fixture.Chapter.Guid] };
        var service = new ExportService(fixture.Workspace.Projects, materializeAsset: assets.MaterializeAsync);
        var output = fail ? fixture.Temp.Combine("absent", "book.epub") : fixture.Temp.Combine("book.epub");

        if (fail) await Assert.ThrowsAsync<DirectoryNotFoundException>(() => service.ExportAsync(options, output));
        else await service.ExportAsync(options, output);

        Assert.Equal(fixture.AssetPath, options.CoverImagePath);
    }

    private static async Task<byte[]> ReadEntryAsync(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name);
        Assert.NotNull(entry);
        await using var stream = entry.Open();
        using var result = new MemoryStream();
        await stream.CopyToAsync(result);
        return result.ToArray();
    }

    private sealed class Fixture : IDisposable
    {
        public const string Prose = "<p>A map.</p><p><img src=\"Images/map.png\" alt=\"Valley\"></p>";
        public TempDir Temp { get; } = new();
        public IFileService Files { get; } = Substitute.For<IFileService>();
        public Workspace Workspace { get; }
        public ExportRpc Export => new(Workspace);
        public string AssetPath => Path.Combine(Workspace.Projects.ActiveBookRoot!, "Images", "map.png");
        public ChapterData Chapter { get; private set; } = new();
        public SceneData Scene { get; private set; } = new();

        private Fixture()
        {
            var local = new FileService();
            Files.CombinePath(Arg.Any<string[]>()).Returns(call => Path.Combine(call.Arg<string[]>()));
            Files.GetFileName(Arg.Any<string>()).Returns(call => Path.GetFileName(call.Arg<string>()));
            Files.GetDirectoryName(Arg.Any<string>()).Returns(call => Path.GetDirectoryName(call.Arg<string>()) ?? "");
            Files.ReadTextAsync(Arg.Any<string>()).Returns(call => local.ReadTextAsync(call.Arg<string>()));
            Files.WriteTextAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(call => local.WriteTextAsync(call.ArgAt<string>(0), call.ArgAt<string>(1)));
            Files.CreateDirectoryAsync(Arg.Any<string>()).Returns(call => local.CreateDirectoryAsync(call.Arg<string>()));
            Files.DirectoryExistsAsync(Arg.Any<string>()).Returns(call => local.DirectoryExistsAsync(call.Arg<string>()));
            Files.GetFilesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()).Returns(call => local.GetFilesAsync(call.ArgAt<string>(0), call.ArgAt<string>(1), call.ArgAt<bool>(2)));
            Files.GetDirectoriesAsync(Arg.Any<string>()).Returns(call => local.GetDirectoriesAsync(call.Arg<string>()));
            Files.ExistsAsync(Arg.Any<string>()).Returns(call => IsAsset(call.Arg<string>()) ? Task.FromResult(true) : local.ExistsAsync(call.Arg<string>()));
            Files.ReadBytesAsync(Arg.Any<string>()).Returns(call => IsAsset(call.Arg<string>()) ? Task.FromResult(Image) : local.ReadBytesAsync(call.Arg<string>()));
            Workspace = new Workspace(Temp.Combine("settings"), fileService: Files);
        }

        private bool IsAsset(string path) => Workspace.Projects.ActiveBookRoot != null && Path.GetFullPath(path) == AssetPath;

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            await fixture.Workspace.Projects.CreateProjectAsync(fixture.Temp.Path, "Novel", "Book");
            fixture.Chapter = await fixture.Workspace.Projects.CreateChapterAsync("Chapter");
            fixture.Scene = await fixture.Workspace.Projects.CreateSceneAsync(fixture.Chapter.Guid, "Scene");
            await fixture.Workspace.Projects.WriteSceneContentAsync(fixture.Chapter, fixture.Scene, Prose);
            fixture.Workspace.Projects.ActiveBook!.CoverImage = "Images/map.png";
            return fixture;
        }

        public void Dispose() { Workspace.Dispose(); Temp.Dispose(); }
    }
}
