using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Sync.Client;
using Novalist.Sync.Client.Tests;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class SharedDeletedRenameTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReopeningDiscardsAnInterruptedRenameOfADeletedEntry(bool rotate)
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        identity.IssueAccessToken = () => identity.Subject;
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        var settings = Path.Combine(directory.Path, "owner-settings");
        var credentials = new Dictionary<string, string>();
        var owner = new HostedDesktop(settings, identity, credentials);
        await using var guest = new HostedDesktop(Path.Combine(directory.Path, "guest-settings"), identity);
        try
        {
            var project = await owner.Rpc.InvokeAsync<ProjectStateDto>("project/create", directory.Path, "Shared", "Book");
            var entry = await owner.Rpc.InvokeAsync<JsonElement>("entities/create", "character", "Mira");
            var id = entry.GetProperty("id").GetString();
            var location = await owner.Rpc.InvokeAsync<JsonElement>("entities/create", "location", "Harbor");
            var locationId = location.GetProperty("id").GetString();
            await owner.Rpc.InvokeAsync("entities/updateLists", "location", locationId, null, null, new[] { new { role = "Home", target = "Mira" } });
            var chapter = Assert.Single((await owner.Rpc.InvokeAsync<ProjectStateDto>("project/createChapter", "Opening")).Chapters);
            var scene = Assert.Single(Assert.Single((await owner.Rpc.InvokeAsync<ProjectStateDto>("project/createScene", chapter.Guid, "Arrival")).Chapters).Scenes);
            await owner.Rpc.InvokeAsync("scenes/write", chapter.Guid, scene.Id, "<p>Mira arrives.</p>", "Mira arrives.");
            await owner.SignInAsync(server.Address);
            var session = await owner.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
            var invitation = await owner.Rpc.InvokeAsync<string>("collaboration/invite", DateTimeOffset.UtcNow.AddHours(1));
            identity.Subject = "guest";
            await guest.SignInAsync(server.Address);
            await guest.Rpc.InvokeAsync<HostedSessionDto>("collaboration/join", server.Address, invitation, Path.Combine(directory.Path, "Guest project"));
            identity.Subject = "writer";
            entry = await owner.Rpc.InvokeAsync<JsonElement>("entities/get", "character", id);
            var name = entry.GetProperty("sharedFields").GetProperty("fields").GetProperty("name").GetGuid();
            location = await owner.Rpc.InvokeAsync<JsonElement>("entities/get", "location", locationId);
            var relationships = location.GetProperty("sharedFields").GetProperty("metadata").GetProperty("relationships").GetGuid();
            var focus = Guid.NewGuid();
            await owner.Rpc.InvokeAsync("collaboration/acquireField", session.SessionId, name, focus);
            server.BeforeFieldPush = request =>
            {
                if (request.ResourceId == relationships) { server.BeforeFieldPush = null; throw new HubException("Reference write interrupted."); }
                return Task.CompletedTask;
            };
            await Assert.ThrowsAsync<RemoteInvocationException>(() => owner.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, name, focus, "Rowan"));
            await owner.Rpc.InvokeAsync("collaboration/releaseField", session.SessionId, name, focus);
            await guest.Rpc.InvokeAsync("entities/delete", "character", id, false);
            await owner.Rpc.InvokeAsync("entities/list", "character");
            if (rotate)
            {
                var member = Assert.Single(await owner.Rpc.InvokeAsync<HostedProjectMember[]>("collaboration/members"), value => value.Role == "Contributor");
                await owner.Rpc.InvokeAsync("collaboration/removeMember", member.UserId);
            }
            await owner.DisposeAsync();
            owner = new HostedDesktop(settings, identity, credentials);
            await owner.Rpc.InvokeAsync("project/open", project.ProjectPath);
            await owner.Rpc.InvokeAsync<HostedSessionDto>("collaboration/resume");
            Assert.Empty((await owner.Rpc.InvokeAsync<JsonElement>("entities/list", "character")).EnumerateArray());
            Assert.Contains("Mira arrives.", (await owner.Rpc.InvokeAsync<SceneContentDto>("scenes/read", chapter.Guid, scene.Id)).Html);
            location = await owner.Rpc.InvokeAsync<JsonElement>("entities/get", "location", locationId);
            Assert.Equal("Mira", location.GetProperty("relationships")[0].GetProperty("target").GetString());
        }
        finally { await owner.DisposeAsync(); }
    }
}
