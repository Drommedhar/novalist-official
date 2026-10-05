using System.Text.Json;
using Nerdbank.Streams;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class WorkspaceCoordinationTests
{
    [Theory]
    [InlineData("nl/12/3/45/-", "12", 3, null)]
    [InlineData("nl/7/0/1/save-token", "7", 0, "save-token")]
    public void TransportIdentityRetainsOwnerEpochAndSaveToken(string id, string owner, long epoch, string? token)
    {
        var identity = WorkspaceRequestIdentity.Parse(new RequestId(id));
        Assert.NotNull(identity);
        Assert.Equal(owner, identity.Owner);
        Assert.Equal(epoch, identity.Epoch);
        Assert.Equal(token, identity.FlushToken);
    }

    [Theory]
    [InlineData("nl/1/2")]
    [InlineData("nl/name/2/3/-")]
    [InlineData("nl/1/-2/3/-")]
    [InlineData("nl/1/2/x/-")]
    [InlineData("nl/1/2/3/")]
    public void MalformedTransportIdentityCannotFallBackToLegacy(string id)
        => Assert.Throws<InvalidOperationException>(() => WorkspaceRequestIdentity.Parse(new RequestId(id)));

    [Fact]
    public void LegacyIdsAndNestedRequestContextsRemainCompatible()
    {
        Assert.Null(WorkspaceRequestIdentity.Parse(new RequestId(1)));
        Assert.Null(WorkspaceRequestIdentity.Parse(new RequestId("legacy")));
        var owner = new WorkspaceRequestIdentity("1", 0, null);
        using (WorkspaceRequestContext.Enter(owner, null))
        {
            Assert.Same(owner, WorkspaceRequestContext.Identity);
            using (WorkspaceRequestContext.Enter(null, null)) Assert.Null(WorkspaceRequestContext.Identity);
            Assert.Same(owner, WorkspaceRequestContext.Identity);
        }
        Assert.Null(WorkspaceRequestContext.Identity);
    }

    [Fact]
    public async Task GateLeaseReleasesAndReacquiresExactlyOnce()
    {
        using var gate = new SemaphoreSlim(0, 1);
        var lease = new WorkspaceGateLease(gate);
        using (WorkspaceRequestContext.Enter(null, lease))
        {
            Assert.Same(lease, WorkspaceRequestContext.Lease);
            lease.Yield();
            lease.Yield();
            Assert.Equal(1, gate.CurrentCount);
            await lease.ReacquireAsync();
            Assert.Equal(0, gate.CurrentCount);
            lease.Active = false;
            Assert.Null(WorkspaceRequestContext.Lease);
        }
        lease.Yield();
    }

    [Fact]
    public async Task LegacyHostNeedsNoWindowAcknowledgements()
    {
        using var dir = new TempDir();
        using var workspace = new Workspace(dir.Path);
        using var gate = new SemaphoreSlim(1, 1);
        var coordinator = new WorkspaceCoordinator(workspace, gate);
        Assert.Equal(42, await coordinator.RunAsync("project/open", () => Task.FromResult(42)));
        coordinator.Notify = (_, _) => throw new InvalidOperationException("No windows registered");
        await coordinator.RunAsync("project/open", () => Task.CompletedTask);
        Assert.Equal(0, coordinator.Epoch);
        coordinator.Validate(null, "project/open");
        Assert.Throws<InvalidOperationException>(() => coordinator.Validate(null, "workspace/applied"));
    }

    [Fact]
    public async Task TransitionPublishesOneSnapshotAndRejectsStaleOrForeignAcknowledgements()
    {
        using var dir = new TempDir();
        using var workspace = new Workspace(dir.Path);
        using var gate = new SemaphoreSlim(1, 1);
        var coordinator = new WorkspaceCoordinator(workspace, gate);
        var owner = new WorkspaceRequestIdentity("1", 0, null);
        coordinator.Validate(owner, "project/getState");
        var methods = new List<string>();
        coordinator.Notify = async (method, payload) =>
        {
            methods.Add(method);
            var json = JsonSerializer.SerializeToElement(payload);
            var token = json.GetProperty("token").GetString()!;
            if (method == "workspace/prepare")
            {
                Assert.Equal(1, gate.CurrentCount);
                coordinator.Validate(new("2", 0, token), "scenes/write");
                Assert.Throws<InvalidOperationException>(() => coordinator.Validate(new("2", 0, "expired"), "scenes/write"));
                Assert.Throws<InvalidOperationException>(() => coordinator.Validate(new("2", 0, token), "project/open"));
                var conflicting = Task.Run(async () =>
                {
                    using var context = WorkspaceRequestContext.Enter(null, null);
                    // Flow suppression of the public request context does not
                    // grant a second window ownership of this transaction.
                    coordinator.Validate(new("2", 0, null), "project/switchBook");
                    await Task.CompletedTask;
                });
                await Assert.ThrowsAsync<InvalidOperationException>(() => conflicting);
                coordinator.Prepared("foreign", true, null);
                coordinator.Applied("foreign");
                coordinator.Prepared(token, true, null);
            }
            else
            {
                Assert.Equal("workspace/changed", method);
                Assert.Equal(0, gate.CurrentCount);
                Assert.Equal(1, json.GetProperty("epoch").GetInt64());
                coordinator.Applied(token);
            }
        };
        using (WorkspaceRequestContext.Enter(owner, null))
            await coordinator.RunAsync("project/open", () => coordinator.RunAsync("project/switchBook", () => Task.CompletedTask));
        Assert.Equal(["workspace/prepare", "workspace/changed"], methods);
        Assert.Equal(1, owner.Epoch);
        Assert.Throws<InvalidOperationException>(() => coordinator.Validate(new("2", 0, null), "scenes/write"));
        coordinator.Validate(new("0", 0, null), "workspace/snapshot");
        Assert.Throws<InvalidOperationException>(() => coordinator.Validate(owner, "workspace/snapshot"));
        coordinator.ClientClosed("1");
        Assert.Equal(1, gate.CurrentCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefusedOrTimedOutSaveDoesNotRunMutation(bool timeOut)
    {
        using var dir = new TempDir();
        using var workspace = new Workspace(dir.Path);
        using var gate = new SemaphoreSlim(1, 1);
        var coordinator = new WorkspaceCoordinator(workspace, gate) { AcknowledgementTimeout = TimeSpan.FromMilliseconds(30) };
        coordinator.Validate(new("1", 0, null), "project/getState");
        var events = new List<string>();
        coordinator.Notify = (method, payload) =>
        {
            events.Add(method);
            if (method == "workspace/prepare" && !timeOut)
                coordinator.Prepared(JsonSerializer.SerializeToElement(payload).GetProperty("token").GetString()!, false, null);
            return Task.CompletedTask;
        };
        var ran = false;
        await Assert.ThrowsAnyAsync<Exception>(() => coordinator.RunAsync("project/open", () => { ran = true; return Task.CompletedTask; }));
        Assert.False(ran);
        Assert.Equal(0, coordinator.Epoch);
        Assert.Equal(["workspace/prepare", "workspace/aborted"], events);
        Assert.Equal(1, gate.CurrentCount);
    }

    [Fact]
    public async Task FailedMutationStillPublishesActualStateAndMissingApplyAckDoesNotAbortUnlock()
    {
        using var dir = new TempDir();
        using var workspace = new Workspace(dir.Path);
        using var gate = new SemaphoreSlim(1, 1);
        var coordinator = new WorkspaceCoordinator(workspace, gate) { AcknowledgementTimeout = TimeSpan.FromMilliseconds(30) };
        coordinator.Validate(new("1", 0, null), "project/getState");
        var events = new List<string>();
        coordinator.Notify = (method, payload) =>
        {
            events.Add(method);
            if (method == "workspace/prepare")
                coordinator.Prepared(JsonSerializer.SerializeToElement(payload).GetProperty("token").GetString()!, true, null);
            return Task.CompletedTask;
        };
        await Assert.ThrowsAsync<TimeoutException>(() => coordinator.RunAsync("project/open", () => Task.FromException(new IOException("fixture"))));
        Assert.Equal(["workspace/prepare", "workspace/changed"], events);
        Assert.Equal(1, coordinator.Epoch);
        Assert.Equal(1, gate.CurrentCount);
    }

    [Fact]
    public async Task ExtensionContextChangeYieldsTheQueueForSavesAndResumesBeforePublishing()
    {
        using var dir = new TempDir();
        using var workspace = new Workspace(dir.Path);
        using var gate = new SemaphoreSlim(1, 1);
        var coordinator = new WorkspaceCoordinator(workspace, gate);
        var streams = FullDuplexStream.CreatePair();
        using var server = new SerialDispatchJsonRpc(Handler(streams.Item1), gate, coordinator: coordinator);
        using var client = new OwnedClient(Handler(streams.Item2));
        var target = new CoordinatedTarget(coordinator);
        server.AddLocalRpcTarget(target);
        server.AddLocalRpcTarget(new WorkspaceCoordinationRpc(coordinator));
        server.AddLocalRpcTarget(new ScenesRpc(workspace));
        var windows = new WindowAcknowledgements(client);
        client.AddLocalRpcTarget(windows);
        coordinator.Notify = (method, payload) => server.NotifyAsync(method, payload);
        server.StartListening();
        client.StartListening();
        Assert.Equal(1, await client.InvokeAsync<int>("spec/change").WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, target.Writes);
        Assert.Equal(1, coordinator.Epoch);
        Assert.Equal(1, client.Epoch);
        Assert.Equal(0, windows.PrepareFailures);
        await client.InvokeWithParameterObjectAsync("scenes/setEditingMany", new { scenes = new[] { new EditingSceneDto("c", "s", true) } });
        Assert.True(workspace.Editing.IsBusy("c", "s"));
        client.Owner = "2";
        await client.InvokeAsync("scenes/setEditing", "c", "other", true);
        Assert.True(workspace.Editing.IsBusy("c", "s"));
        Assert.True(workspace.Editing.IsBusy("c", "other"));
        client.Owner = "0";
        await client.InvokeAsync("workspace/clientClosed", "1");
        Assert.False(workspace.Editing.IsBusy("c", "s"));
        Assert.True(workspace.Editing.IsBusy("c", "other"));
        _ = await client.InvokeAsync<JsonElement>("workspace/snapshot");
        client.Owner = "2";
        client.Epoch = 0;
        await Assert.ThrowsAsync<RemoteInvocationException>(() => client.InvokeAsync("spec/write"));
        Assert.Equal(1, target.Writes);
        client.Epoch = 1;
        await Assert.ThrowsAsync<RemoteInvocationException>(() => client.InvokeAsync("workspace/prepared", "fake", true));
        Assert.Equal("ok", await client.InvokeAsync<string>("system/ping"));
        client.InvalidId = true;
        await Assert.ThrowsAsync<RemoteInvocationException>(() => client.InvokeAsync("spec/write"));
    }

    private static HeaderDelimitedMessageHandler Handler(Stream stream)
    {
        var formatter = new SystemTextJsonFormatter();
        formatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        return new(stream, stream, formatter);
    }

    private sealed class OwnedClient(IJsonRpcMessageHandler handler) : JsonRpc(handler)
    {
        private long _sequence;
        internal string Owner { get; set; } = "1";
        internal long Epoch { get; set; }
        internal string? Token { get; set; }
        internal bool InvalidId { get; set; }
        protected override RequestId CreateNewRequestId() => new(InvalidId ? "nl/invalid" : $"nl/{Owner}/{Epoch}/{Interlocked.Increment(ref _sequence)}/{Token ?? "-"}");
    }

    private sealed class CoordinatedTarget(WorkspaceCoordinator coordinator)
    {
        internal int Writes { get; private set; }
        [JsonRpcMethod("spec/change")]
        public Task<int> ChangeAsync() => coordinator.RunAsync("extension/switch", () => Task.FromResult(Writes));
        [JsonRpcMethod("spec/write")]
        public void Write() => Writes++;
        [JsonRpcMethod("system/ping")]
        public string Ping() => "ok";
    }

    private sealed class WindowAcknowledgements(OwnedClient client)
    {
        internal int PrepareFailures { get; private set; }
        [JsonRpcMethod("workspace/prepare")]
        public async Task PrepareAsync(JsonElement payload)
        {
            var token = payload.GetProperty("token").GetString()!;
            client.Token = token;
            try { await client.InvokeAsync("spec/write"); }
            catch { PrepareFailures++; throw; }
            finally { client.Token = null; }
            client.Owner = "0";
            await client.InvokeAsync("workspace/prepared", token, true);
            client.Owner = "1";
        }
        [JsonRpcMethod("workspace/changed")]
        public async Task ChangedAsync(JsonElement payload)
        {
            client.Epoch = payload.GetProperty("epoch").GetInt64();
            client.Owner = "0";
            await client.InvokeAsync("workspace/applied", payload.GetProperty("token").GetString()!);
            client.Owner = "1";
        }
    }
}
