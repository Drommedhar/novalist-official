using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nerdbank.Streams;
using NSec.Cryptography;
using NSubstitute;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Core.Models;
using Novalist.Sync.Client;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class SharedSceneConstructionTests
{
    [Fact]
    public async Task DuplicateScenePathsAreRejectedBeforeOpeningDocumentsAndCanBeRetried()
    {
        await using var fixture = await ConstructionFixture.StartAsync();
        var state = fixture.Document(fixture.ScenePath, "html:<p>Original.</p>");
        var duplicate = state with { ItemId = Guid.NewGuid() };

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.AttachAsync([state, duplicate]));

        await fixture.AttachAsync([state]);
        var scene = await fixture.Rpc.InvokeAsync<SceneContentDto>("scenes/read", fixture.Chapter.Guid, fixture.Scene.Id);
        Assert.Equal(state.ItemId, scene.Shared?.ItemId);
        Assert.Equal("<p>Original.</p>", scene.Html);
    }

    [Fact]
    public async Task ACheckpointSceneOutsideBookFoldersKeepsItsWritingWithoutChangingDraftCounts()
    {
        await using var fixture = await ConstructionFixture.StartAsync();
        const string relative = "Retained/orphan.novalist";
        var path = Path.Combine(fixture.Root, "Retained", "orphan.novalist");
        Directory.CreateDirectory(Path.Combine(fixture.Root, "Retained"));
        await File.WriteAllTextAsync(path, FileFrontMatter.Stamp("<p>Archived.</p>", "orphan"), TestContext.Current.CancellationToken);
        var document = fixture.Document(relative, "html:<p>Archived.</p>");
        var count = fixture.Scene.WordCount;
        await fixture.AttachAsync([document]);

        await fixture.Rpc.InvokeAsync("collaboration/writeScene", fixture.SessionId, document.ItemId, Encoding.UTF8.GetBytes("html:<p>Retained new writing.</p>"));

        Assert.Contains("Retained new writing.", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(count, fixture.Scene.WordCount);
        Assert.Null((await fixture.Rpc.InvokeAsync<SceneContentDto>("scenes/read", fixture.Chapter.Guid, fixture.Scene.Id)).Shared);
    }

    [Fact]
    public async Task InvalidPlanningKeysAreRejectedBeforeChangingTheSceneManifest()
    {
        await using var fixture = await ConstructionFixture.StartAsync();
        var manifest = Path.Combine(fixture.Host.Workspace.Projects.ActiveDraftRoot ?? throw new InvalidOperationException("Missing draft."), "scenes.json");
        var before = await File.ReadAllBytesAsync(manifest, TestContext.Current.CancellationToken);
        var relative = Path.GetRelativePath(fixture.Root, manifest).Replace('\\', '/');
        var document = fixture.Document(relative, "text:Not a planning field") with { TextField = new("stage", SceneId: fixture.Scene.Id) };

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.AttachAsync([document]));

        Assert.Equal(before, await File.ReadAllBytesAsync(manifest, TestContext.Current.CancellationToken));
    }

    private sealed class ConstructionFixture : IAsyncDisposable
    {
        private readonly TempDir directory = new();
        private readonly DeviceSequence sequence;
        private readonly SessionJournal journal;
        private readonly Key signing = Key.Create(SignatureAlgorithm.Ed25519);
        private readonly EnvelopeCipher cipher;
        private readonly CollaborationSession session;
        internal BackendHost Host { get; }
        internal JsonRpc Rpc { get; }
        internal Guid SessionId { get; } = Guid.NewGuid();
        internal string Root => Host.Workspace.Projects.ProjectRoot ?? throw new InvalidOperationException("Missing project.");
        internal ChapterDto Chapter => Assert.Single(Host.Workspace.BuildState().Chapters);
        internal SceneDto Scene => Assert.Single(Chapter.Scenes);
        internal string ScenePath => Path.GetRelativePath(Root, Assert.Single(Directory.GetFiles(Root, "*.novalist", SearchOption.AllDirectories))).Replace('\\', '/');

        private ConstructionFixture()
        {
            Host = new BackendHost(Path.Combine(directory.Path, "settings"));
            var (backend, desktop) = FullDuplexStream.CreatePair();
            Host.Attach(backend, backend);
            var formatter = new SystemTextJsonFormatter();
            formatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            Rpc = new(new HeaderDelimitedMessageHandler(desktop, desktop, formatter));
            Rpc.AddLocalRpcTarget(new CollaborationDocumentPeer());
            Rpc.StartListening();
            var project = Guid.NewGuid();
            sequence = new(Path.Combine(directory.Path, "device.sqlite"), Guid.NewGuid());
            journal = new(Path.Combine(directory.Path, "session.sqlite"), SessionId, project, sequence);
            cipher = new(project, 0, RandomNumberGenerator.GetBytes(32));
            var transport = Substitute.For<IUpdateTransport>();
            transport.ConnectionIdentity.Returns("test:construction");
            session = new(new(project, sequence.DeviceId, 0), journal, cipher, signing, new Dictionary<Guid, PublicKey>(), transport);
        }

        internal static async Task<ConstructionFixture> StartAsync()
        {
            var fixture = new ConstructionFixture();
            try
            {
                await fixture.Rpc.InvokeAsync("project/create", fixture.directory.Path, "Shared", "Book");
                await fixture.Rpc.InvokeAsync("project/createChapter", "Opening");
                await fixture.Rpc.InvokeAsync("project/createScene", fixture.Chapter.Guid, "Arrival");
                await fixture.Rpc.InvokeAsync("scenes/write", fixture.Chapter.Guid, fixture.Scene.Id, "<p>Original.</p>", "Original.");
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        internal SharedDocumentState Document(string path, string state) => new(Guid.NewGuid(), path, Encoding.UTF8.GetBytes(state));
        internal Task AttachAsync(IReadOnlyList<SharedDocumentState> documents) => Host.Workspace.AttachSharedProjectAsync(SessionId, session, new(documents, []), TestContext.Current.CancellationToken);

        public async ValueTask DisposeAsync()
        {
            await Host.Workspace.DetachSharedScenesAsync();
            await session.DisposeAsync();
            await Host.DisposeAsync();
            Rpc.Dispose();
            cipher.Dispose();
            signing.Dispose();
            journal.Dispose();
            sequence.Dispose();
            directory.Dispose();
        }
    }
}
