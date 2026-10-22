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
    public async Task AFailedMetadataReplayKeepsTheLocalCopyAndCanBeRetried()
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
        var age = entity.GetProperty("sharedFields").GetProperty("fields").GetProperty("age").GetGuid();
        var focus = Guid.NewGuid();
        await desktop.Rpc.InvokeAsync("collaboration/acquireField", session.SessionId, age, focus);
        await desktop.Rpc.InvokeAsync("collaboration/writeField", session.SessionId, age, focus, "42");
        await desktop.Rpc.InvokeAsync("collaboration/releaseField", session.SessionId, age, focus);
        await desktop.Rpc.InvokeAsync("collaboration/end");
        await desktop.Rpc.InvokeAsync("entities/update", "character", id, new { age = "Local retained value" });
        files.RejectSharedAge = true;

        var error = await Assert.ThrowsAsync<RemoteInvocationException>(() => desktop.Rpc.InvokeAsync("collaboration/resume"));

        Assert.Contains("Temporary metadata write failure", error.Message);
        Assert.Null(await desktop.Rpc.InvokeAsync<HostedSessionDto?>("collaboration/status"));
        Assert.Equal("Local retained value", (await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "character", id)).GetProperty("age").GetString());
        await desktop.Rpc.InvokeAsync("collaboration/resume");
        Assert.Equal("42", (await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "character", id)).GetProperty("age").GetString());
    }

    private sealed class FailingFileService : FileService, IFileService
    {
        internal bool RejectSharedAge { get; set; }

        public new Task WriteBytesAsync(string path, byte[] bytes)
        {
            if (RejectSharedAge && path.EndsWith(".json", StringComparison.Ordinal) && Encoding.UTF8.GetString(bytes).Contains("\"age\": \"42\"", StringComparison.Ordinal))
            {
                RejectSharedAge = false;
                throw new IOException("Temporary metadata write failure.");
            }
            return base.WriteBytesAsync(path, bytes);
        }
    }
}
