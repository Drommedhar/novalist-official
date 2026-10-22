using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

namespace Novalist.Sync.Client;

internal static class RelayTunnelSocket
{
    public static async Task<ClientWebSocket> ConnectAsync(Uri server, string path,
        Func<CancellationToken, Task<string>> token, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var socket = new ClientWebSocket();
        try
        {
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(15);
            socket.Options.SetRequestHeader("Authorization", "Bearer " + await token(deadline.Token));
            var endpoint = new Uri(new Uri(server.AbsoluteUri.TrimEnd('/') + '/'), path);
            await socket.ConnectAsync(new UriBuilder(endpoint) { Scheme = server.Scheme == "https" ? "wss" : "ws" }.Uri, deadline.Token);
            return socket;
        }
        catch { socket.Dispose(); throw; }
    }

    public static async Task<Guid> ReceiveIdAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[64];
        var result = await socket.ReceiveAsync(buffer, ct);
        if (result.MessageType != WebSocketMessageType.Text || !result.EndOfMessage ||
            !Guid.TryParseExact(Encoding.ASCII.GetString(buffer, 0, result.Count), "N", out var id) || id == Guid.Empty)
            throw new InvalidDataException("The relay connection ended or returned an invalid route.");
        return id;
    }

    public static async Task TransferAsync(WebSocket socket, TcpClient tcp, CancellationToken ct)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var forward = ToSocketAsync(tcp.GetStream(), socket, lifetime.Token);
        var backward = ToStreamAsync(socket, tcp.GetStream(), lifetime.Token);
        try { await Task.WhenAny(forward, backward); }
        finally
        {
            await lifetime.CancelAsync();
            socket.Abort();
            tcp.Dispose();
        }
        try { await Task.WhenAll(forward, backward); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    private static async Task ToSocketAsync(Stream stream, WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(), ct);
            if (count == 0) return;
            await socket.SendAsync(buffer.AsMemory(0, count), WebSocketMessageType.Binary, true, ct);
        }
    }

    private static async Task ToStreamAsync(WebSocket socket, Stream stream, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var packet = await socket.ReceiveAsync(buffer.AsMemory(), ct);
            if (packet.MessageType == WebSocketMessageType.Close) return;
            if (packet.MessageType != WebSocketMessageType.Binary) throw new InvalidDataException("The relay returned a nonbinary packet.");
            await stream.WriteAsync(buffer.AsMemory(0, packet.Count), ct);
        }
    }
}
