using System.Text.Json;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Sync.Client.Tests;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class SharedCodexRenameTests
{
    [Fact]
    public async Task RenamingAnEntryWaitsForItsParentReferenceLockAndUpdatesBothAfterRelease()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var desktop = new HostedDesktop(Path.Combine(directory.Path, "settings"), identity);
        await desktop.Rpc.InvokeAsync("project/create", directory.Path, "Shared", "Book");
        var character = await desktop.Rpc.InvokeAsync<JsonElement>("entities/create", "character", "Mira");
        var characterId = character.GetProperty("id").GetString();
        var location = await desktop.Rpc.InvokeAsync<JsonElement>("entities/create", "location", "Harbor");
        var locationId = location.GetProperty("id").GetString();
        await desktop.Rpc.InvokeAsync("entities/update", "location", locationId, new { parent = "Mira" });
        await desktop.SignInAsync(server.Address);
        var session = await desktop.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        character = await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "character", characterId);
        location = await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "location", locationId);
        var name = character.GetProperty("sharedFields").GetProperty("fields").GetProperty("name").GetGuid();
        var parent = location.GetProperty("sharedFields").GetProperty("fields").GetProperty("parent").GetGuid();
        var renameFocus = Guid.NewGuid();
        var parentFocus = Guid.NewGuid();
        await desktop.Rpc.InvokeAsync("collaboration/acquireField", session.SessionId, name, renameFocus);
        await desktop.Rpc.InvokeAsync("collaboration/acquireField", session.SessionId, parent, parentFocus);

        var blocked = await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, name, renameFocus, "Rowan"));

        Assert.Contains("reference", blocked.Message);
        Assert.Equal("Mira", (await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "character", characterId)).GetProperty("name").GetString());
        Assert.Equal("Mira", (await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "location", locationId)).GetProperty("parent").GetString());
        await desktop.Rpc.InvokeAsync("collaboration/releaseField", session.SessionId, parent, parentFocus);
        await desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, name, renameFocus, "Rowan");
        await desktop.Rpc.InvokeAsync("collaboration/releaseField", session.SessionId, name, renameFocus);
        Assert.Equal("Rowan", (await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "character", characterId)).GetProperty("name").GetString());
        Assert.Equal("Rowan", (await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "location", locationId)).GetProperty("parent").GetString());
    }

    [Fact]
    public async Task ConfirmedNameAndSurnameChangesReachSharedScenesAndSectionsButRejectedWritesDoNot()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var owner = new HostedDesktop(Path.Combine(directory.Path, "owner-settings"), identity);
        await owner.Rpc.InvokeAsync("project/create", directory.Path, "Shared", "Book");
        var chapter = Assert.Single((await owner.Rpc.InvokeAsync<ProjectStateDto>("project/createChapter", "Opening")).Chapters);
        var scene = Assert.Single(Assert.Single((await owner.Rpc.InvokeAsync<ProjectStateDto>("project/createScene", chapter.Guid, "Arrival")).Chapters).Scenes);
        await owner.Rpc.InvokeAsync("scenes/write", chapter.Guid, scene.Id, "<p>Mira arrives.</p>", "Mira arrives.");
        await owner.Rpc.InvokeAsync("project/createScene", chapter.Guid, "No references");
        var character = await owner.Rpc.InvokeAsync<JsonElement>("entities/create", "character", "Mira");
        var id = character.GetProperty("id").GetString();
        await owner.Rpc.InvokeAsync("entities/updateLists", "character", id, null, new[] { new EntitySectionDto("Notes", "Mira returns.", "notes") }, null);
        await owner.SignInAsync(server.Address);
        var session = await owner.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        var invitation = await owner.Rpc.InvokeAsync<string>("collaboration/invite", DateTimeOffset.UtcNow.AddHours(1));
        identity.Subject = "guest";
        await using var guest = new HostedDesktop(Path.Combine(directory.Path, "guest-settings"), identity);
        await guest.SignInAsync(server.Address);
        await guest.Rpc.InvokeAsync("collaboration/join", server.Address, invitation, Path.Combine(directory.Path, "Guest copy"));
        character = await owner.Rpc.InvokeAsync<JsonElement>("entities/get", "character", id);
        var fields = character.GetProperty("sharedFields").GetProperty("fields");
        var name = fields.GetProperty("name").GetGuid();
        var surname = fields.GetProperty("surname").GetGuid();
        var focus = Guid.NewGuid();
        await Assert.ThrowsAsync<RemoteInvocationException>(() => owner.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, name, focus, "Rejected"));
        await Assert.ThrowsAsync<RemoteInvocationException>(() => owner.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, Guid.NewGuid(), focus, "Invalid binding"));
        Assert.Contains("Mira arrives.", (await owner.Rpc.InvokeAsync<SceneContentDto>("scenes/read", chapter.Guid, scene.Id)).Html);

        await WriteAsync(owner, session.SessionId, fields.GetProperty("age").GetGuid(), "42");
        await WriteAsync(owner, session.SessionId, name, "Rowan");
        await WriteAsync(owner, session.SessionId, surname, "Vale");
        await WriteAsync(owner, session.SessionId, surname, "Vale");
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var html = (await guest.Rpc.InvokeAsync<SceneContentDto>("scenes/read", chapter.Guid, scene.Id)).Html;
            var entry = await guest.Rpc.InvokeAsync<JsonElement>("entities/get", "character", id);
            if (html.Contains("Rowan Vale arrives.", StringComparison.Ordinal) && entry.GetProperty("sections")[0].GetProperty("content").GetString() == "Rowan Vale returns.")
            {
                Assert.Equal("42", entry.GetProperty("age").GetString());
                return;
            }
            await Task.Delay(20, ct);
        }
        Assert.Fail("The confirmed name did not reach shared prose.");
    }

    private static async Task WriteAsync(HostedDesktop desktop, Guid session, Guid item, string value)
    {
        var focus = Guid.NewGuid();
        await desktop.Rpc.InvokeAsync("collaboration/acquireField", session, item, focus);
        await desktop.Rpc.InvokeAsync("collaboration/writeField", session, item, focus, value);
        await desktop.Rpc.InvokeAsync("collaboration/releaseField", session, item, focus);
    }
}
