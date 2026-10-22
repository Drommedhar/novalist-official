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
    public async Task WorldBibleRenamesValidateInactiveBookHierarchiesBeforeChangingAnyReference()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var desktop = new HostedDesktop(Path.Combine(directory.Path, "settings"), identity);
        var project = await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/create", directory.Path, "Shared", "First book");
        var firstBook = project.ActiveBookId;
        var harbor = await desktop.Rpc.InvokeAsync<JsonElement>("entities/create", "location", "Harbor");
        var harborId = harbor.GetProperty("id").GetString();
        await desktop.Rpc.InvokeAsync("entities/moveToWorldBible", "location", harborId);
        project = await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/createBook", "Second book");
        var secondBook = project.Books.Single(book => book.Id != firstBook).Id;
        await desktop.Rpc.InvokeAsync("project/switchBook", secondBook);
        var quay = await desktop.Rpc.InvokeAsync<JsonElement>("entities/create", "location", "Quay");
        var quayId = quay.GetProperty("id").GetString();
        await desktop.Rpc.InvokeAsync("entities/setParent", quayId, "Harbor");
        await desktop.Rpc.InvokeAsync("project/switchBook", firstBook);
        await desktop.SignInAsync(server.Address);
        var session = await desktop.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        harbor = await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "location", harborId);
        var name = harbor.GetProperty("sharedFields").GetProperty("fields").GetProperty("name").GetGuid();
        var focus = Guid.NewGuid();
        focus = Guid.NewGuid();
        await desktop.Rpc.InvokeAsync("collaboration/acquireField", session.SessionId, name, focus);
        await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, name, focus, "Quay"));
        Assert.Equal("Harbor", (await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "location", harborId)).GetProperty("name").GetString());
        await desktop.Rpc.InvokeAsync("project/switchBook", secondBook);
        Assert.Equal("Harbor", (await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "location", quayId)).GetProperty("parent").GetString());
        await desktop.Rpc.InvokeAsync("project/switchBook", firstBook);
        await desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, name, focus, "Coast");
        await desktop.Rpc.InvokeAsync("collaboration/releaseField", session.SessionId, name, focus);
        await desktop.Rpc.InvokeAsync("project/switchBook", secondBook);
        Assert.Equal("Coast", (await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "location", quayId)).GetProperty("parent").GetString());
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
        focus = Guid.NewGuid();
        await desktop.Rpc.InvokeAsync("collaboration/acquireField", session.SessionId, parent, focus);
        foreach (var name in new[] { "Harbor", "Quay" })
            await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, parent, focus, name));
        await desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, parent, focus, "  Uncharted  ");
        Assert.Equal("Uncharted", (await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "location", harborId)).GetProperty("parent").GetString());
        await desktop.Rpc.InvokeAsync("collaboration/releaseField", session.SessionId, parent, focus);
        var nameField = fields.GetProperty("fields").GetProperty("name").GetGuid();
        var renameFocus = Guid.NewGuid();
        renameFocus = Guid.NewGuid();
        await desktop.Rpc.InvokeAsync("collaboration/acquireField", session.SessionId, nameField, renameFocus);
        await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, nameField, renameFocus, "Quay"));
        Assert.Equal("Harbor", (await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "location", harborId)).GetProperty("name").GetString());
        await desktop.Rpc.InvokeAsync("collaboration/releaseField", session.SessionId, nameField, renameFocus);
        var world = fields.GetProperty("metadata").GetProperty("isWorld").GetGuid();
        var worldFocus = Guid.NewGuid();
        worldFocus = Guid.NewGuid();
        await desktop.Rpc.InvokeAsync("collaboration/acquireField", session.SessionId, world, worldFocus);
        await desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, world, worldFocus, "true");
        await desktop.Rpc.InvokeAsync("collaboration/releaseField", session.SessionId, world, worldFocus);
        focus = Guid.NewGuid();
        await desktop.Rpc.InvokeAsync("collaboration/acquireField", session.SessionId, parent, focus);
        await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, parent, focus, "Uncharted"));
        await desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, parent, focus, "");
        Assert.Equal("", (await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "location", harborId)).GetProperty("parent").GetString());
        await desktop.Rpc.InvokeAsync("collaboration/releaseField", session.SessionId, parent, focus);
    }
}
