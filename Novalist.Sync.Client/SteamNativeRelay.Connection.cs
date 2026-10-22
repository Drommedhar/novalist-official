#if NOVALIST_STEAMWORKS
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Steamworks;

namespace Novalist.Sync.Client;

internal sealed partial class SteamNativeRelay
{
    private sealed class Connection(HSteamNetConnection handle, TcpClient? socket, CancellationToken stopping) : IDisposable
    {
        internal HSteamNetConnection Handle => handle;
        internal TcpClient? Socket { get; set; } = socket;
        internal CancellationTokenSource Lifetime { get; } = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        internal TaskCompletionSource Connected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Channel<byte[]> Incoming { get; } = Channel.CreateBounded<byte[]>(64);
        internal Task Work { get; set; } = Task.CompletedTask;
        internal void Stop() { Lifetime.Cancel(); Incoming.Writer.TryComplete(); }
        public void Dispose() { Socket?.Dispose(); Lifetime.Dispose(); }
    }

    private async Task BridgeAsync(Connection connection)
    {
        var ct = connection.Lifetime.Token;
        try
        {
            await connection.Connected.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            if (connection.Socket is null)
            {
                connection.Socket = new TcpClient { NoDelay = true };
                await connection.Socket.ConnectAsync(IPAddress.Loopback, hostPort, ct);
            }
            var stream = connection.Socket.GetStream();
            var sending = ForwardToSteamAsync(connection, stream, ct);
            var receiving = ForwardToTcpAsync(connection, stream, ct);
            await Task.WhenAny(sending, receiving);
            connection.Stop();
            await Task.WhenAll(sending, receiving);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (OperationCanceledException error) { Failure = new IOException("Steam timed out sending session data.", error); }
        catch (Exception error) when (error is IOException or SocketException or TimeoutException) { Failure = error; }
        finally
        {
            lock (nativeGate)
            {
                connections.TryRemove(connection.Handle, out _);
                SteamNetworkingSockets.CloseConnection(connection.Handle, 0, "Session connection ended", false);
                connection.Dispose();
            }
        }
    }

    private async Task ForwardToSteamAsync(Connection connection, NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[49152];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, ct);
            if (count == 0) return;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            while (!Send(connection.Handle, buffer, count)) await Task.Delay(10, timeout.Token);
        }
    }

    private bool Send(HSteamNetConnection handle, byte[] data, int length)
    {
        var pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            lock (nativeGate)
            {
                var result = SteamNetworkingSockets.SendMessageToConnection(handle, pinned.AddrOfPinnedObject(), (uint)length,
                    Constants.k_nSteamNetworkingSend_ReliableNoNagle, out _);
                if (result == EResult.k_EResultLimitExceeded) return false;
                if (result != EResult.k_EResultOK) throw new IOException("Steam could not deliver a session packet: " + result);
                return true;
            }
        }
        finally { pinned.Free(); }
    }

    private static async Task ForwardToTcpAsync(Connection connection, NetworkStream stream, CancellationToken ct)
    {
        await foreach (var data in connection.Incoming.Reader.ReadAllAsync(ct)) await stream.WriteAsync(data, ct);
    }
}
#endif
