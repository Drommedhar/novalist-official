using System.Text;
using System.Text.Json;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Core.Services;
using Novalist.Core.Models;
using Novalist.Sync.Client;
using NSubstitute;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class PeerProjectRpcTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public async Task DesktopPeerSessionsTransferAProjectPublishPresenceAndRetainLocalCopiesWhenEnded(string address)
    {
        var ct = TestContext.Current.CancellationToken;
        using var temporary = new TempDir();
        await using var owner = new Desktop(temporary.Combine("owner-settings"));
        await using var guest = new Desktop(temporary.Combine("guest-settings"));
        await owner.Workspace.Projects.CreateProjectAsync(temporary.Path, "Novel", "Book");
        var chapter = await owner.Workspace.Projects.CreateChapterAsync("Opening");
        var scene = await owner.Workspace.Projects.CreateSceneAsync(chapter.Guid, "Arrival");
        await owner.Workspace.Projects.WriteSceneContentAsync(chapter, scene, "<p>Shared writing.</p>");
        var originalDraft = owner.Workspace.Projects.ActiveBook!.ActiveDraftId!;
        var otherDraft = await owner.Workspace.Projects.CreateDraftAsync("Another draft", originalDraft);
        var status = await owner.Rpc.HostPeerAsync(address, 0, "Owner", ct);
        Assert.True(status.IsOwner);
        Assert.Equal("tcp", status.Mode);
        Assert.Equal(status.ProjectId, owner.Rpc.Status()?.ProjectId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.Rpc.HostPeerAsync("127.0.0.1", 0, "Owner", ct));
        var invitation = await owner.Rpc.InviteAsync(DateTimeOffset.UtcNow.AddHours(1), ct);
        var joined = await guest.Rpc.JoinPeerAsync(invitation, temporary.Combine("Guest copy"), "Guest", ct);
        Assert.False(joined.IsOwner);
        Assert.Equal(status.ProjectId, joined.ProjectId);
        Assert.Equal("<p>Shared writing.</p>", FileFrontMatter.Strip(await guest.Workspace.Projects.ReadSceneContentAsync(chapter, scene)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => guest.Rpc.InviteAsync(DateTimeOffset.UtcNow.AddHours(1), ct));
        var item = (await new ScenesRpc(owner.Workspace).ReadAsync(chapter.Guid, scene.Id)).Shared!.ItemId;
        var view = Guid.NewGuid();
        await owner.Rpc.PublishPresenceAsync(status.SessionId, item, view, new([1], [2]), true, ct);
        await EventuallyAsync(() => guest.Rpc.Participants(joined.SessionId).Count == 1, ct);
        Assert.Equal("Owner", Assert.Single(guest.Rpc.Participants(joined.SessionId)).Name);
        Assert.Equal(scene.Id, Assert.Single(guest.Rpc.Participants(joined.SessionId)).SceneId);
        await guest.Workspace.Projects.SwitchDraftAsync(otherDraft.Id);
        Assert.Null(Assert.Single(guest.Rpc.Participants(joined.SessionId)).SceneId);
        await guest.Workspace.Projects.SwitchDraftAsync(originalDraft);
        Assert.Equal(scene.Id, Assert.Single(guest.Rpc.Participants(joined.SessionId)).SceneId);
        await owner.Rpc.PublishPresenceAsync(status.SessionId, item, view, null, false, ct);
        await EventuallyAsync(() => guest.Rpc.Participants(joined.SessionId).Count == 0, ct);
        await owner.Rpc.PublishPresenceAsync(status.SessionId, Guid.NewGuid(), view, null, true, ct);
        await EventuallyAsync(() => guest.Rpc.Participants(joined.SessionId).Count == 1, ct);
        Assert.Null(Assert.Single(guest.Rpc.Participants(joined.SessionId)).SceneId);
        Assert.Throws<InvalidOperationException>(() => guest.Rpc.Participants(Guid.NewGuid()));
        await guest.Rpc.EndAsync();
        await owner.Rpc.EndAsync();
        Assert.Null(owner.Rpc.Status());
        Assert.Null(guest.Rpc.Status());
        Assert.Equal("<p>Shared writing.</p>", FileFrontMatter.Strip(await guest.Workspace.Projects.ReadSceneContentAsync(chapter, scene)));
        await guest.Rpc.CreateIndependentCopyAsync(temporary.Combine("Independent"), ct);
        var independent = new ProjectService(new FileService());
        Assert.NotEqual(status.ProjectId.ToString(), (await independent.LoadProjectAsync(temporary.Combine("Independent"))).Id);
        await owner.Rpc.EndAsync();
    }

    [Fact]
    public async Task FailedPeerSetupLeavesTheOpenProjectUsableAndAllowsAnotherAttempt()
    {
        var ct = TestContext.Current.CancellationToken;
        using var temporary = new TempDir();
        await using var owner = new Desktop(temporary.Combine("settings"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.Rpc.HostPeerAsync("127.0.0.1", 0, "Owner", ct));
        await owner.Workspace.Projects.CreateProjectAsync(temporary.Path, "Novel", "Book");
        await Assert.ThrowsAsync<ArgumentException>(() => owner.Rpc.HostPeerAsync("127.0.0.1", 0, "", ct));
        await Assert.ThrowsAsync<ArgumentException>(() => owner.Rpc.HostPeerAsync("not an address", 0, "Owner", ct));
        await Assert.ThrowsAsync<ArgumentException>(() => owner.Rpc.HostPeerAsync("127.0.0.1", -1, "Owner", ct));
        var createDocuments = owner.Workspace.CreateSharedDocuments;
        owner.Workspace.CreateSharedDocuments = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.Rpc.HostPeerAsync("127.0.0.1", 0, "Owner", ct));
        Assert.Null(owner.Rpc.Status());
        owner.Workspace.CreateSharedDocuments = createDocuments;
        var chapter = await owner.Workspace.Projects.CreateChapterAsync("Opening");
        var scene = await owner.Workspace.Projects.CreateSceneAsync(chapter.Guid, "Arrival");
        await owner.Workspace.Projects.WriteSceneContentAsync(chapter, scene, "<p>Retained writing.</p>");
        owner.FailNextLoad = true;
        await Assert.ThrowsAsync<IOException>(() => owner.Rpc.HostPeerAsync("127.0.0.1", 0, "Owner", ct));
        Assert.Null(owner.Rpc.Status());
        var status = await owner.Rpc.HostPeerAsync("127.0.0.1", 0, "Owner", ct);
        var link = await owner.Rpc.InviteAsync(DateTimeOffset.UtcNow.AddHours(1), ct);
        await using var guest = new Desktop(temporary.Combine("guest-settings"));
        await Assert.ThrowsAsync<IOException>(() => guest.Rpc.JoinPeerAsync(link, temporary.Path, "Guest", ct));
        var withoutCheckpoint = PeerInvitation.Parse(link) with { Checkpoint = null };
        await Assert.ThrowsAsync<InvalidDataException>(() => guest.Rpc.JoinPeerAsync(withoutCheckpoint.ToLink(), temporary.Combine("No checkpoint"), "Guest", ct));
        var wrongPin = PeerInvitation.Parse(link) with { Address = PeerInvitation.Parse(link).Address with { CertificateHash = new byte[32] } };
        await Assert.ThrowsAnyAsync<Exception>(() => guest.Rpc.JoinPeerAsync(wrongPin.ToLink(), temporary.Combine("Wrong host"), "Guest", ct));
        Assert.Null(guest.Rpc.Status());
        Assert.False(Directory.Exists(temporary.Combine("Wrong host")));
        Assert.Equal(status.ProjectId, (await guest.Rpc.JoinPeerAsync(link, temporary.Combine("Guest"), "Guest", ct)).ProjectId);
    }

    [Fact]
    public async Task DefaultDesktopExplainsSteamIsUnavailableWithoutCreatingASharedSession()
    {
        var ct = TestContext.Current.CancellationToken;
        using var temporary = new TempDir();
        await using var desktop = new Desktop(temporary.Combine("settings"));
        await desktop.Workspace.Projects.CreateProjectAsync(temporary.Path, "Novel", "Book");
        Assert.False(desktop.Rpc.SteamStatus().Available);
        await Assert.ThrowsAsync<NotSupportedException>(() => desktop.Rpc.HostSteamAsync("Writer", ct));
        Assert.Null(desktop.Rpc.Status());
        Assert.NotNull(desktop.Workspace.Projects.ProjectRoot);
        Assert.Equal("tcp", (await desktop.Rpc.HostPeerAsync("127.0.0.1", 0, "Writer", ct)).Mode);
        var link = await desktop.Rpc.InviteAsync(DateTimeOffset.UtcNow.AddHours(1), ct);
        var steamLink = new SteamPeerInvitation(76561198000000001, PeerInvitation.Parse(link)).ToLink();
        await using var guest = new Desktop(temporary.Combine("guest-settings"));
        await Assert.ThrowsAsync<NotSupportedException>(() => guest.Rpc.JoinSteamAsync(steamLink, temporary.Combine("Guest"), "Guest", ct));
        Assert.Null(guest.Rpc.Status());
        Assert.False(Directory.Exists(temporary.Combine("Guest")));
        Assert.Equal("tcp", (await guest.Rpc.JoinPeerAsync(link, temporary.Combine("Guest"), "Guest", ct)).Mode);
    }

    private static async Task EventuallyAsync(Func<bool> condition, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private sealed class Desktop : IAsyncDisposable
    {
        private readonly Dictionary<Guid, string> html = [];
        internal Workspace Workspace { get; }
        internal CollaborationRpc Rpc { get; }
        internal bool FailNextLoad { get; set; }

        internal Desktop(string settings)
        {
            Workspace = new(settings);
            var credentials = Substitute.For<ICollaborationCredentials>();
            credentials.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));
            Workspace.PeerProjects = new(Workspace, credentials);
            Workspace.CreateSharedDocuments = id => new(id, SceneDocumentsAsync);
            Rpc = new(Workspace);
        }

        private Task<JsonElement> SceneDocumentsAsync(object parameters, CancellationToken ct)
        {
            var request = JsonSerializer.SerializeToElement(parameters);
            if (request.GetProperty("action").GetString() == "capture")
            {
                var content = html[request.GetProperty("id").GetGuid()];
                return Task.FromResult(JsonSerializer.SerializeToElement(new SharedSceneSnapshot(Encoding.UTF8.GetBytes(content), [], content, null)));
            }
            if (request.GetProperty("action").GetString() == "execute")
            {
                var command = request.GetProperty("command");
                var id = command.GetProperty("id").GetGuid();
                switch (command.GetProperty("kind").GetString())
                {
                    case "seed": html[id] = command.GetProperty("html").GetString() ?? ""; break;
                    case "load":
                        if (FailNextLoad) { FailNextLoad = false; throw new IOException("Document host failed to load the scene."); }
                        html[id] = Encoding.UTF8.GetString(command.GetProperty("state").GetBytesFromBase64());
                        break;
                    case "receive": return Task.FromResult(JsonSerializer.SerializeToElement(new SceneApplyResult(true, [])));
                }
            }
            return Task.FromResult(JsonSerializer.SerializeToElement<object?>(null));
        }

        public async ValueTask DisposeAsync()
        {
            await Rpc.EndAsync();
            Workspace.PeerProjects?.Dispose();
            Workspace.Dispose();
        }
    }
}
