using System.Text.Json;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Sync.Client;
using Novalist.Sync.Client.Tests;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class SharedCodexTypesTests
{
    [Theory]
    [InlineData("location", "type")]
    [InlineData("item", "origin")]
    [InlineData("lore", "category")]
    [InlineData("species", "habitat")]
    public async Task BuiltInAndCustomCodexFieldsUseRenewableFocusLeases(string type, string key)
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var desktop = new HostedDesktop(Path.Combine(directory.Path, "settings"), identity);
        await desktop.Rpc.InvokeAsync("project/create", directory.Path, "Shared", "Book");
        if (type == "species")
            await desktop.Rpc.InvokeAsync("entities/saveCustomType", new CustomTypeSpecDto(type, "Species", "Species",
                [new(key, "Habitat", "String", "", null, false)], true, true, true));
        var created = await desktop.Rpc.InvokeAsync<JsonElement>("entities/create", type, "Entry");
        var id = created.GetProperty("id").GetString();
        await desktop.SignInAsync(server.Address);
        var session = await desktop.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        var entry = await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", type, id);
        var field = entry.GetProperty("sharedFields").GetProperty("fields").GetProperty(key).GetGuid();
        var focus = Guid.NewGuid();

        await desktop.Rpc.InvokeAsync<SharedFieldFocus>("collaboration/acquireField", session.SessionId, field, focus);
        Assert.NotNull(await desktop.Rpc.InvokeAsync<FieldFocusResult?>("collaboration/renewField", session.SessionId, field, focus));
        await desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, field, focus, "Shared value");
        Assert.True(await desktop.Rpc.InvokeAsync<bool>("collaboration/releaseField", session.SessionId, field, focus));

        entry = await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", type, id);
        Assert.Equal("Shared value", (type == "species" ? entry.GetProperty("fields") : entry).GetProperty(key).GetString());
        await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.Rpc.InvokeAsync("entities/update", type, id, new Dictionary<string, string> { [key] = "Bypassed" }));
        await desktop.Rpc.InvokeAsync("collaboration/end");
        await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.Rpc.InvokeAsync("collaboration/renewField", session.SessionId, field, focus));
    }
}
