using System.Text.Json;
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
    [Fact]
    public async Task ConfirmedRenameFinishesAfterTheDocumentHostRecovers()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        identity.IssueAccessToken = () => identity.Subject;
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        var settings = Path.Combine(directory.Path, "settings");
        var credentials = new Dictionary<string, string>();
        var owner = new HostedDesktop(settings, identity, credentials);
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
            character = await owner.Rpc.InvokeAsync<JsonElement>("entities/get", "character", characterId);
            var name = character.GetProperty("sharedFields").GetProperty("fields").GetProperty("name").GetGuid();
            location = await owner.Rpc.InvokeAsync<JsonElement>("entities/get", "location", locationId);
            var relationships = location.GetProperty("sharedFields").GetProperty("metadata").GetProperty("relationships").GetGuid();
            var focus = Guid.NewGuid();
            focus = Guid.NewGuid();
            await owner.Rpc.InvokeAsync("collaboration/acquireField", session.SessionId, name, focus);
            owner.Documents.RejectRenameItem = (await owner.Rpc.InvokeAsync<SceneContentDto>("scenes/read", chapter.Guid, second.Id)).Shared!.ItemId;
            await Assert.ThrowsAsync<RemoteInvocationException>(() => owner.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, name, focus, "Rowan"));
            await owner.Rpc.InvokeAsync("collaboration/releaseField", session.SessionId, name, focus);
            focus = Guid.NewGuid();
            await owner.Rpc.InvokeAsync("collaboration/acquireField", session.SessionId, name, focus);
            await owner.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, name, focus, "Rowan");
            await owner.Rpc.InvokeAsync("collaboration/releaseField", session.SessionId, name, focus);
            Assert.Equal("Rowan", (await owner.Rpc.InvokeAsync<JsonElement>("entities/get", "character", characterId)).GetProperty("name").GetString());
            location = await owner.Rpc.InvokeAsync<JsonElement>("entities/get", "location", locationId);
            Assert.Equal("Rowan", location.GetProperty("parent").GetString());
            Assert.Equal("Rowan", location.GetProperty("relationships")[0].GetProperty("target").GetString());
            Assert.Contains("Rowan arrives.", (await owner.Rpc.InvokeAsync<SceneContentDto>("scenes/read", chapter.Guid, first.Id)).Html);
            Assert.Contains("Rowan leaves.", (await owner.Rpc.InvokeAsync<SceneContentDto>("scenes/read", chapter.Guid, second.Id)).Html);
        }
        finally { await owner.DisposeAsync(); }
    }

}
