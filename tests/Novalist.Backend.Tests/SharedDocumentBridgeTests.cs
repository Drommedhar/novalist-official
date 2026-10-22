using System.Text.Json;
using Nerdbank.Streams;
using Novalist.Sync.Client;
using Novalist.Backend.Tests.TestHelpers;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class SharedDocumentBridgeTests
{
    [Fact]
    public async Task BackendDocumentClientExchangesBinaryStatesThroughDuplexRpc()
    {
        using var temporary = new TempDir();
        var (backendStream, desktopStream) = FullDuplexStream.CreatePair();
        using var host = new BackendHost(temporary.Path);
        host.Attach(backendStream, backendStream);
        var formatter = new SystemTextJsonFormatter();
        formatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        using var desktop = new JsonRpc(new HeaderDelimitedMessageHandler(desktopStream, desktopStream, formatter));
        var peer = new DesktopPeer();
        desktop.AddLocalRpcTarget(peer);
        desktop.StartListening();
        var factory = host.Workspace.CreateSharedDocuments ?? throw new InvalidOperationException();
        var session = Guid.NewGuid();
        var item = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;
        await using (var documents = factory(session))
        {
            await documents.OpenAsync(ct);
            await documents.SeedAsync(item, "<p>Original.</p>", ct);
            await documents.LoadAsync(item, [1, 2, 3, 4], [[5, 6]], ct);
            await documents.ReceiveAsync(item, [7, 8], ct);
            await documents.RemoveAsync(item, [9, 10], ct);
            var snapshot = await documents.CaptureAsync(item, ct);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, snapshot.State);
            Assert.Equal(new byte[] { 5, 6 }, Assert.Single(snapshot.Deletions));
            Assert.Null(snapshot.Html);
            Assert.Equal("<p>Recovered.</p>", snapshot.Recovered);
            await Assert.ThrowsAsync<RemoteInvocationException>(() => documents.CaptureAsync(peer.InvalidItem, ct));
            using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var capture = documents.CaptureAsync(peer.WaitingItem, cancelled.Token);
            await peer.Waiting.Task.WaitAsync(ct);
            await cancelled.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture);
        }
        Assert.True(peer.Closed);
        Assert.All(peer.Sessions, value => Assert.Equal(session, value));
        var loaded = Assert.Single(peer.Commands, value => value.GetProperty("kind").GetString() == "load");
        Assert.Equal("AQIDBA==", loaded.GetProperty("state").GetString());
        Assert.Equal("BQY=", loaded.GetProperty("deletions")[0].GetString());
        Assert.Equal(item, loaded.GetProperty("id").GetGuid());
    }

    public sealed class DesktopPeer
    {
        public Guid InvalidItem { get; } = Guid.NewGuid();
        public Guid WaitingItem { get; } = Guid.NewGuid();
        public List<Guid> Sessions { get; } = [];
        public List<JsonElement> Commands { get; } = [];
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Closed { get; private set; }

        [JsonRpcMethod("collaboration/documents")]
        public async Task<object?> DocumentsAsync(Guid sessionId, string action, JsonElement command = default,
            Guid? id = null, CancellationToken cancellationToken = default)
        {
            Sessions.Add(sessionId);
            if (action == "close") Closed = true;
            if (action == "execute") Commands.Add(command.Clone());
            if (id == InvalidItem) throw new InvalidOperationException("Document unavailable.");
            if (id == WaitingItem)
            {
                Waiting.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            if (action == "capture") return new SharedSceneSnapshot([1, 2, 3, 4], [[5, 6]], null, "<p>Recovered.</p>");
            return action == "execute" && command.GetProperty("kind").GetString() == "receive" ? new SceneApplyResult(true, []) : null;
        }
    }
}
