using System.Net;
using System.Net.Sockets;
using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class RelayCapacityTests
{
    [Fact]
    public async Task HostBoundsConnectingAndActiveRoutesAndReusesReleasedCapacity()
    {
        using var deadline = TestDeadline();
        var ct = deadline.Token;
        await using var relay = await TestPacketRelay.StartAsync(ct);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var attaching = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowAttachment = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokenRequests = 0;
        Task<string> TokenAsync(CancellationToken token)
        {
            if (Interlocked.Increment(ref tokenRequests) != 2) return Task.FromResult("test");
            attaching.SetResult();
            return allowAttachment.Task.WaitAsync(token);
        }
        await using var host = await RelayPeerTunnel.HostAsync(relay.Address,
            ((IPEndPoint)listener.LocalEndpoint).Port, TokenAsync, 1, ct);
        var firstId = await relay.RequestRouteAsync(ct);
        using var firstPeer = await listener.AcceptTcpClientAsync(ct);
        await attaching.Task.WaitAsync(ct);
        var nextId = await relay.RequestRouteAsync(ct);
        for (var i = 0; i < 8; i++) await relay.RequestRouteAsync(ct);

        await AssertNoAdditionalConnectionAsync(relay, ct);
        Assert.False(listener.Pending());
        allowAttachment.SetResult("test");
        using var first = await relay.AcceptAsync(ct);
        Assert.Equal(firstId, first.RouteId);
        await AssertNoAdditionalConnectionAsync(relay, ct);
        first.Dispose();
        using var nextPeer = await listener.AcceptTcpClientAsync(ct);
        using var next = await relay.AcceptAsync(ct);
        Assert.Equal(nextId, next.RouteId);
        await AssertTransferAsync(nextPeer, next, ct);

        await host.DisposeAsync().AsTask().WaitAsync(ct);
        Assert.Equal(0, await nextPeer.GetStream().ReadAsync(new byte[1], ct));
        Assert.Null(host.Failure);
    }

    [Fact]
    public async Task GuestBoundsLocalConnectionsAndReusesReleasedCapacity()
    {
        using var deadline = TestDeadline();
        var ct = deadline.Token;
        await using var relay = await TestPacketRelay.StartAsync(ct);
        await using var guest = await RelayPeerTunnel.ConnectAsync(relay.Address, relay.SessionId, null, 1, ct);
        using var first = await relay.AcceptAsync(ct);
        using var firstPeer = new TcpClient();
        await firstPeer.ConnectAsync(IPAddress.Loopback, guest.LocalPort, ct);
        await AssertTransferAsync(firstPeer, first, ct);
        using var nextPeer = new TcpClient();
        await nextPeer.ConnectAsync(IPAddress.Loopback, guest.LocalPort, ct);

        await AssertNoAdditionalConnectionAsync(relay, ct);
        first.Dispose();
        using var next = await relay.AcceptAsync(ct);
        await AssertTransferAsync(nextPeer, next, ct);
        using var waitingPeer = new TcpClient();
        await waitingPeer.ConnectAsync(IPAddress.Loopback, guest.LocalPort, ct);
        await guest.DisposeAsync().AsTask().WaitAsync(ct);
        Assert.Equal(0, await nextPeer.GetStream().ReadAsync(new byte[1], ct));
        Assert.Null(guest.Failure);
    }

    [Fact]
    public async Task FailedHostAttachmentReleasesCapacity()
    {
        using var deadline = TestDeadline();
        var ct = deadline.Token;
        await using var relay = await TestPacketRelay.StartAsync(ct);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        await using var host = await RelayPeerTunnel.HostAsync(relay.Address,
            ((IPEndPoint)listener.LocalEndpoint).Port, null, 1, ct);
        relay.RejectedRoute = Guid.NewGuid();
        await relay.RequestRouteAsync(relay.RejectedRoute, ct);
        using var rejectedPeer = await listener.AcceptTcpClientAsync(ct);
        Assert.Equal(0, await rejectedPeer.GetStream().ReadAsync(new byte[1], ct));

        var id = await relay.RequestRouteAsync(ct);
        using var peer = await listener.AcceptTcpClientAsync(ct);
        using var connection = await relay.AcceptAsync(ct);
        Assert.Equal(id, connection.RouteId);
        await AssertTransferAsync(peer, connection, ct);
        Assert.Null(host.Failure);
    }

    private static CancellationTokenSource TestDeadline()
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        return deadline;
    }

    private static async Task AssertNoAdditionalConnectionAsync(TestPacketRelay relay, CancellationToken ct)
    {
        using var observation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        observation.CancelAfter(TimeSpan.FromMilliseconds(250));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => relay.AcceptAsync(observation.Token).AsTask());
        ct.ThrowIfCancellationRequested();
    }

    private static async Task AssertTransferAsync(TcpClient peer, TestPacketRelay.Connection connection, CancellationToken ct)
    {
        await peer.GetStream().WriteAsync(new byte[] { 42 }, ct);
        var buffer = new byte[1];
        var packet = await connection.Socket.ReceiveAsync(buffer, ct);
        Assert.Equal(1, packet.Count);
        Assert.Equal(42, buffer[0]);
    }
}
