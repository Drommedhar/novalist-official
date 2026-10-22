using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class CheckpointImportTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "novalist-import-tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(".novalist/project.json", "{\"books\":[{\"folderName\":\"../outside\"}]}")]
    [InlineData(".novalist/project.json", "{\"books\":[{\"FolderName\":\"Book\",\"ChapterFolder\":\"../../../outside\"}]}")]
    [InlineData(".novalist/project.json", "{\"books\":[{\"folderName\":\"Book\",\"drafts\":[{\"folderName\":\"../../outside\"}]}]}")]
    [InlineData("Book/Drafts/default/draft.json", "{\"chapters\":[{\"folderName\":\"../../../../outside\"}]}")]
    [InlineData("Book/Drafts/default/scenes.json", "{\"archived\":[{\"fileName\":\"../../../../../outside/sentinel.txt\"}]}")]
    [InlineData("Book/Characters/person.json", "{\"id\":\"../../outside/sentinel\"}")]
    [InlineData("Book/Characters/scenes.json", "{\"id\":\"../../outside/sentinel\"}")]
    [InlineData("Book/Drafts/default/Maps/scenes.json", "{\"id\":\"../../outside/sentinel\"}")]
    [InlineData("Book/Characters/person.json", "{\"images\":[{\"path\":\"C:/private/key.png\"}]}")]
    [InlineData("Book/Drafts/default/Chapters/Opening/scene.novalist", "<!--nv v=1 id=../../outside/sentinel -->\n<p>Story.</p>")]
    [InlineData("Book/Drafts/default/Chapters/Opening/.nvchapter.json", "{\"guid\":\"../../outside\"}")]
    [InlineData(".novalist/project.json", "{\"researchItems\":[{\"type\":2,\"content\":\"../outside/sentinel.txt\"}]}")]
    public async Task UntrustedStorageReferencesNeverPublishOrTouchOutsideFiles(string path, string content)
    {
        var ct = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(Path.Combine(root, "outside"));
        var sentinel = Path.Combine(root, "outside/sentinel.txt");
        await File.WriteAllTextAsync(sentinel, "Private original", ct);
        var files = new Dictionary<string, string>
        {
            [".novalist/project.json"] = """{"books":[{"folderName":"Book","drafts":[{"folderName":"default"}]}]}""",
            [path] = content
        };
        using var archive = await ArchiveAsync(files, ct);
        var destination = Path.Combine(root, "joined");

        await Assert.ThrowsAsync<InvalidDataException>(() => ProjectCheckpoint.InstallAsync(archive, destination, 1024 * 1024, ct));

        Assert.False(Directory.Exists(destination));
        Assert.Equal("Private original", await File.ReadAllTextAsync(sentinel, ct));
        Assert.Single(Directory.GetFileSystemEntries(Path.Combine(root, "outside")));
        Assert.Empty(Directory.GetDirectories(root, ".novalist-join-*"));
    }

    [Theory]
    [InlineData("{\"books\":[{\"folderName\":\"Book\"}]}")]
    [InlineData("{\"books\":[{\"folderName\":\"Book\",\"drafts\":[]}]}")]
    public async Task ImplicitLegacyDraftsAreValidatedBeforeMigration(string metadata)
    {
        var ct = TestContext.Current.CancellationToken;
        using var archive = await ArchiveAsync(new()
        {
            [".novalist/project.json"] = metadata,
            ["Book/Drafts/default/scenes.json"] = """{"archived":[{"fileName":"../../../../../outside/sentinel.txt"}]}"""
        }, ct);
        var destination = Path.Combine(root, "joined");
        await Assert.ThrowsAsync<InvalidDataException>(() => ProjectCheckpoint.InstallAsync(archive, destination, 1024 * 1024, ct));
        Assert.False(Directory.Exists(destination));
    }

    [Theory]
    [InlineData(" File ")]
    [InlineData(" 2 ")]
    [InlineData("02")]
    [InlineData("+2")]
    [InlineData("File,Image")]
    public async Task AllAcceptedResearchEnumSpellingsProtectFileReferences(string type)
    {
        var ct = TestContext.Current.CancellationToken;
        using var archive = await ArchiveAsync(new()
        {
            [".novalist/project.json"] = JsonSerializer.Serialize(new { researchItems = new[] { new { type, content = "../outside/private" } } })
        }, ct);
        await Assert.ThrowsAsync<InvalidDataException>(() => ProjectCheckpoint.InstallAsync(archive, Path.Combine(root, "joined"), 1024 * 1024, ct));
    }

    [Theory]
    [InlineData("fields")]
    [InlineData("properties")]
    [InlineData("customProperties")]
    public async Task SceneBucketsCannotMasqueradeAsCustomProseDictionaries(string guid)
    {
        var ct = TestContext.Current.CancellationToken;
        using var archive = await ArchiveAsync(new()
        {
            [".novalist/project.json"] = """{"books":[{"folderName":"Book"}]}""",
            ["Book/Drafts/default/draft.json"] = JsonSerializer.Serialize(new { chapters = new[] { new { guid, folderName = "Chapter" } } }),
            ["Book/Drafts/default/scenes.json"] = JsonSerializer.Serialize(new { chapters = new Dictionary<string, object>
                { [guid] = new[] { new { fileName = "../../../../../outside/private" } } } })
        }, ct);
        await Assert.ThrowsAsync<InvalidDataException>(() => ProjectCheckpoint.InstallAsync(archive, Path.Combine(root, "joined"), 1024 * 1024, ct));
    }

    [Fact]
    public async Task CustomFieldValuesAreProseAndResearchAttachmentsRemainOpaque()
    {
        var ct = TestContext.Current.CancellationToken;
        const string content = """{"id":"person","customProperties":{"id":"A/B","path":"north: unknown"},"fields":{"path":"../a metaphor"}}""";
        using var archive = await ArchiveAsync(new()
        {
            [".novalist/project.json"] = """{"books":[{"folderName":"Book"}]}""",
            ["Book/Characters/person.json"] = content,
            ["Research/source.novalist"] = "<!--nv v=1 id=../arbitrary -->"
        }, ct);
        var destination = Path.Combine(root, "joined");
        await ProjectCheckpoint.InstallAsync(archive, destination, 1024 * 1024, ct);
        Assert.Equal(content, await File.ReadAllTextAsync(Path.Combine(destination, "Book/Characters/person.json"), ct));
    }

    [Fact]
    public async Task IncomingSettingsCannotRedirectServicesUsingTheGuestsCredentials()
    {
        var ct = TestContext.Current.CancellationToken;
        using var archive = await ArchiveAsync(new()
        {
            [".novalist/project.json"] = "{}",
            [".novalist/settings.json"] = """{"overrides":{"grammarCheckEnabled":true,"grammarCheckApiUrl":"https://attacker.example","bookFontSize":11}}""",
            ["Research/draft.json"] = "An opaque research attachment, not project metadata."
        }, ct);
        var destination = Path.Combine(root, "joined");

        await ProjectCheckpoint.InstallAsync(archive, destination, 1024 * 1024, ct);

        var settings = await File.ReadAllTextAsync(Path.Combine(destination, ".novalist/settings.json"), ct);
        Assert.DoesNotContain("grammarCheck", settings);
        Assert.Contains("bookFontSize", settings);
        Assert.Equal("An opaque research attachment, not project metadata.", await File.ReadAllTextAsync(Path.Combine(destination, "Research/draft.json"), ct));
    }

    private static async Task<MemoryStream> ArchiveAsync(Dictionary<string, string> files, CancellationToken ct)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifest = new List<CheckpointFile>();
            foreach (var (path, content) in files)
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                manifest.Add(new(path, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes))));
                await using var entry = zip.CreateEntry("files/" + path).Open();
                await entry.WriteAsync(bytes, ct);
            }
            await using var metadata = zip.CreateEntry("manifest.json").Open();
            await JsonSerializer.SerializeAsync(metadata, new CheckpointManifest(1, manifest), cancellationToken: ct);
        }
        stream.Position = 0;
        return stream;
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}
