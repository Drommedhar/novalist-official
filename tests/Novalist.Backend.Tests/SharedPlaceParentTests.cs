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
