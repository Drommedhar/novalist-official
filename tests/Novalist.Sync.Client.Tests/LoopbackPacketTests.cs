using System.Net;
using System.Net.Sockets;
using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class LoopbackPacketTests
{
    [Fact]
    public async Task FragmentedAndBackToBackTcpWritesRemainDistinctPackets()
    {
        var ct = TestContext.Current.CancellationToken;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        using var peer = new TcpClient();
        var accept = listener.AcceptTcpClientAsync(ct).AsTask();
        await peer.ConnectAsync(endpoint.Address, endpoint.Port, ct);
        await using var receiver = new LoopbackPacketConnection(await accept);
        var stream = peer.GetStream();
        await stream.WriteAsync(new byte[] { 0, 0 }, ct);
        await stream.WriteAsync(new byte[] { 0, 3, 10 }, ct);
        await stream.WriteAsync(new byte[] { 20, 30, 0, 0, 0, 2, 40, 50 }, ct);
        Assert.Equal(new byte[] { 10, 20, 30 }, await receiver.ReceiveAsync(ct));
        Assert.Equal(new byte[] { 40, 50 }, await receiver.ReceiveAsync(ct));
        await receiver.SendAsync(new byte[] { 60, 70 }, ct);
        var reply = new byte[6];
        await stream.ReadExactlyAsync(reply, ct);
        Assert.Equal(new byte[] { 0, 0, 0, 2, 60, 70 }, reply);
    }
}
