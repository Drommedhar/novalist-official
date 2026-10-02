using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public sealed class ImportImageTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FileService _files = new();
    private readonly ProjectService _projects;
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jvIoAAAAASUVORK5CYII=");
    public ImportImageTests() => _projects = new(_files);
    public void Dispose() => _dir.Dispose();
    private StructuredImportService Service() => new(_projects, _files, "Source writing");
    private async Task<StructuredImportService> OpenAsync()
    {
        await _projects.CreateProjectAsync(_dir.Path, "Novel", "Book");
        return Service();
    }
    private static StructuredImportEntry Entry(string target, params ImportImageReference[] images)
        => new(target, JsonSerializer.SerializeToElement(new { novalistImport = 1, target, data = new { name = "Entry", images }, content = "Preserved biography." }));

    [Fact]
    public async Task UploadsArePortableDeduplicatedAndReusableAfterRestartAndInFolderImports()
    {
        var service = await OpenAsync();
        _projects.ActiveBook!.ImageFolder = "Pictures";
        await _projects.SaveProjectAsync();
        var upload = await service.UploadImageAsync(Png, " IMAGE/PNG; charset=binary ");
        Assert.Equal(64, upload.ImageId.Length);
        Assert.Equal(Png.Length, upload.Bytes);
        Assert.Equal("image/png", upload.ContentType);
        Assert.False(upload.Reused);
        Assert.Equal(32 * 1024 * 1024, StructuredImportService.MaxImageBytes);
        Assert.Equal(100, StructuredImportService.MaxImagesPerEntry);
        Assert.Equal(6, StructuredImportService.ImageContentTypes.Count);
        var relative = "Pictures/Imported/" + upload.ImageId + ".png";
        var saved = Path.Combine(_projects.ActiveBookRoot!, relative);
        Assert.Equal(Png, await _files.ReadBytesAsync(saved));
        Assert.True((await service.UploadImageAsync(Png, "image/png")).Reused);
        await _projects.LoadProjectAsync(_projects.ProjectRoot!);
        service = Service();
        Assert.True((await service.UploadImageAsync(Png, "image/png")).Reused);
        var reference = new ImportImageReference(upload.ImageId, "Portrait", "A portrait from the source.");
        var entry = Entry("character", reference, reference with { Name = "Duplicate" });
        Assert.Equal("valid", Assert.Single(await service.ImportAsync([entry], true)).Status);
        var entities = new EntityService(_projects);
        Assert.Empty(await entities.LoadCharactersAsync());
        Assert.Equal("imported", Assert.Single(await service.ImportAsync([entry])).Status);
        var character = Assert.Single(await entities.LoadCharactersAsync());
        var image = Assert.Single(character.Images);
        Assert.Equal("Portrait", image.Name);
        Assert.Equal(reference.Alt, image.Alt);
        Assert.Equal(relative, image.Path);
        Assert.Equal("Preserved biography.", Assert.Single(character.Sections).Content);
        Assert.Equal("skipped", Assert.Single(await Service().ImportAsync([entry])).Status);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(saved)!));

        var source = Path.Combine(_dir.Path, "source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "entry.json"), Entry("location", reference).Document.GetRawText());
        var folder = new FolderImportService(_projects, _files);
        folder.Scan(source);
        folder.Configure("location", [], "Source writing");
        Assert.Equal(1, (await folder.ImportBatchAsync()).Imported);
        Assert.Equal(relative, Assert.Single(Assert.Single(await entities.LoadLocationsAsync()).Images).Path);
    }

    public static IEnumerable<object[]> Formats()
    {
        yield return ["image/png", Png, ".png"];
        yield return ["image/jpeg", new byte[] { 255, 216, 255, 255, 217 }, ".jpg"];
        yield return ["image/gif", Encoding.ASCII.GetBytes("GIF89a123456789"), ".gif"];
        yield return ["image/webp", Encoding.ASCII.GetBytes("RIFF1234WEBPVP8 1234x"), ".webp"];
        yield return ["image/bmp", Encoding.ASCII.GetBytes("BM" + new string('x', 24)), ".bmp"];
        yield return ["image/svg+xml", Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\"><path d=\"M0 0\"/></svg>"), ".svg"];
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task SupportedFormatsAreVerifiedStoredAndResolvedWithTheirRealExtension(string mime, byte[] bytes, string extension)
    {
        var service = await OpenAsync();
        var upload = await service.UploadImageAsync(bytes, mime);
        var entry = Entry("character", new ImportImageReference(upload.ImageId, "Image"));
        Assert.Equal("imported", Assert.Single(await service.ImportAsync([entry])).Status);
        var image = Assert.Single(Assert.Single(await new EntityService(_projects).LoadCharactersAsync()).Images);
        Assert.EndsWith(extension, image.Path);
        Assert.Equal(string.Empty, image.Alt);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_projects.ActiveBookRoot!, image.Path)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UploadImageAsync(Encoding.UTF8.GetBytes("not an image"), mime));
    }

    [Fact]
    public async Task BadOrOversizedImagesAndUnsafeSvgAreRejectedWithoutCreatingAssets()
    {
        var service = await OpenAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => service.UploadImageAsync([], "image/png"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UploadImageAsync(new byte[StructuredImportService.MaxImageBytes + 1], "image/png"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UploadImageAsync(Png, "application/octet-stream"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UploadImageAsync(new byte[24], "image/png"));
        foreach (var svg in new[] { "", "<!-- empty -->", "<html/>", "<svg/>", "<svg xmlns=\"http://www.w3.org/2000/svg\"><path></svg>", "<!DOCTYPE svg [<!ENTITY x SYSTEM 'file:///secret'>]><svg xmlns=\"http://www.w3.org/2000/svg\">&x;</svg>" })
            await Assert.ThrowsAsync<ArgumentException>(() => service.UploadImageAsync(Encoding.UTF8.GetBytes(svg), "image/svg+xml"));
        Assert.Empty(Directory.GetFiles(Path.Combine(_projects.ActiveBookRoot!, _projects.ActiveBook!.ImageFolder), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task FailedUploadsCleanPartialFilesAndAllowTheSameBytesToBeRetried()
    {
        await OpenAsync();
        var proxy = Substitute.For<IFileService>();
        proxy.ExistsAsync(Arg.Any<string>()).Returns(call => _files.ExistsAsync(call.Arg<string>()));
        proxy.WriteBytesAsync(Arg.Any<string>(), Arg.Any<byte[]>()).Returns(call => _files.WriteBytesAsync(call.ArgAt<string>(0), call.ArgAt<byte[]>(1)));
        proxy.DeleteFileAsync(Arg.Any<string>()).Returns(call => _files.DeleteFileAsync(call.Arg<string>()));
        proxy.MoveFileAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.FromException(new IOException()));
        var store = new ImportImageStore(_projects, proxy);
        await Assert.ThrowsAsync<IOException>(() => store.UploadAsync(Png, "image/png"));
        await proxy.Received(1).DeleteFileAsync(Arg.Is<string>(path => path.EndsWith(".tmp")));
        Assert.Empty(Directory.GetFiles(Path.Combine(_projects.ActiveBookRoot!, _projects.ActiveBook!.ImageFolder), "*", SearchOption.AllDirectories));
        Assert.False((await Service().UploadImageAsync(Png, "image/png")).Reused);
    }

    [Fact]
    public async Task SchemasAndValidationRejectMissingAssetsBadReferencesAndExcessiveArraysBeforeImporting()
    {
        var service = await OpenAsync();
        var reference = new ImportImageReference(new string('0', 64), "Portrait");
        foreach (var validate in new[] { true, false })
        {
            var result = Assert.Single(await service.ImportAsync([Entry("character", reference)], validate));
            Assert.Equal("invalid", result.Status);
            Assert.Contains("POST /v1/images", result.Error);
        }
        var store = new ImportImageStore(_projects, _files);
        await Assert.ThrowsAsync<ArgumentException>(() => store.ResolveAsync(Enumerable.Repeat(reference, 101).ToArray()));
        foreach (var invalid in new[] { null!, reference with { ImageId = "../../other.png" }, reference with { ImageId = new string('A', 64) }, reference with { ImageId = reference.ImageId + "\n" }, reference with { Name = " " }, reference with { Name = null! } })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => store.ResolveAsync([invalid]));
            Assert.Equal("invalid", Assert.Single(await service.ImportAsync([Entry("character", invalid)])).Status);
        }
        Assert.Equal("invalid", Assert.Single(await service.ImportAsync([Entry("character", Enumerable.Repeat(reference, 101).ToArray())])).Status);
        var schema = JsonNode.Parse(service.Schema().ExportTarget("character"))!;
        Assert.Equal(100, schema["properties"]!["data"]!["properties"]!["images"]!["maxItems"]!.GetValue<int>());
        foreach (var target in new[] { "scene", "research" })
            Assert.Null(JsonNode.Parse(service.Schema().ExportTarget(target))!["properties"]!["data"]!["properties"]!["images"]);
        Assert.Empty(await new EntityService(_projects).LoadCharactersAsync());
    }

    [Fact]
    public async Task PicturesCanBeAddedToEveryCodexTypeWithoutReplacingExistingFieldsOrCaptions()
    {
        var service = await OpenAsync();
        _projects.CurrentProject!.CustomEntityTypes.Add(new() { TypeKey = "faction", FolderName = "Factions" });
        var uploaded = await service.UploadImageAsync(Png, "image/png");
        var reference = new ImportImageReference(uploaded.ImageId, "Portrait", "Original caption");
        var entities = new EntityService(_projects);
        foreach (var target in new[] { "character", "location", "item", "lore", "faction" })
        {
            var result = Assert.Single(await service.ImportAsync([Entry(target)]));
            var folder = target switch { "character" => _projects.ActiveBook!.CharacterFolder, "location" => _projects.ActiveBook!.LocationFolder,
                "item" => _projects.ActiveBook!.ItemFolder, "lore" => _projects.ActiveBook!.LoreFolder, _ => "Factions" };
            var path = Path.Combine(_projects.ActiveBookRoot!, folder, result.Id + ".json");
            var before = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            var attached = await service.AttachImagesAsync(target, result.Id!, [reference, reference]);
            Assert.Equal(new ImportImageAttachment(target, result.Id!, 1, 1), attached);
            var after = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            before.AsObject().Remove("images");
            after.AsObject().Remove("images");
            Assert.True(JsonNode.DeepEquals(before, after));
            var repeated = await Service().AttachImagesAsync(target, result.Id!, [reference with { Name = "New name", Alt = "Changed" }]);
            Assert.Equal(0, repeated.Added);
            Assert.Equal(1, repeated.Total);
            Assert.Contains("Original caption", await File.ReadAllTextAsync(path));
        }
        var character = Assert.Single(await entities.LoadCharactersAsync());
        character.Surname = "User edit";
        character.CustomProperties!["user"] = "Kept";
        await entities.SaveCharacterAsync(character);
        var second = await service.UploadImageAsync(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"), "image/svg+xml");
        Assert.Equal(1, (await service.AttachImagesAsync("character", character.Id, [new(second.ImageId, "Second")])).Added);
        var saved = Assert.Single(await entities.LoadCharactersAsync());
        Assert.Equal("User edit", saved.Surname);
        Assert.Equal("Kept", saved.CustomProperties!["user"]);
        Assert.Equal(2, saved.Images.Count);
        saved.Locked = true;
        await entities.SaveCharacterAsync(saved);
        await Assert.ThrowsAsync<ImportEntryLockedException>(() => service.AttachImagesAsync("character", character.Id, [reference]));
    }

    [Fact]
    public async Task AttachmentErrorsAndChangedDestinationsCannotModifyAnotherEntryOrBook()
    {
        var service = await OpenAsync();
        var id = Guid.NewGuid().ToString();
        foreach (var target in new[] { "unknown", "scene", "research" })
            await Assert.ThrowsAsync<ArgumentException>(() => service.AttachImagesAsync(target, id, []));
        await Assert.ThrowsAsync<ArgumentException>(() => service.AttachImagesAsync("character", "../other", []));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.AttachImagesAsync("character", id, []));
        var path = Path.Combine(_projects.ActiveBookRoot!, _projects.ActiveBook!.CharacterFolder, id + ".json");
        foreach (var json in new[] { "null", JsonSerializer.Serialize(new CharacterData()) })
        {
            await File.WriteAllTextAsync(path, json);
            await Assert.ThrowsAsync<InvalidDataException>(() => service.AttachImagesAsync("character", id, []));
        }
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new CharacterData { Id = id }));
        await Assert.ThrowsAsync<ImportImageNotFoundException>(() => service.AttachImagesAsync("character", id, [new(new string('0', 64), "Missing")]));
        var other = await _projects.CreateBookAsync("Other");
        await _projects.SwitchBookAsync(other.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UploadImageAsync(Png, "image/png"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AttachImagesAsync("character", id, []));
    }
}
