using System.Text;
using System.Text.Json;
using Nerdbank.Streams;
using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Services;
using Novalist.Sync.Client;
using Novalist.Sync.Client.Tests;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Sync.IntegrationTests;

public sealed class DesktopSharingTests(CommunityServerFixture server) : IClassFixture<CommunityServerFixture>
{
    [Theory]
    [InlineData("external")]
    [InlineData("capture")]
    [InlineData("invite")]
    public async Task InterruptedSharingPreservesExternalWritingAndCanBeCancelledByShutdown(string interruption)
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(Path.GetTempPath(), "novalist-desktop-sharing", Guid.NewGuid().ToString("N"));
        await using var issuer = await TestIdentityProvider.StartAsync(ct);
        issuer.Subject = "owner-" + Guid.NewGuid();
        issuer.IssueAccessToken = () => server.Token(issuer.Subject);
        server.ConfigureDesktopSignIn(issuer.Authority);
        DesktopParticipant? owner = null;
        CommunityServerFixture.InvitationBlock? blocked = null;
        Task? creating = null;
        try
        {
            var project = new ProjectService(new FileService());
            await project.CreateProjectAsync(folder, "Shared novel", "Book");
            var chapter = await project.CreateChapterAsync("Opening");
            var scene = await project.CreateSceneAsync(chapter.Guid, "Arrival");
            await project.WriteSceneContentAsync(chapter, scene, "<p>Original writing.</p>");
            owner = new DesktopParticipant(Path.Combine(folder, "owner-settings"), issuer);
            await owner.Rpc.InvokeWithParameterObjectAsync("project/open", new { path = project.ProjectRoot }, ct);
            await owner.Rpc.InvokeWithParameterObjectAsync("collaboration/account/signIn", new { server = server.Address }, ct);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (interruption == "capture") owner.Peer.CaptureEntered = entered;
            else if (interruption == "external") owner.Peer.OnClose = () => File.WriteAllText(project.GetSceneFilePath(chapter, scene),
                Novalist.Core.Models.FileFrontMatter.Stamp("<p>External writing during upload.</p>", scene.Id));
            creating = owner.Rpc.InvokeWithParameterObjectAsync("collaboration/create", new { server = server.Address }, ct);
            if (interruption == "invite")
            {
                await creating;
                blocked = await server.HoldInvitationsAsync(ct);
                creating = owner.Rpc.InvokeWithParameterObjectAsync("collaboration/invite", new { expiresAt = DateTimeOffset.UtcNow.AddHours(1) }, ct);
                await blocked.WaitForBlockedRequestAsync(ct);
                entered.TrySetResult();
            }
            if (interruption != "external")
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
                await owner.Rpc.InvokeAsync("system/shutdown");
                await owner.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct);
                owner = null;
                await Assert.ThrowsAnyAsync<Exception>(() => creating);
            }
            else
            {
                await Assert.ThrowsAsync<RemoteInvocationException>(() => creating);
                Assert.Equal("<p>External writing during upload.</p>", await project.ReadSceneContentAsync(chapter, scene));
                Assert.Null(await owner.Rpc.InvokeAsync<HostedSessionDto?>("collaboration/status"));
            }
        }
        finally
        {
            if (blocked is not null) await blocked.DisposeAsync();
            if (owner is not null)
            {
                owner.Peer.UnblockCapture.TrySetResult();
                if (creating is not null) await creating.ContinueWith(_ => { }, TaskScheduler.Default);
                await owner.DisposeAsync();
            }
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task DesktopCreatesAnInvitationAndAnOfflineOwnersGuestJoinsAUsableLocalProject()
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(Path.GetTempPath(), "novalist-desktop-sharing", Guid.NewGuid().ToString("N"));
        await using var issuer = await TestIdentityProvider.StartAsync(ct);
        issuer.Subject = "owner-" + Guid.NewGuid();
        issuer.IssueAccessToken = () => server.Token(issuer.Subject);
        server.ConfigureDesktopSignIn(issuer.Authority);
        try
        {
            var project = new ProjectService(new FileService());
            await project.CreateProjectAsync(folder, "Shared novel", "Book");
            var chapter = await project.CreateChapterAsync("Opening");
            var scene = await project.CreateSceneAsync(chapter.Guid, "Arrival");
            await project.WriteSceneContentAsync(chapter, scene, "<p>Original writing.</p>");
            await using var owner = new DesktopParticipant(Path.Combine(folder, "owner-settings"), issuer);
            await owner.Rpc.InvokeWithParameterObjectAsync("project/open", new { path = project.ProjectRoot }, ct);
            await owner.Rpc.InvokeWithParameterObjectAsync("collaboration/account/signIn", new { server = server.Address }, ct);

            var sharing = await owner.Rpc.InvokeWithParameterObjectAsync<JsonElement>("collaboration/create", new { server = server.Address }, ct);
            var ownerRead = await owner.Rpc.InvokeWithParameterObjectAsync<SceneContentDto>("scenes/read", new { chapterGuid = chapter.Guid, sceneId = scene.Id }, ct);
            Assert.Equal(sharing.GetProperty("sessionId").GetGuid(), ownerRead.Shared!.SessionId);
            var link = await owner.Rpc.InvokeWithParameterObjectAsync<string>("collaboration/invite", new { expiresAt = DateTimeOffset.UtcNow.AddHours(1) }, ct);
            await Assert.ThrowsAsync<RemoteInvocationException>(() => owner.Rpc.InvokeWithParameterObjectAsync(
                "collaboration/account/signOut", new { server = server.Address }, ct));
            await owner.Rpc.InvokeAsync("collaboration/end");

            issuer.Subject = "guest-" + Guid.NewGuid();
            await using var guest = new DesktopParticipant(Path.Combine(folder, "guest-settings"), issuer);
            await guest.Rpc.InvokeWithParameterObjectAsync("collaboration/account/signIn", new { server = server.Address }, ct);
            var destination = Path.Combine(folder, "Joined novel");
            var joined = await guest.Rpc.InvokeWithParameterObjectAsync<JsonElement>("collaboration/join", new { server = server.Address, link, destination }, ct);
            var read = await guest.Rpc.InvokeWithParameterObjectAsync<SceneContentDto>("scenes/read", new { chapterGuid = chapter.Guid, sceneId = scene.Id }, ct);
            Assert.Equal("<p>Original writing.</p>", read.Html);
            Assert.Equal(ownerRead.Shared.ItemId, read.Shared!.ItemId);
            Assert.Equal(joined.GetProperty("sessionId").GetGuid(), read.Shared.SessionId);
            await guest.Rpc.InvokeWithParameterObjectAsync("collaboration/writeScene",
                new { sessionId = read.Shared.SessionId, itemId = read.Shared.ItemId, update = Encoding.UTF8.GetBytes("<p>Guest writing.</p>") }, ct);
            await guest.Rpc.InvokeAsync("collaboration/end");
            var retained = new ProjectService(new FileService());
            await retained.LoadProjectAsync(destination);
            Assert.Equal("<p>Guest writing.</p>", await retained.ReadSceneContentAsync(chapter, scene));
            Assert.Null((await guest.Rpc.InvokeWithParameterObjectAsync<SceneContentDto>("scenes/read", new { chapterGuid = chapter.Guid, sceneId = scene.Id }, ct)).Shared);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    private sealed class DesktopParticipant : IAsyncDisposable
    {
        private readonly BackendHost host;
        public JsonRpc Rpc { get; }
        public DesktopPeer Peer { get; }

        public DesktopParticipant(string settings, TestIdentityProvider issuer)
        {
            host = new(settings);
            var (backend, desktop) = FullDuplexStream.CreatePair();
            host.Attach(backend, backend);
            var formatter = new SystemTextJsonFormatter();
            formatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            Rpc = new(new HeaderDelimitedMessageHandler(desktop, desktop, formatter));
            Peer = new DesktopPeer(issuer);
            Rpc.AddLocalRpcTarget(Peer);
            Rpc.StartListening();
        }

        public async ValueTask DisposeAsync() { await host.DisposeAsync(); Rpc.Dispose(); }
    }

    private sealed class DesktopPeer(TestIdentityProvider issuer)
    {
        private readonly Dictionary<string, string> credentials = [];
        private readonly Dictionary<Guid, byte[]> documents = [];
        public Action? OnClose { get; set; }
        public TaskCompletionSource? CaptureEntered { get; set; }
        public TaskCompletionSource UnblockCapture { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        [JsonRpcMethod("collaboration/openBrowser")]
        public Task OpenBrowserAsync(Uri url, CancellationToken cancellationToken) => issuer.OpenBrowserAsync(url, cancellationToken);

        [JsonRpcMethod("collaboration/credentials")]
        public string? Credentials(string action, string key, string? value = null)
        {
            if (action == "read") return credentials.GetValueOrDefault(key);
            if (action == "write") credentials[key] = value ?? throw new ArgumentException("Missing credential.");
            else if (action == "remove") credentials.Remove(key);
            else throw new ArgumentException("Unknown credential operation.");
            return null;
        }

        [JsonRpcMethod("collaboration/documents")]
        public async Task<object?> Documents(Guid sessionId, string action, JsonElement command = default, Guid? id = null, CancellationToken cancellationToken = default)
        {
            if (action == "close") OnClose?.Invoke();
            if (action == "capture")
            {
                if (CaptureEntered is { } entered)
                {
                    entered.TrySetResult();
                    await UnblockCapture.Task.WaitAsync(cancellationToken);
                }
                var state = documents[id!.Value];
                return new SharedSceneSnapshot(state, [], Encoding.UTF8.GetString(state), null);
            }
            if (action != "execute") return null;
            var kind = command.GetProperty("kind").GetString();
            var item = command.GetProperty("id").GetGuid();
            if (kind == "seed") documents[item] = Encoding.UTF8.GetBytes(command.GetProperty("html").GetString()!);
            else if (kind != "validate") documents[item] = command.GetProperty("state").GetBytesFromBase64();
            return kind == "receive" ? new SceneApplyResult(true, []) : null;
        }
    }
}
