using System.Text.Json;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Sync.Client.Tests;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class SharedGraphContentionTests
{
    [Fact]
    public async Task AConcurrentRenameWaitsForTheCurrentWriterAndCanBeRetried()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var owner = new HostedDesktop(directory.Combine("owner-settings"), identity);
        await owner.Rpc.InvokeAsync("project/create", directory.Path, "Shared", "Book");
        var first = await owner.Rpc.InvokeAsync<JsonElement>("entities/create", "character", "Mira");
        var second = await owner.Rpc.InvokeAsync<JsonElement>("entities/create", "character", "Lio");
        var chapter = Assert.Single((await owner.Rpc.InvokeAsync<ProjectStateDto>("project/createChapter", "Opening")).Chapters);
        var scene = Assert.Single(Assert.Single((await owner.Rpc.InvokeAsync<ProjectStateDto>("project/createScene", chapter.Guid, "Arrival")).Chapters).Scenes);
        await owner.Rpc.InvokeAsync("scenes/write", chapter.Guid, scene.Id, "<p>Mira arrives.</p>", "Mira arrives.");
        await owner.SignInAsync(server.Address);
        var shared = await owner.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        var link = await owner.Rpc.InvokeAsync<string>("collaboration/invite", DateTimeOffset.UtcNow.AddHours(1));
        identity.Subject = "guest";
        await using var guest = new HostedDesktop(directory.Combine("guest-settings"), identity);
        await guest.SignInAsync(server.Address);
        var joined = await guest.Rpc.InvokeAsync<HostedSessionDto>("collaboration/join", server.Address, link, directory.Combine("Joined"));
        var firstId = first.GetProperty("id").GetString();
        var secondId = second.GetProperty("id").GetString();
        first = await owner.Rpc.InvokeAsync<JsonElement>("entities/get", "character", firstId);
        second = await guest.Rpc.InvokeAsync<JsonElement>("entities/get", "character", secondId);
        var firstName = first.GetProperty("sharedFields").GetProperty("fields").GetProperty("name").GetGuid();
        var secondName = second.GetProperty("sharedFields").GetProperty("fields").GetProperty("name").GetGuid();
        var firstFocus = Guid.NewGuid();
        var secondFocus = Guid.NewGuid();
        await owner.Rpc.InvokeAsync("collaboration/acquireField", shared.SessionId, firstName, firstFocus);
        await guest.Rpc.InvokeAsync("collaboration/acquireField", joined.SessionId, secondName, secondFocus);
        await owner.Rpc.InvokeAsync("collaboration/presence", shared.SessionId, firstName, Guid.NewGuid(), null, true);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.Documents.AfterRename = async _ => { entered.TrySetResult(); await finish.Task.WaitAsync(ct); };
        var rename = owner.Rpc.InvokeAsync("collaboration/writeField", shared.SessionId, firstName, firstFocus, "Miriam");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            var error = await Assert.ThrowsAsync<RemoteInvocationException>(() => guest.Rpc.InvokeAsync("collaboration/writeField", joined.SessionId, secondName, secondFocus, "Leo"));
            Assert.Contains("names or relationships", error.Message);
        }
        finally { finish.TrySetResult(); }
        await rename;
        await guest.Rpc.InvokeAsync("collaboration/writeField", joined.SessionId, secondName, secondFocus, "Leo");
        Assert.Equal("Leo", (await guest.Rpc.InvokeAsync<JsonElement>("entities/get", "character", secondId)).GetProperty("name").GetString());
        Assert.Contains("Miriam arrives.", (await owner.Rpc.InvokeAsync<SceneContentDto>("scenes/read", chapter.Guid, scene.Id)).Html);
    }
}
