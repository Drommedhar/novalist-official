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
    [Theory]
    [InlineData("parent")]
    [InlineData("relationships")]
    [InlineData("pov")]
    public async Task RenamingAnEntryWaitsForReferenceLocksAndUpdatesReferencesAfterRelease(string heldReference)
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
        await desktop.Rpc.InvokeAsync("entities/updateLists", "location", locationId, null, null, new[] { new { role = "Home", target = "Mira" }, new { role = "Neighbor", target = "Other" } });
        var chapter = Assert.Single((await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/createChapter", "Opening")).Chapters);
        var scene = Assert.Single(Assert.Single((await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/createScene", chapter.Guid, "Arrival")).Chapters).Scenes);
        await desktop.Rpc.InvokeAsync("scenes/setAnalysisOverride", chapter.Guid, scene.Id, new { pov = "Mira" });
        await desktop.SignInAsync(server.Address);
        var session = await desktop.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        character = await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "character", characterId);
        location = await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "location", locationId);
        var name = character.GetProperty("sharedFields").GetProperty("fields").GetProperty("name").GetGuid();
        var meta = await desktop.Rpc.InvokeAsync<JsonElement>("scenes/getMeta", chapter.Guid, scene.Id);
        var parent = heldReference switch
        {
            "relationships" => location.GetProperty("sharedFields").GetProperty("metadata").GetProperty("relationships").GetGuid(),
            "pov" => meta.GetProperty("sharedFields").GetProperty("fields").GetProperty("analysisOverrides.pov").GetGuid(),
            _ => location.GetProperty("sharedFields").GetProperty("fields").GetProperty("parent").GetGuid()
        };
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
        var relationships = (await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "location", locationId)).GetProperty("relationships");
        Assert.Equal("Rowan", relationships[0].GetProperty("target").GetString());
        Assert.Equal("Home", relationships[0].GetProperty("role").GetString());
        Assert.Equal("Other", relationships[1].GetProperty("target").GetString());
        Assert.Equal("Rowan", (await desktop.Rpc.InvokeAsync<SceneEditDto>("project/getSceneEdit", chapter.Guid, scene.Id)).Pov);
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

    [Fact]
    public async Task LocalRenameKeepsSameNamedEntriesInAnotherBookUnchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var desktop = new HostedDesktop(Path.Combine(directory.Path, "settings"), identity);
        var project = await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/create", directory.Path, "Shared", "First book");
        var firstBook = project.ActiveBookId;
        var first = await CreateEntriesAsync(desktop);
        project = await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/createBook", "Second book");
        var secondBook = project.Books.Single(book => book.Id != firstBook).Id;
        await desktop.Rpc.InvokeAsync("project/switchBook", secondBook);
        var second = await CreateEntriesAsync(desktop);
        await desktop.Rpc.InvokeAsync("project/switchBook", firstBook);
        await desktop.SignInAsync(server.Address);
        var session = await desktop.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        var character = await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "character", first.Character);
        var name = character.GetProperty("sharedFields").GetProperty("fields").GetProperty("name").GetGuid();

        await WriteAsync(desktop, session.SessionId, name, "Rowan");

        var changed = await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "location", first.Location);
        Assert.Equal("Rowan", changed.GetProperty("parent").GetString());
        Assert.Equal("[[Rowan]]", changed.GetProperty("sections")[0].GetProperty("content").GetString());
        await desktop.Rpc.InvokeAsync("project/switchBook", secondBook);
        var unchanged = await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "location", second.Location);
        Assert.Equal("Mira", unchanged.GetProperty("parent").GetString());
        Assert.Equal("[[Mira]]", unchanged.GetProperty("sections")[0].GetProperty("content").GetString());
    }

    private static async Task<(string Character, string Location)> CreateEntriesAsync(HostedDesktop desktop)
    {
        var character = await desktop.Rpc.InvokeAsync<JsonElement>("entities/create", "character", "Mira");
        var location = await desktop.Rpc.InvokeAsync<JsonElement>("entities/create", "location", "Harbor");
        var id = location.GetProperty("id").GetString()!;
        await desktop.Rpc.InvokeAsync("entities/update", "location", id, new { parent = "Mira" });
        await desktop.Rpc.InvokeAsync("entities/updateLists", "location", id, null, new[] { new EntitySectionDto("Notes", "[[Mira]]", "notes") }, null);
        return (character.GetProperty("id").GetString()!, id);
    }
}
