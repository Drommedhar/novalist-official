using System.Text.Json;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Sync.Client.Tests;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class SharedPlaceParentTests
{
    [Theory]
    [InlineData("parent", false)]
    [InlineData("parent", true)]
    [InlineData("name", false)]
    [InlineData("name", true)]
    [InlineData("isWorld", false)]
    [InlineData("isWorld", true)]
    public async Task GraphChangesCannotCommitFromConcurrentOrExpiredSnapshots(string operation, bool expired)
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var owner = new HostedDesktop(Path.Combine(directory.Path, "owner-settings"), identity);
        await owner.Rpc.InvokeAsync("project/create", directory.Path, "Shared", "Book");
        var first = await owner.Rpc.InvokeAsync<JsonElement>("entities/create", "location", "First");
        var second = await owner.Rpc.InvokeAsync<JsonElement>("entities/create", "location", "Second");
        await owner.SignInAsync(server.Address);
        var ownerSession = await owner.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        var invitation = await owner.Rpc.InvokeAsync<string>("collaboration/invite", DateTimeOffset.UtcNow.AddHours(1));
        identity.Subject = "guest";
        await using var guest = new HostedDesktop(Path.Combine(directory.Path, "guest-settings"), identity);
        await guest.SignInAsync(server.Address);
        var guestSession = await guest.Rpc.InvokeAsync<HostedSessionDto>("collaboration/join", server.Address, invitation, Path.Combine(directory.Path, "Guest"));
        first = await owner.Rpc.InvokeAsync<JsonElement>("entities/get", "location", first.GetProperty("id").GetString());
        second = await guest.Rpc.InvokeAsync<JsonElement>("entities/get", "location", second.GetProperty("id").GetString());
        var firstParent = first.GetProperty("sharedFields").GetProperty(operation == "isWorld" ? "metadata" : "fields").GetProperty(operation).GetGuid();
        var secondParent = second.GetProperty("sharedFields").GetProperty("fields").GetProperty("parent").GetGuid();
        var firstFocus = Guid.NewGuid();
        var secondFocus = Guid.NewGuid();
        await owner.Rpc.InvokeAsync("collaboration/acquireField", ownerSession.SessionId, firstParent, firstFocus);
        await guest.Rpc.InvokeAsync("collaboration/acquireField", guestSession.SessionId, secondParent, secondFocus);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Novalist.Sync.Client.FieldLeaseCondition? graph = null;
        server.BeforeFieldPush = async request => { if (request.ResourceId == firstParent) { graph = request.RequiredLease; entered.TrySetResult(); await resume.Task.WaitAsync(ct); } };
        var value = operation == "parent" ? "Second" : operation == "name" ? "Renamed" : "true";
        var pending = owner.Rpc.InvokeAsync("collaboration/writeField", ownerSession.SessionId, firstParent, firstFocus, value);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            await Assert.ThrowsAsync<RemoteInvocationException>(() => guest.Rpc.InvokeAsync("collaboration/writeField", guestSession.SessionId, secondParent, secondFocus, "First"));
            if (expired)
            {
                Assert.NotNull(graph);
                lock (server.Gate)
                {
                    var key = server.Leases.Keys.Single(key => key.Item2 == graph.ResourceId);
                    server.Leases[key] = server.Leases[key] with { ExpiresAt = DateTimeOffset.UnixEpoch };
                }
                await guest.Rpc.InvokeAsync("collaboration/writeField", guestSession.SessionId, secondParent, secondFocus, "First");
            }
        }
        finally
        {
            resume.TrySetResult();
            if (expired) await Assert.ThrowsAsync<RemoteInvocationException>(() => pending);
            else await pending;
        }
        if (!expired && operation == "parent") await Assert.ThrowsAsync<RemoteInvocationException>(() => guest.Rpc.InvokeAsync("collaboration/writeField", guestSession.SessionId, secondParent, secondFocus, "First"));
        Assert.Equal(expired ? "First" : "", (await guest.Rpc.InvokeAsync<JsonElement>("entities/get", "location", second.GetProperty("id").GetString())).GetProperty("parent").GetString());
        first = await owner.Rpc.InvokeAsync<JsonElement>("entities/get", "location", first.GetProperty("id").GetString());
        Assert.Equal(operation == "name" && !expired ? "Renamed" : "First", first.GetProperty("name").GetString());
        Assert.Equal(operation == "isWorld" && !expired, first.TryGetProperty("isWorld", out var isWorld) && isWorld.GetBoolean());
    }

    [Fact]
    public async Task LeasedParentEditsRejectCyclesAndKeepWorldsAtTheRoot()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var desktop = new HostedDesktop(Path.Combine(directory.Path, "settings"), identity);
        await desktop.Rpc.InvokeAsync("project/create", directory.Path, "Shared", "Book");
        var harbor = await desktop.Rpc.InvokeAsync<JsonElement>("entities/create", "location", "Harbor");
        var quay = await desktop.Rpc.InvokeAsync<JsonElement>("entities/create", "location", "Quay");
        var harborId = harbor.GetProperty("id").GetString();
        var quayId = quay.GetProperty("id").GetString();
        await desktop.Rpc.InvokeAsync("entities/setParent", quayId, "Harbor");
        await desktop.SignInAsync(server.Address);
        var session = await desktop.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        harbor = await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "location", harborId);
        var fields = harbor.GetProperty("sharedFields");
        var parent = fields.GetProperty("fields").GetProperty("parent").GetGuid();
        var focus = Guid.NewGuid();
        await desktop.Rpc.InvokeAsync("collaboration/acquireField", session.SessionId, parent, focus);
        foreach (var name in new[] { "Harbor", "Quay" })
            await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, parent, focus, name));
        await desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, parent, focus, "  Uncharted  ");
        Assert.Equal("Uncharted", (await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "location", harborId)).GetProperty("parent").GetString());
        await desktop.Rpc.InvokeAsync("collaboration/releaseField", session.SessionId, parent, focus);
        var nameField = fields.GetProperty("fields").GetProperty("name").GetGuid();
        var renameFocus = Guid.NewGuid();
        await desktop.Rpc.InvokeAsync("collaboration/acquireField", session.SessionId, nameField, renameFocus);
        await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, nameField, renameFocus, "Quay"));
        Assert.Equal("Harbor", (await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "location", harborId)).GetProperty("name").GetString());
        await desktop.Rpc.InvokeAsync("collaboration/releaseField", session.SessionId, nameField, renameFocus);
        var world = fields.GetProperty("metadata").GetProperty("isWorld").GetGuid();
        var worldFocus = Guid.NewGuid();
        await desktop.Rpc.InvokeAsync("collaboration/acquireField", session.SessionId, world, worldFocus);
        await desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, world, worldFocus, "true");
        await desktop.Rpc.InvokeAsync("collaboration/releaseField", session.SessionId, world, worldFocus);
        await desktop.Rpc.InvokeAsync("collaboration/acquireField", session.SessionId, parent, focus);
        await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, parent, focus, "Uncharted"));
        await desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, parent, focus, "");
        Assert.Equal("", (await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "location", harborId)).GetProperty("parent").GetString());
        await desktop.Rpc.InvokeAsync("collaboration/releaseField", session.SessionId, parent, focus);
    }
}
