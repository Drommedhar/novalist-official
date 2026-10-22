using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Sync.Client;
using Novalist.Sync.Client.Tests;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class SharedRenameRecoveryTests
{
    [Theory]
    [InlineData("ack", false)]
    [InlineData("references", false)]
    [InlineData("prose", false)]
    [InlineData("ack", true)]
    [InlineData("references", true)]
    [InlineData("prose", true)]
    [InlineData("references", true, true)]
    [InlineData("references", true, true, true)]
    public async Task ConfirmedRenameFinishesAfterRetryOrActorRestart(string failure, bool restart, bool competingWriter = false, bool rotate = false)
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        identity.IssueAccessToken = () => identity.Subject;
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        var settings = Path.Combine(directory.Path, "settings");
        var credentials = new Dictionary<string, string>();
        var owner = new HostedDesktop(settings, identity, credentials);
        await using var guest = new HostedDesktop(Path.Combine(directory.Path, "guest"), identity);
        try
        {
            var project = await owner.Rpc.InvokeAsync<ProjectStateDto>("project/create", directory.Path, "Shared", "Book");
            var character = await owner.Rpc.InvokeAsync<JsonElement>("entities/create", "character", "Mira");
            var characterId = character.GetProperty("id").GetString();
            var location = await owner.Rpc.InvokeAsync<JsonElement>("entities/create", "location", "Harbor");
            var locationId = location.GetProperty("id").GetString();
            await owner.Rpc.InvokeAsync("entities/update", "location", locationId, new { parent = "Mira" });
            await owner.Rpc.InvokeAsync("entities/updateLists", "location", locationId, null, null, new[] { new { role = "Home", target = "Mira" } });
            var chapter = Assert.Single((await owner.Rpc.InvokeAsync<ProjectStateDto>("project/createChapter", "Opening")).Chapters);
            var first = Assert.Single(Assert.Single((await owner.Rpc.InvokeAsync<ProjectStateDto>("project/createScene", chapter.Guid, "Arrival")).Chapters).Scenes);
            var second = (await owner.Rpc.InvokeAsync<ProjectStateDto>("project/createScene", chapter.Guid, "Departure")).Chapters.Single().Scenes.Last();
            await owner.Rpc.InvokeAsync("scenes/write", chapter.Guid, first.Id, "<p>Mira arrives.</p>", "Mira arrives.");
            await owner.Rpc.InvokeAsync("scenes/write", chapter.Guid, second.Id, "<p>Mira leaves.</p>", "Mira leaves.");
            await owner.SignInAsync(server.Address);
            var session = await owner.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
            HostedSessionDto? guestSession = null;
            if (competingWriter)
            {
                var invitation = await owner.Rpc.InvokeAsync<string>("collaboration/invite", DateTimeOffset.UtcNow.AddHours(1));
                identity.Subject = "guest";
                await guest.SignInAsync(server.Address);
                guestSession = await guest.Rpc.InvokeAsync<HostedSessionDto>("collaboration/join", server.Address, invitation, Path.Combine(directory.Path, "Guest project"));
                identity.Subject = "writer";
            }
            character = await owner.Rpc.InvokeAsync<JsonElement>("entities/get", "character", characterId);
            var name = character.GetProperty("sharedFields").GetProperty("fields").GetProperty("name").GetGuid();
            location = await owner.Rpc.InvokeAsync<JsonElement>("entities/get", "location", locationId);
            var relationships = location.GetProperty("sharedFields").GetProperty("metadata").GetProperty("relationships").GetGuid();
            var focus = Guid.NewGuid();
            await owner.Rpc.InvokeAsync("collaboration/acquireField", session.SessionId, name, focus);
            if (failure == "ack") server.AfterFieldPush = request =>
            {
                if (request.ResourceId != name) return;
                server.AfterFieldPush = null;
                throw new HubException("Acknowledgement lost after commit.");
            };
            if (failure == "references") server.BeforeFieldPush = request =>
            {
                if (request.ResourceId == relationships) { server.BeforeFieldPush = null; throw new HubException("Reference write interrupted."); }
                return Task.CompletedTask;
            };
            if (failure == "prose") owner.Documents.RejectRenameItem = (await owner.Rpc.InvokeAsync<SceneContentDto>("scenes/read", chapter.Guid, second.Id)).Shared!.ItemId;
            await Assert.ThrowsAsync<RemoteInvocationException>(() => owner.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, name, focus, "Rowan"));
            await owner.Rpc.InvokeAsync("collaboration/releaseField", session.SessionId, name, focus);
            if (guestSession is not null)
                await ChangeReferencesWhileRenameIsPendingAsync(owner, session, name, guest, guestSession, location, chapter.Guid, first.Id);
            if (rotate)
            {
                var member = Assert.Single(await owner.Rpc.InvokeAsync<HostedProjectMember[]>("collaboration/members"), value => value.Role == "Contributor");
                await owner.Rpc.InvokeAsync("collaboration/removeMember", member.UserId);
            }
            if (restart)
            {
                await owner.DisposeAsync();
                owner = new HostedDesktop(settings, identity, credentials);
                await owner.Rpc.InvokeAsync("project/open", project.ProjectPath);
                session = await owner.Rpc.InvokeAsync<HostedSessionDto>("collaboration/resume");
            }
            else
            {
                await owner.Rpc.InvokeAsync("collaboration/acquireField", session.SessionId, name, focus);
                await owner.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, name, focus, "Rowan");
                await owner.Rpc.InvokeAsync("collaboration/releaseField", session.SessionId, name, focus);
            }
            Assert.Equal("Rowan", (await owner.Rpc.InvokeAsync<JsonElement>("entities/get", "character", characterId)).GetProperty("name").GetString());
            location = await owner.Rpc.InvokeAsync<JsonElement>("entities/get", "location", locationId);
            Assert.Equal(competingWriter ? "Other" : "Rowan", location.GetProperty("parent").GetString());
            Assert.Equal("Rowan", location.GetProperty("relationships")[0].GetProperty("target").GetString());
            Assert.Contains("Rowan arrives.", (await owner.Rpc.InvokeAsync<SceneContentDto>("scenes/read", chapter.Guid, first.Id)).Html);
            Assert.Contains("Rowan leaves.", (await owner.Rpc.InvokeAsync<SceneContentDto>("scenes/read", chapter.Guid, second.Id)).Html);
            if (competingWriter) Assert.Contains("Concurrent sentence.", (await owner.Rpc.InvokeAsync<SceneContentDto>("scenes/read", chapter.Guid, first.Id)).Html);
        }
        finally { await owner.DisposeAsync(); }
    }

    private static async Task ChangeReferencesWhileRenameIsPendingAsync(HostedDesktop owner, HostedSessionDto ownerSession, Guid name,
        HostedDesktop guest, HostedSessionDto guestSession, JsonElement location, string chapter, string scene)
    {
        var fields = location.GetProperty("sharedFields");
        var parent = fields.GetProperty("fields").GetProperty("parent").GetGuid();
        var relationships = fields.GetProperty("metadata").GetProperty("relationships").GetGuid();
        var focus = Guid.NewGuid();
        await guest.Rpc.InvokeAsync("collaboration/acquireField", guestSession.SessionId, parent, focus);
        await guest.Rpc.InvokeAsync("collaboration/writeField", guestSession.SessionId, parent, focus, "Other");
        await guest.Rpc.InvokeAsync("collaboration/releaseField", guestSession.SessionId, parent, focus);
        var document = (await guest.Rpc.InvokeAsync<SceneContentDto>("scenes/read", chapter, scene)).Shared!;
        await guest.Rpc.InvokeAsync("collaboration/writeScene", guestSession.SessionId, document.ItemId, Encoding.UTF8.GetBytes("<p>Mira arrives. Concurrent sentence.</p>"));
        await guest.Rpc.InvokeAsync("collaboration/presence", guestSession.SessionId, relationships, Guid.NewGuid(), null, true);
        await guest.Rpc.InvokeAsync("collaboration/acquireField", guestSession.SessionId, relationships, focus);
        await owner.Rpc.InvokeAsync("collaboration/acquireField", ownerSession.SessionId, name, focus);
        var denied = await Assert.ThrowsAsync<RemoteInvocationException>(() => owner.Rpc.InvokeAsync("collaboration/writeField", ownerSession.SessionId, name, focus, "Rowan"));
        Assert.Contains("Writer is editing a reference", denied.Message);
        await owner.Rpc.InvokeAsync("collaboration/releaseField", ownerSession.SessionId, name, focus);
        await guest.Rpc.InvokeAsync("collaboration/releaseField", guestSession.SessionId, relationships, focus);
    }
}
