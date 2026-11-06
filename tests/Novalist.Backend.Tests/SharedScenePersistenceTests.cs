using System.Security.Cryptography;
using System.Text.Json;
using Nerdbank.Streams;
using NSec.Cryptography;
using NSubstitute;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Sync.Client;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class SharedScenePersistenceTests
{
    [Theory]
    [InlineData("project/close")]
    [InlineData("project/open")]
    [InlineData("project/create")]
    [InlineData("collaboration/end")]
    public async Task SharedEditorRpcPersistsLocalCopyAndClosesSessionBeforeLeavingProject(string transition)
    {
        using var temporary = new TempDir();
        using var host = new BackendHost(Path.Combine(temporary.Path, "settings"));
        var workspace = host.Workspace;
        await workspace.Projects.CreateProjectAsync(temporary.Path, "Shared novel", "Book");
        await workspace.OpenProjectAsync(workspace.Projects.ProjectRoot!);
        var chapter = await workspace.Projects.CreateChapterAsync("Chapter");
        var scene = await workspace.Projects.CreateSceneAsync(chapter.Guid, "Scene");
        var (backendStream, desktopStream) = FullDuplexStream.CreatePair();
        host.Attach(backendStream, backendStream);
        var formatter = new SystemTextJsonFormatter();
        formatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        using var desktop = new JsonRpc(new HeaderDelimitedMessageHandler(desktopStream, desktopStream, formatter));
        var peer = new DocumentPeer();
        desktop.AddLocalRpcTarget(peer);
        desktop.StartListening();
        var project = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var item = Guid.NewGuid();
        using var sequence = new DeviceSequence(Path.Combine(temporary.Path, "device.sqlite"), Guid.NewGuid());
        using var journal = new SessionJournal(Path.Combine(temporary.Path, "session.sqlite"), sessionId, project, sequence);
        using var signing = Key.Create(SignatureAlgorithm.Ed25519);
        using var cipher = new EnvelopeCipher(project, 0, RandomNumberGenerator.GetBytes(32));
        var transport = Substitute.For<IUpdateTransport>();
        transport.ConnectionIdentity.Returns("test:desktop");
        await using var session = new CollaborationSession(new(project, sequence.DeviceId, 0), journal, cipher, signing,
            new Dictionary<Guid, PublicKey>(), transport);
        var path = workspace.Projects.GetSceneFilePath(chapter, scene);
        var originalRoot = workspace.Projects.ProjectRoot!;
        var relative = Path.GetRelativePath(originalRoot, path).Replace('\\', '/');
        var ct = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(path, Novalist.Core.Models.FileFrontMatter.Stamp("<p>1</p>", scene.Id), ct);
        await workspace.AttachSharedScenesAsync(sessionId, session, [new(item, relative, [1])], ct);
        var today = DateOnly.FromDateTime(DateTime.Now);
        Assert.Equal(0, workspace.WordHistory.TotalForDay(today));
        try
        {
            var read = await desktop.InvokeWithParameterObjectAsync<SceneContentDto>("scenes/read", new { chapterGuid = chapter.Guid, sceneId = scene.Id }, ct);
            Assert.Equal("<p>1</p>", read.Html);
            Assert.Equal(sessionId, read.Shared!.SessionId);
            Assert.Equal(item, read.Shared.ItemId);
            Assert.Equal(new byte[] { 1 }, read.Shared.State);
            var manuscript = await desktop.InvokeAsync<JsonElement[]>("manuscript/get", "All");
            var shared = Assert.Single(manuscript).GetProperty("scenes")[0].GetProperty("shared");
            Assert.Equal(item, shared.GetProperty("itemId").GetGuid());
            Assert.Equal(sessionId, shared.GetProperty("sessionId").GetGuid());
            Assert.Equal(read.Shared.State, shared.GetProperty("state").GetBytesFromBase64());
            await desktop.InvokeWithParameterObjectAsync("collaboration/writeScene", new { sessionId, itemId = item, update = new byte[] { 2 } }, ct);
            Assert.Equal(new byte[] { 2 }, Assert.Single(session.ReadItem(item)));
            Assert.Contains("<p>2</p>", await File.ReadAllTextAsync(path, ct));
            Assert.Contains(scene.Id, await File.ReadAllTextAsync(path, ct));
            Assert.Equal(8, scene.WordCount);
            Assert.Equal(7, workspace.WordHistory.TotalForDay(today));
            var notification = await peer.Updated.Task.WaitAsync(ct);
            Assert.Equal(item, notification.ItemId);
            Assert.Equal(new byte[] { 2 }, notification.Update);
            Assert.Equal(new byte[] { 2 }, await desktop.InvokeWithParameterObjectAsync<byte[]>("collaboration/readScene", new { sessionId, itemId = item }, ct));
            await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.InvokeWithParameterObjectAsync("collaboration/writeScene",
                new { sessionId = Guid.NewGuid(), itemId = item, update = new byte[] { 3 } }, ct));
            await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.InvokeWithParameterObjectAsync("scenes/write",
                new { chapterGuid = chapter.Guid, sceneId = scene.Id, html = "<p>Stale.</p>", plainText = "Stale." }, ct));
            await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.InvokeWithParameterObjectAsync("scenes/resolveConflict",
                new { chapterGuid = chapter.Guid, sceneId = scene.Id, html = "<p>Stale.</p>", plainText = "Stale." }, ct));
            await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.WriteSceneAsync(chapter.Guid, scene.Id, "Stale", "Stale"));
            session.Save(item, [3]);
            var otherRoot = await CreateOtherProjectAsync(temporary.Path);
            object parameters = transition switch
            {
                "project/open" => new { path = otherRoot },
                "project/create" => new { parentDirectory = temporary.Path, projectName = "Another novel", firstBookName = "Book" },
                _ => new { }
            };
            await desktop.InvokeWithParameterObjectAsync(transition, parameters, ct);
            Assert.True(journal.IsClosed);
            Assert.Contains("<p>3</p>", await File.ReadAllTextAsync(path, ct));
            Assert.Throws<InvalidOperationException>(() => session.Save(item, [4]));
            await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.InvokeWithParameterObjectAsync("collaboration/readScene", new { sessionId, itemId = item }, ct));
        }
        finally { await workspace.DetachSharedScenesAsync(); }
        await workspace.OpenProjectAsync(originalRoot);
        await workspace.WordHistory.LoadAsync();
        await new ScenesRpc(workspace).WriteAsync(chapter.Guid, scene.Id, "<p>Local copy.</p>", "Local copy.");
        Assert.Null((await new ScenesRpc(workspace).ReadAsync(chapter.Guid, scene.Id)).Shared);
        Assert.Equal(8, workspace.WordHistory.TotalForDay(today));
    }

    private static async Task<string> CreateOtherProjectAsync(string directory)
    {
        using var workspace = new Workspace(Path.Combine(directory, "other-settings"));
        await workspace.Projects.CreateProjectAsync(directory, "Next novel", "Book");
        return workspace.Projects.ProjectRoot!;
    }

    public sealed record UpdateNotice(Guid SessionId, Guid ItemId, byte[] Update);

    public sealed class DocumentPeer
    {
        private readonly Dictionary<Guid, byte[]> states = [];
        public TaskCompletionSource<UpdateNotice> Updated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        [JsonRpcMethod("collaboration/sceneUpdated")]
        public void SceneUpdated(UpdateNotice notice) => Updated.TrySetResult(notice);

        [JsonRpcMethod("collaboration/documents")]
        public object? Documents(Guid sessionId, string action, JsonElement command = default, Guid? id = null)
        {
            if (action == "execute" && command.GetProperty("kind").GetString() != "validate")
                states[command.GetProperty("id").GetGuid()] = command.GetProperty("state").GetBytesFromBase64();
            if (action != "capture") return action == "execute" && command.GetProperty("kind").GetString() == "receive" ? new SceneApplyResult(true, []) : null;
            var state = states[id!.Value];
            var html = state[0] == 2 ? "<p>2</p><p>other<br>words</p><h2>more</h2><ul><li>last</li></ul><div>one<div>two</div>three</div>" : $"<p>{state[0]}</p>";
            return new SharedSceneSnapshot(state, [], html, null);
        }
    }
}
