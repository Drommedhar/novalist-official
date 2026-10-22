using System.Text;
using System.Text.Json;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sync.Client;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class SharedProjectCaptureTests
{
    [Fact]
    public async Task SharingResearchBindsNoteAndLinkProseByStableIdentityWithoutBindingAssetPaths()
    {
        var ct = TestContext.Current.CancellationToken;
        using var temporary = new TempDir();
        using var workspace = new Workspace(Path.Combine(temporary.Path, "settings"));
        await workspace.Projects.CreateProjectAsync(temporary.Path, "Novel", "Book");
        var research = new Novalist.Backend.Rpc.LibraryRpc(workspace);
        await research.SaveResearchAsync(null, "Notes", "Note", "**Exact** research prose.", []);
        await research.SaveResearchAsync(null, "Source", "Link", "https://example.org", []);
        await research.SaveResearchAsync(null, "Diagram", "Image", "Research/diagram.png", []);
        workspace.CreateSharedDocuments = id => new(id, new CapturePeer().RequestAsync);
        using var archive = new MemoryStream();
        var captured = await workspace.CaptureSharedProjectAsync(archive, ct);
        Assert.Equal(2, captured.Documents.Count);
        var notes = research.ListResearch().Where(item => item.Type != "Image").ToArray();
        foreach (var note in notes)
        {
            var document = Assert.Single(captured.Documents, item => item.TextField?.ResearchItemId == note.Id);
            Assert.Equal(".novalist/project.json", document.Path);
            Assert.Equal(note.Content, Encoding.UTF8.GetString(document.State));
        }
        archive.Position = 0;
        var installed = await ProjectCheckpoint.InstallAsync(archive, Path.Combine(temporary.Path, "Joined"), 1024 * 1024, ct);
        Assert.Equal(captured.Documents.Select(item => item.TextField), installed.Documents.Select(item => item.TextField));
    }

    [Fact]
    public async Task SharingPreservesMarkdownAndGivesLegacySectionsStableIdentities()
    {
        var ct = TestContext.Current.CancellationToken;
        using var temporary = new TempDir();
        using var workspace = new Workspace(Path.Combine(temporary.Path, "settings"));
        await workspace.Projects.CreateProjectAsync(temporary.Path, "Novel", "Book");
        var entity = new LocationData { Id = "place", Name = "Place", Description = "**Markdown** <tag>\n\nExact text.", Sections = [new() { Title = "History", Content = "Long ago." }] };
        await new EntityService(workspace.Projects).SaveLocationAsync(entity);
        var root = workspace.Projects.ProjectRoot ?? throw new InvalidOperationException();
        var relative = ProjectContentSelection.Read(root).Single(path => path.EndsWith("/place.json", StringComparison.Ordinal));
        var file = Path.Combine(root, relative);
        var legacy = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(file, ct))!;
        legacy["sections"]![0]!.AsObject().Remove("id");
        await File.WriteAllTextAsync(file, legacy.ToJsonString(), ct);
        workspace.CreateSharedDocuments = id => new(id, new CapturePeer().RequestAsync);
        using var archive = new MemoryStream();
        var captured = await workspace.CaptureSharedProjectAsync(archive, ct);
        var description = Assert.Single(captured.Documents, item => item.TextField?.Key == "description");
        Assert.Equal(entity.Description, Encoding.UTF8.GetString(description.State));
        var section = Assert.Single(captured.Documents, item => item.TextField?.Key == "content");
        var sectionId = section.TextField?.SectionId;
        Assert.False(string.IsNullOrEmpty(sectionId));
        using var nextArchive = new MemoryStream();
        var recaptured = await workspace.CaptureSharedProjectAsync(nextArchive, ct);
        Assert.Equal(sectionId, Assert.Single(recaptured.Documents, item => item.TextField?.Key == "content").TextField?.SectionId);
        archive.Position = 0;
        var destination = Path.Combine(temporary.Path, "Joined");
        var installed = await ProjectCheckpoint.InstallAsync(archive, destination, 1024 * 1024, ct);
        Assert.Equal(captured.Documents.Select(item => item.TextField), installed.Documents.Select(item => item.TextField));
        using var retained = JsonDocument.Parse(await File.ReadAllTextAsync(file, ct));
        Assert.Equal(entity.Description, retained.RootElement.GetProperty("description").GetString());
    }

    [Fact]
    public async Task ASceneAddedDuringCaptureRequiresRetryInsteadOfPublishingAnIncompleteSharedProject()
    {
        var ct = TestContext.Current.CancellationToken;
        using var temporary = new TempDir();
        using var workspace = new Workspace(Path.Combine(temporary.Path, "settings"));
        await workspace.Projects.CreateProjectAsync(temporary.Path, "Novel", "Book");
        var chapter = await workspace.Projects.CreateChapterAsync("Opening");
        var scene = await workspace.Projects.CreateSceneAsync(chapter.Guid, "Arrival");
        await workspace.Projects.WriteSceneContentAsync(chapter, scene, "<p>Existing writing.</p>");
        var peer = new CapturePeer { AfterSeed = async () => { await workspace.Projects.CreateSceneAsync(chapter.Guid, "Late addition"); } };
        workspace.CreateSharedDocuments = id => new(id, peer.RequestAsync);
        using var archive = new MemoryStream();

        await Assert.ThrowsAsync<IOException>(() => workspace.CaptureSharedProjectAsync(archive, ct));

        Assert.True(peer.Closed);
    }

    [Fact]
    public async Task InitialSharingCapturesEveryBookAndDraftWithoutChangingTheOpenDraft()
    {
        var ct = TestContext.Current.CancellationToken;
        using var temporary = new TempDir();
        using var workspace = new Workspace(Path.Combine(temporary.Path, "settings"));
        await workspace.Projects.CreateProjectAsync(temporary.Path, "Novel", "First book");
        var first = workspace.Projects.ActiveBook!;
        var chapter = await workspace.Projects.CreateChapterAsync("Opening");
        var scene = await workspace.Projects.CreateSceneAsync(chapter.Guid, "Arrival");
        await workspace.Projects.WriteSceneContentAsync(chapter, scene, "<p>First draft.</p>");
        await workspace.Projects.CreateDraftAsync("Revision", first.ActiveDraftId);
        var second = await workspace.Projects.CreateBookAsync("Second book");
        await workspace.Projects.SwitchBookAsync(second.Id);
        var otherChapter = await workspace.Projects.CreateChapterAsync("Later");
        var otherScene = await workspace.Projects.CreateSceneAsync(otherChapter.Guid, "Return");
        await workspace.Projects.WriteSceneContentAsync(otherChapter, otherScene, "<p>Second book.</p>");
        await workspace.Projects.SwitchBookAsync(first.Id);
        if (OperatingSystem.IsWindows()) first.ChapterFolder = first.ChapterFolder.ToLowerInvariant();
        var research = Path.Combine(workspace.Projects.ProjectRoot!, "Research");
        Directory.CreateDirectory(research);
        await File.WriteAllTextAsync(Path.Combine(research, "old-manuscript.novalist"), "An opaque research attachment.", ct);
        var peer = new CapturePeer();
        workspace.CreateSharedDocuments = id => new(id, peer.RequestAsync);
        using var archive = new MemoryStream();

        var captured = (await workspace.CaptureSharedProjectAsync(archive, ct)).Documents;

        Assert.Equal(3, captured.Count(item => item.TextField is null));
        Assert.Equal(12, captured.Count(item => item.TextField?.SceneId is not null));
        Assert.Equal(15, captured.Select(item => item.ItemId).Distinct().Count());
        Assert.Equal(2, captured.Count(item => Encoding.UTF8.GetString(item.State) == "<p>First draft.</p>"));
        Assert.Contains(captured, item => Encoding.UTF8.GetString(item.State) == "<p>Second book.</p>");
        Assert.Equal(first.Id, workspace.Projects.ActiveBook!.Id);
        Assert.Equal("draft-default", workspace.Projects.ActiveBook.ActiveDraftId);
        Assert.True(peer.Closed);
        archive.Position = 0;
        var destination = Path.Combine(temporary.Path, "Joined copy");
        var installed = await ProjectCheckpoint.InstallAsync(archive, destination, 1024 * 1024, ct);
        Assert.Equal(captured.Select(item => item.ItemId), installed.Documents.Select(item => item.ItemId));
        var localCopy = new ProjectService(new FileService());
        Assert.Equal(2, (await localCopy.LoadProjectAsync(destination)).Books.Count);
        Assert.Equal("An opaque research attachment.", await File.ReadAllTextAsync(Path.Combine(destination, "Research/old-manuscript.novalist"), ct));
        foreach (var item in captured.Where(item => item.TextField is null))
            Assert.Equal(Encoding.UTF8.GetString(item.State), FileFrontMatter.Strip(await File.ReadAllTextAsync(Path.Combine(destination, item.Path), ct)));
    }

    private sealed class CapturePeer
    {
        private readonly Dictionary<Guid, string> html = [];
        private readonly HashSet<Guid> plain = [];
        public bool Closed { get; private set; }
        public Func<Task>? AfterSeed { get; init; }

        public async Task<JsonElement> RequestAsync(object parameters, CancellationToken ct)
        {
            var request = JsonSerializer.SerializeToElement(parameters);
            switch (request.GetProperty("action").GetString())
            {
                case "execute":
                    var command = request.GetProperty("command");
                    var id = command.GetProperty("id").GetGuid();
                    var isText = command.GetProperty("kind").GetString() == "seedText";
                    if (isText) plain.Add(id);
                    html.Add(id, command.GetProperty(isText ? "text" : "html").GetString()!);
                    if (AfterSeed is not null) await AfterSeed();
                    break;
                case "capture":
                    var content = html[request.GetProperty("id").GetGuid()];
                    return JsonSerializer.SerializeToElement(plain.Contains(request.GetProperty("id").GetGuid())
                        ? new SharedSceneSnapshot(content.Length == 0 ? [0] : Encoding.UTF8.GetBytes(content), [], null, null) { Text = content }
                        : new SharedSceneSnapshot(Encoding.UTF8.GetBytes(content), [], content, null));
                case "close": Closed = true; break;
            }
            return JsonSerializer.SerializeToElement<object?>(null);
        }
    }
}
