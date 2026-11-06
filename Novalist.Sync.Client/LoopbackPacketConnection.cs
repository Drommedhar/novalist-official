using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Novalist.Sync.Client;

public sealed class LoopbackPacketConnection : IAsyncDisposable
{
    public const int MaxPacketBytes = 64 * 1024;
    private readonly TcpClient client;
    private readonly NetworkStream stream;
    private readonly SemaphoreSlim sending = new(1);
    private readonly SemaphoreSlim receiving = new(1);

    public LoopbackPacketConnection(TcpClient client)
    {
        if (client.Client.RemoteEndPoint is not IPEndPoint remote || !IsLoopback(remote.Address) ||
            client.Client.LocalEndPoint is not IPEndPoint local || !IsLoopback(local.Address))
        {
            client.Dispose();
            throw new ArgumentException("The TCP test transport only accepts loopback connections.", nameof(client));
        }
        this.client = client;
        client.NoDelay = true;
        stream = client.GetStream();
    }

    public async Task SendAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
    {
        if (packet.IsEmpty || packet.Length > MaxPacketBytes) throw new ArgumentOutOfRangeException(nameof(packet));
        await sending.WaitAsync(cancellationToken);
        try
        {
            var header = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(header, packet.Length);
            await stream.WriteAsync(header, cancellationToken);
            await stream.WriteAsync(packet, cancellationToken);
        }
        catch
        {
            client.Dispose();
            throw;
        }
        finally { sending.Release(); }
    }

    public async Task<byte[]> ReceiveAsync(CancellationToken cancellationToken)
    {
        await receiving.WaitAsync(cancellationToken);
        try
        {
            var header = new byte[4];
            await stream.ReadExactlyAsync(header, cancellationToken);
            var length = BinaryPrimitives.ReadInt32BigEndian(header);
            if (length <= 0 || length > MaxPacketBytes) throw new InvalidDataException("Invalid collaboration packet length.");
            var packet = new byte[length];
            await stream.ReadExactlyAsync(packet, cancellationToken);
            return packet;
        }
        catch
        {
            client.Dispose();
            throw;
        }
        finally { receiving.Release(); }
    }

    private static bool IsLoopback(IPAddress address) => IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);

    public ValueTask DisposeAsync()
    {
        client.Dispose();
        return ValueTask.CompletedTask;
    }
}
