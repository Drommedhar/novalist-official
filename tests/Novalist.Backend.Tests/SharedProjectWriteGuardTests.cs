using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nerdbank.Streams;
using NSubstitute;
using Novalist.Backend.Extensions;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Core.Services;
using Novalist.Sync.Client;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class SharedProjectWriteGuardTests
{
    [Fact]
    public async Task ADisconnectedWriterCannotCreateStructureThatAppearsAfterReconnecting()
    {
        await using var fixture = await SharedProjectFixture.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        await fixture.Wire.DisconnectAsync(ct);

        await Assert.ThrowsAsync<RemoteInvocationException>(() => fixture.Desktop.InvokeAsync("project/createChapter", "Offline chapter"));
        await fixture.Updates.SynchronizeAsync(createProject: false, ct);
        var project = await fixture.Desktop.InvokeAsync<ProjectStateDto>("project/getState");

        Assert.DoesNotContain(project.Chapters, chapter => chapter.Title == "Offline chapter");
        Assert.True(await fixture.Desktop.InvokeAsync<bool>("system/barrier"));
    }

    [Fact]
    public async Task OnlineStructuralChangesArePublishedThroughTheSession()
    {
        await using var fixture = await SharedProjectFixture.StartAsync();
        await fixture.Desktop.InvokeAsync("project/createChapter", "New chapter");
        var state = await fixture.Desktop.InvokeAsync<ProjectStateDto>("project/getState");
        Assert.Contains(state.Chapters, chapter => chapter.Title == "New chapter");
        var committed = await fixture.ReadCommittedFilesAsync();
        Assert.Contains(committed.Values, bytes => Encoding.UTF8.GetString(bytes).Contains("New chapter", StringComparison.Ordinal));
        Assert.True(await fixture.Desktop.InvokeAsync<bool>("system/barrier"));
    }

    [Fact]
    public async Task DisconnectedProseRemainsWritableAndOrdinaryWritesStayBlocked()
    {
        await using var fixture = await SharedProjectFixture.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        await fixture.Wire.DisconnectAsync(ct);
        await fixture.Desktop.InvokeAsync("collaboration/writeScene", fixture.SessionId, fixture.Scene.ItemId, Encoding.UTF8.GetBytes("<p>Offline prose survives.</p>"));
        var html = await File.ReadAllTextAsync(Path.Combine(fixture.Root, fixture.Scene.Path), ct);
        Assert.Contains("Offline prose survives.", html);
        await Assert.ThrowsAsync<RemoteInvocationException>(() => fixture.Desktop.InvokeAsync("project/createChapter", "Blocked"));
        await fixture.Updates.SynchronizeAsync(false, ct);
        var document = await fixture.Desktop.InvokeAsync<byte[]>("collaboration/readScene", fixture.SessionId, fixture.Scene.ItemId);
        Assert.Contains("Offline prose survives.", Encoding.UTF8.GetString(document));
    }

    [Fact]
    public async Task UntrackedDiskEditsAreRecoveredAndAfterRequestErrorsDoNotBreakRpc()
    {
        await using var fixture = await SharedProjectFixture.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(fixture.Root, fixture.Scene.Path);
        var original = await File.ReadAllBytesAsync(path, ct);
        const string external = "Outside writing must not disappear";
        await File.WriteAllTextAsync(path, external, ct);

        var error = await Assert.ThrowsAsync<RemoteInvocationException>(() => fixture.Desktop.InvokeAsync<bool>("system/barrier"));
        Assert.Contains("Recovered edits", error.Message);
        Assert.Equal(original, await File.ReadAllBytesAsync(path, ct));
        var recovery = Directory.GetFiles(Path.Combine(fixture.Root, "Recovered edits"), "*", SearchOption.AllDirectories);
        Assert.Contains(recovery, file => File.ReadAllText(file) == external);
        Assert.True(await fixture.Desktop.InvokeAsync<bool>("system/barrier"));
    }

    [Fact]
    public async Task OrdinaryEntityUpdatesCannotBypassFocusButLockedUpdatesStillWork()
    {
        await using var fixture = await SharedProjectFixture.StartAsync();
        await Assert.ThrowsAsync<RemoteInvocationException>(() => fixture.Desktop.InvokeAsync("entities/update", "character", fixture.CharacterId, new { age = "42" }));
        await Assert.ThrowsAsync<RemoteInvocationException>(() => fixture.Desktop.InvokeAsync("entities/update", "character", fixture.CharacterId, new { distinguishingFeatures = "Unsafe full prose replacement" }));
        var entry = await fixture.Desktop.InvokeAsync<JsonElement>("entities/get", "character", fixture.CharacterId);
        Assert.NotEqual("42", entry.GetProperty("age").GetString());
        var field = fixture.State.Fields.Single(binding => binding.Path.EndsWith(fixture.CharacterId + ".json", StringComparison.Ordinal) && binding.Key.Equals("age", StringComparison.OrdinalIgnoreCase));
        var focus = Guid.NewGuid();
        await fixture.Desktop.InvokeAsync<SharedFieldFocus>("collaboration/acquireField", fixture.SessionId, field.ItemId, focus);
        await fixture.Desktop.InvokeAsync("collaboration/writeField", fixture.SessionId, field.ItemId, focus, "42");
        await fixture.Desktop.InvokeAsync("collaboration/releaseField", fixture.SessionId, field.ItemId, focus);
        entry = await fixture.Desktop.InvokeAsync<JsonElement>("entities/get", "character", fixture.CharacterId);
        Assert.Equal("42", entry.GetProperty("age").GetString());
    }

    [Fact]
    public async Task ARefusedProjectCommitPreservesLocalWorkWithoutPublishingItLater()
    {
        await using var fixture = await SharedProjectFixture.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        await using var observer = new SharedProjectFiles(fixture.Updates, (_, _) => Task.CompletedTask);
        var context = fixture.Updates.Context;
        var lease = new FieldLeaseRequest(context.ProjectId, observer.ItemId, context.Epoch, Guid.NewGuid());
        Assert.True((await fixture.Updates.AcquireFieldAsync(lease, ct)).Acquired);
        await Assert.ThrowsAsync<RemoteInvocationException>(() => fixture.Desktop.InvokeAsync("project/createChapter", "Refused chapter"));
        await fixture.Updates.ReleaseFieldAsync(lease, ct);
        var state = await fixture.Desktop.InvokeAsync<ProjectStateDto>("project/getState");
        Assert.DoesNotContain(state.Chapters, chapter => chapter.Title == "Refused chapter");
        var committed = await fixture.ReadCommittedFilesAsync();
        Assert.DoesNotContain(committed.Values, bytes => Encoding.UTF8.GetString(bytes).Contains("Refused chapter", StringComparison.Ordinal));
        Assert.True(Directory.Exists(Path.Combine(fixture.Root, "Recovered edits")));
    }

    [Fact]
    public async Task ProseProjectionPreservesAnOutsideEditBeforeReplacingTheFile()
    {
        await using var fixture = await SharedProjectFixture.StartAsync();
        var path = Path.Combine(fixture.Root, fixture.Scene.Path);
        await File.WriteAllTextAsync(path, "External draft", TestContext.Current.CancellationToken);
        await fixture.Desktop.InvokeAsync("collaboration/writeScene", fixture.SessionId, fixture.Scene.ItemId, Encoding.UTF8.GetBytes("<p>Collaborative draft</p>"));
        Assert.Contains("Collaborative draft", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        var recovery = Directory.GetFiles(Path.Combine(fixture.Root, "Recovered edits"), "*", SearchOption.AllDirectories);
        Assert.Contains(recovery, file => File.ReadAllText(file) == "External draft");
    }

    [Fact]
    public async Task PersonalSettingsStillSaveOfflineAndUncoordinatedSharedWritesFail()
    {
        await using var fixture = await SharedProjectFixture.StartAsync();
        var path = Path.Combine(fixture.Root, fixture.Scene.Path);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Host.Workspace.FileService.WriteTextAsync(path, "Background overwrite"));
        await fixture.Desktop.InvokeAsync<bool>("system/barrier");
        await fixture.Wire.DisconnectAsync(TestContext.Current.CancellationToken);
        var settings = await fixture.Desktop.InvokeAsync<JsonElement>("settings/updateProject", new { editorFontSize = 29 });
        Assert.Equal(29, settings.GetProperty("effective").GetProperty("editorFontSize").GetDouble());
        await Assert.ThrowsAsync<RemoteInvocationException>(() => fixture.Desktop.InvokeAsync<bool>("backup/restore", "unused"));
        Assert.Contains("Original prose", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisconnectedWritersCanNavigatePreviouslyOpenedBooks()
    {
        await using var fixture = await SharedProjectFixture.StartAsync();
        var project = await fixture.Desktop.InvokeAsync<ProjectStateDto>("project/createBook", "Second book");
        var first = project.ActiveBookId;
        var second = project.Books.Single(book => book.Name == "Second book");
        await fixture.Desktop.InvokeAsync<ProjectStateDto>("project/switchBook", second.Id);
        await fixture.Desktop.InvokeAsync<ProjectStateDto>("project/switchBook", first);
        await fixture.Wire.DisconnectAsync(TestContext.Current.CancellationToken);
        project = await fixture.Desktop.InvokeAsync<ProjectStateDto>("project/switchBook", second.Id);
        Assert.Equal(second.Id, project.ActiveBookId);
        project = await fixture.Desktop.InvokeAsync<ProjectStateDto>("project/switchBook", first);
        Assert.Single(project.Chapters);
    }

    private sealed class SharedProjectFixture : IAsyncDisposable
    {
        private readonly TempDir directory = new();
        private readonly List<IDisposable> resources = [];
        private readonly List<IAsyncDisposable> asynchronous = [];
        private BackendHost? backendHost;
        private JsonRpc? desktopRpc;
        private TcpPeerTransport? wire;
        private CollaborationSession? updates;
        internal BackendHost Host => backendHost ?? throw new InvalidOperationException("Fixture not started.");
        internal JsonRpc Desktop => desktopRpc ?? throw new InvalidOperationException("Fixture not started.");
        internal TcpPeerTransport Wire => wire ?? throw new InvalidOperationException("Fixture not started.");
        internal CollaborationSession Updates => updates ?? throw new InvalidOperationException("Fixture not started.");
        internal string Root => Host.Workspace.Projects.ProjectRoot ?? throw new InvalidOperationException("Project not open.");
        internal SharedProjectState State { get; private set; } = new([], []);
        private IReadOnlyDictionary<string, byte[]> initialFiles = new Dictionary<string, byte[]>();
        internal SharedDocumentState Scene => Assert.Single(State.Documents, document => document.TextField is null);
        internal string CharacterId { get; private set; } = "";
        internal Guid SessionId { get; } = Guid.NewGuid();

        internal static async Task<SharedProjectFixture> StartAsync()
        {
            var fixture = new SharedProjectFixture();
            try { await fixture.InitializeAsync(); return fixture; }
            catch { await fixture.DisposeAsync(); throw; }
        }

        private async Task InitializeAsync()
        {
            var ct = TestContext.Current.CancellationToken;
            backendHost = new BackendHost(Path.Combine(directory.Path, "settings"));
            var (backend, desktop) = FullDuplexStream.CreatePair();
            Host.Attach(backend, backend);
            var formatter = new SystemTextJsonFormatter();
            formatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            desktopRpc = new JsonRpc(new HeaderDelimitedMessageHandler(desktop, desktop, formatter));
            Desktop.AddLocalRpcTarget(new SceneDocumentPeer());
            Desktop.StartListening();
            await Desktop.InvokeAsync("project/create", directory.Path, "Shared", "Book");
            var chapter = Assert.Single((await Desktop.InvokeAsync<ProjectStateDto>("project/createChapter", "Chapter")).Chapters);
            var created = await Desktop.InvokeAsync<ProjectStateDto>("project/createScene", chapter.Guid, "Scene");
            var scene = Assert.Single(Assert.Single(created.Chapters).Scenes);
            await Desktop.InvokeAsync("scenes/write", chapter.Guid, scene.Id, "<p>Original prose.</p>", "Original prose.");
            var character = await Desktop.InvokeAsync<JsonElement>("entities/create", "character", "Mira");
            CharacterId = character.GetProperty("id").GetString() ?? throw new InvalidOperationException("Missing character identity.");
            var credentials = Substitute.For<ICollaborationCredentials>();
            credentials.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));
            var device = await new DeviceIdentityStore(credentials).GetOrCreateAsync(new("https://guard.test"), "writer", ct);
            resources.Add(device);
            var peer = await TcpPeerHost.StartAsync(new(IPAddress.Loopback, 0), Path.Combine(directory.Path, "peer"),
                new(Guid.NewGuid(), 0, RandomNumberGenerator.GetBytes(32), device), ct);
            asynchronous.Add(peer);
            var access = peer.CreateOwnerAccess("127.0.0.1");
            resources.Add(access);
            wire = new TcpPeerTransport(access, device);
            asynchronous.Add(Wire);
            var sequence = new DeviceSequence(Path.Combine(directory.Path, "device.sqlite"), device.Id);
            resources.Add(sequence);
            var journal = new SessionJournal(Path.Combine(directory.Path, "session.sqlite"), SessionId, access.ProjectId, sequence);
            resources.Add(journal);
            var cipher = access.CreateCipher();
            resources.Add(cipher);
            updates = new(new(access.ProjectId, device.Id, access.Epoch), journal, cipher, access.CreateSessionIdentity(device), Wire);
            asynchronous.Add(Updates);
            await Updates.SynchronizeAsync(createProject: true, ct);
            using var archive = new MemoryStream();
            State = await Host.Workspace.CaptureSharedProjectAsync(archive, ct);
            archive.Position = 0;
            initialFiles = await ProjectCheckpoint.ReadFilesAsync(archive, long.MaxValue, ct);
            await Host.Workspace.AttachSharedProjectAsync(SessionId, Updates, State, archive, ct);
        }

        internal async Task<IReadOnlyDictionary<string, byte[]>> ReadCommittedFilesAsync()
        {
            var ct = TestContext.Current.CancellationToken;
            await using var observer = new SharedProjectFiles(Updates, (_, _) => Task.CompletedTask);
            await observer.StartAsync(initialFiles, State, ct);
            return await observer.SnapshotAsync(ct);
        }

        public async ValueTask DisposeAsync()
        {
            if (backendHost is not null)
            {
                if (updates is not null) await Updates.SynchronizeAsync(createProject: false, CancellationToken.None);
                await Host.Workspace.DetachSharedScenesAsync();
                await Host.DisposeAsync();
            }
            desktopRpc?.Dispose();
            foreach (var resource in asynchronous.AsEnumerable().Reverse()) await resource.DisposeAsync();
            foreach (var resource in resources.AsEnumerable().Reverse()) resource.Dispose();
            directory.Dispose();
        }
    }

    private sealed class SceneDocumentPeer
    {
        private readonly Dictionary<Guid, byte[]> states = [];

        [JsonRpcMethod("collaboration/documents")]
        public object? Documents(string action, JsonElement command = default, Guid? id = null)
        {
            if (action == "execute")
            {
                var item = command.GetProperty("id").GetGuid();
                var kind = command.GetProperty("kind").GetString();
                if (kind == "seed") states[item] = Encoding.UTF8.GetBytes(command.GetProperty("html").GetString() ?? "");
                if (kind == "seedText") states[item] = Encoding.UTF8.GetBytes("text:" + command.GetProperty("text").GetString());
                if (kind == "load") states[item] = command.GetProperty("state").GetBytesFromBase64();
                if (kind == "receive")
                {
                    states[item] = command.GetProperty("state").GetBytesFromBase64();
                    return new SceneApplyResult(true, []);
                }
            }
            if (action != "capture" || id is null) return null;
            var state = states[id.Value];
            var value = Encoding.UTF8.GetString(state);
            return value.StartsWith("text:", StringComparison.Ordinal)
                ? new SharedSceneSnapshot(state, [], null, null) { Text = value[5..] }
                : new SharedSceneSnapshot(state, [], value, null);
        }
    }
}
