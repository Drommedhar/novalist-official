using System.Text;
using System.Text.Json;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Core.Services;
using Novalist.Sync.Client.Tests;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class HostedProjectMetadataRecoveryTests
{
    [Fact]
    public async Task FailedFieldProjectionReleasesTheGraphLeaseAndAllowsRenameRetry()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        var files = new FailingFileService();
        await using var desktop = new HostedDesktop(Path.Combine(directory.Path, "settings"), identity, fileService: files);
        await desktop.Rpc.InvokeAsync("project/create", directory.Path, "Shared", "Book");
        var entity = await desktop.Rpc.InvokeAsync<JsonElement>("entities/create", "character", "Mira");
        var id = entity.GetProperty("id").GetString();
        await desktop.SignInAsync(server.Address);
        var session = await desktop.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        entity = await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "character", id);
        var fields = entity.GetProperty("sharedFields").GetProperty("fields");
        var age = fields.GetProperty("age").GetGuid();
        var name = fields.GetProperty("name").GetGuid();
        var ageFocus = Guid.NewGuid();
        var nameFocus = Guid.NewGuid();
        await desktop.Rpc.InvokeAsync("collaboration/acquireField", session.SessionId, age, ageFocus);
        await desktop.Rpc.InvokeAsync("collaboration/acquireField", session.SessionId, name, nameFocus);
        files.RejectSharedAge = true;
        files.KeepRejecting = true;
        await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, age, ageFocus, "42"));

        var error = await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, name, nameFocus, "Miriam"));

        Assert.Contains("Temporary metadata write failure", error.Message);
        files.RejectSharedAge = false;
        await desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, name, nameFocus, "Miriam");
        entity = await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "character", id);
        Assert.Equal("Miriam", entity.GetProperty("name").GetString());
        Assert.Equal("42", entity.GetProperty("age").GetString());
        await desktop.Rpc.InvokeAsync("collaboration/releaseField", session.SessionId, name, nameFocus);
        await desktop.Rpc.InvokeAsync("collaboration/releaseField", session.SessionId, age, ageFocus);
    }

    [Fact]
    public async Task ConfirmedRenameRetriesReferencesAfterItsLocalNameProjectionFails()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        var files = new FailingFileService();
        await using var desktop = new HostedDesktop(directory.Combine("settings"), identity, fileService: files);
        await desktop.Rpc.InvokeAsync("project/create", directory.Path, "Shared", "Book");
        var character = await desktop.Rpc.InvokeAsync<JsonElement>("entities/create", "character", "Mira");
        var characterId = character.GetProperty("id").GetString();
        var location = await desktop.Rpc.InvokeAsync<JsonElement>("entities/create", "location", "Harbor");
        var locationId = location.GetProperty("id").GetString();
        await desktop.Rpc.InvokeAsync("entities/update", "location", locationId, new { parent = "Mira" });
        await desktop.SignInAsync(server.Address);
        var session = await desktop.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        character = await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "character", characterId);
        var name = character.GetProperty("sharedFields").GetProperty("fields").GetProperty("name").GetGuid();
        var focus = Guid.NewGuid();
        await desktop.Rpc.InvokeAsync("collaboration/acquireField", session.SessionId, name, focus);
        files.RejectNextName = true;
        await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, name, focus, "Rowan"));
        await desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, name, focus, "Rowan");
        Assert.Equal("Rowan", (await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "character", characterId)).GetProperty("name").GetString());
        Assert.Equal("Rowan", (await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "location", locationId)).GetProperty("parent").GetString());
    }

    private sealed class FailingFileService : FileService, IFileService
    {
        internal bool RejectSharedAge { get; set; }
        internal bool RejectNextName { get; set; }
        internal bool KeepRejecting { get; set; }

        public new Task WriteBytesAsync(string path, byte[] bytes)
        {
            if (RejectNextName && path.EndsWith(".json", StringComparison.Ordinal) && Encoding.UTF8.GetString(bytes).Contains("\"name\": \"Rowan\"", StringComparison.Ordinal))
            {
                RejectNextName = false;
                throw new IOException("Temporary name write failure.");
            }
            if (RejectSharedAge && path.EndsWith(".json", StringComparison.Ordinal) && Encoding.UTF8.GetString(bytes).Contains("\"age\": \"42\"", StringComparison.Ordinal))
            {
                RejectSharedAge = KeepRejecting;
                throw new IOException("Temporary metadata write failure.");
            }
            return base.WriteBytesAsync(path, bytes);
        }
    }
}
