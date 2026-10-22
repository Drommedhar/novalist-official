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

        Assert.Equal(3, captured.Count);
        Assert.Equal(3, captured.Select(item => item.ItemId).Distinct().Count());
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
        foreach (var item in captured)
            Assert.Equal(Encoding.UTF8.GetString(item.State), FileFrontMatter.Strip(await File.ReadAllTextAsync(Path.Combine(destination, item.Path), ct)));
    }

    private sealed class CapturePeer
    {
        private readonly Dictionary<Guid, string> html = [];
        public bool Closed { get; private set; }
        public Func<Task>? AfterSeed { get; init; }

        public async Task<JsonElement> RequestAsync(object parameters, CancellationToken ct)
        {
            var request = JsonSerializer.SerializeToElement(parameters);
            switch (request.GetProperty("action").GetString())
            {
                case "execute":
                    var command = request.GetProperty("command");
                    html.Add(command.GetProperty("id").GetGuid(), command.GetProperty("html").GetString()!);
                    if (AfterSeed is not null) await AfterSeed();
                    break;
                case "capture":
                    var content = html[request.GetProperty("id").GetGuid()];
                    return JsonSerializer.SerializeToElement(new SharedSceneSnapshot(Encoding.UTF8.GetBytes(content), [], content, null));
                case "close": Closed = true; break;
            }
            return JsonSerializer.SerializeToElement<object?>(null);
        }
    }
}
