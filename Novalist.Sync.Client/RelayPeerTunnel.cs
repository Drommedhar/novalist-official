using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;

namespace Novalist.Sync.Client;

public sealed class RelayPeerTunnel : IAsyncDisposable
{
    private readonly Uri server;
    private readonly Func<CancellationToken, Task<string>>? token;
    private readonly string? hostSecret;
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentDictionary<long, Task> connections = new();
    private readonly ClientWebSocket? control;
    private readonly TcpListener? listener;
    private readonly Task running;
    private long nextConnection;
    private Exception? failure;
    private int disposed;

    public Guid SessionId { get; }
    public int LocalPort { get; }
    public Exception? Failure => Volatile.Read(ref failure);

    private RelayPeerTunnel(Uri server, Guid sessionId, int localPort,
        Func<CancellationToken, Task<string>>? token, ClientWebSocket socket, string? hostSecret = null)
    {
        this.server = server;
        this.token = token;
        this.hostSecret = hostSecret;
        SessionId = sessionId;
        if (localPort > 0)
        {
            LocalPort = localPort;
            control = socket;
            running = ObserveAsync(HostLoopAsync, stopOnFailure: true);
        }
        else
        {
            listener = new(IPAddress.Loopback, 0);
            listener.Start();
            LocalPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            running = ObserveAsync(ct => GuestLoopAsync(socket, ct), stopOnFailure: true);
        }
    }

    public static async Task<RelayPeerTunnel> HostAsync(Uri server, int localPort,
        Func<CancellationToken, Task<string>>? token, CancellationToken ct)
    {
        HostedSignIn.ValidateAddress(server);
        ArgumentOutOfRangeException.ThrowIfLessThan(localPort, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(localPort, 65535);
        var secret = token is null ? Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)) : null;
        var socket = await RelayTunnelSocket.ConnectAsync(server, "relay/v1/host", token, ct, secret);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            var id = await RelayTunnelSocket.ReceiveIdAsync(socket, deadline.Token);
            return new(server, id, localPort, token, socket, secret);
        }
        catch { socket.Dispose(); throw; }
    }

    public static async Task<RelayPeerTunnel> ConnectAsync(Uri server, Guid sessionId,
        Func<CancellationToken, Task<string>>? token, CancellationToken ct)
    {
        HostedSignIn.ValidateAddress(server);
        if (sessionId == Guid.Empty) throw new ArgumentException("A relay session is required.", nameof(sessionId));
        var socket = await RelayTunnelSocket.ConnectAsync(server, $"relay/v1/sessions/{sessionId:D}/guest", token, ct);
        try { return new(server, sessionId, 0, token, socket); }
        catch { socket.Dispose(); throw; }
    }

    private async Task HostLoopAsync(CancellationToken ct)
    {
        while (true)
        {
            var routeId = await RelayTunnelSocket.ReceiveIdAsync(control ?? throw new InvalidOperationException("This tunnel has no host control connection."), ct);
            Track(connectionToken => AcceptRouteAsync(routeId, connectionToken));
        }
    }

    private async Task AcceptRouteAsync(Guid routeId, CancellationToken ct)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, LocalPort, ct);
        using var socket = await RelayTunnelSocket.ConnectAsync(server,
            $"relay/v1/sessions/{SessionId:D}/routes/{routeId:D}", token, ct, hostSecret);
        await RelayTunnelSocket.TransferAsync(socket, tcp, ct);
    }

    private async Task GuestLoopAsync(ClientWebSocket initial, CancellationToken ct)
    {
        using (initial)
        {
            using var first = await (listener ?? throw new InvalidOperationException("This tunnel has no guest listener.")).AcceptTcpClientAsync(ct);
            Track(connectionToken => RelayTunnelSocket.TransferAsync(initial, first, connectionToken));
            await AcceptGuestsAsync(ct);
        }
    }

    private async Task AcceptGuestsAsync(CancellationToken ct)
    {
        while (true)
        {
            var tcp = await (listener ?? throw new InvalidOperationException("This tunnel has no guest listener.")).AcceptTcpClientAsync(ct);
            Track(async connectionToken =>
            {
                using (tcp)
                using (var socket = await RelayTunnelSocket.ConnectAsync(server,
                    $"relay/v1/sessions/{SessionId:D}/guest", token, connectionToken))
                    await RelayTunnelSocket.TransferAsync(socket, tcp, connectionToken);
            });
        }
    }

    private void Track(Func<CancellationToken, Task> operation)
    {
        var id = Interlocked.Increment(ref nextConnection);
        var task = ObserveAsync(operation, stopOnFailure: false);
        connections[id] = task;
        _ = task.ContinueWith(_ => connections.TryRemove(id, out var ignored), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task ObserveAsync(Func<CancellationToken, Task> operation, bool stopOnFailure)
    {
        try { await operation(stop.Token); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error)
        {
            // Peer reconnects replace failed routes without ending the relay session.
            if (!stopOnFailure) return;
            if (!stop.IsCancellationRequested) Interlocked.CompareExchange(ref failure, error, null);
            await stop.CancelAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        await stop.CancelAsync();
        listener?.Stop();
        control?.Abort();
        await running;
        await Task.WhenAll(connections.Values);
        control?.Dispose();
        stop.Dispose();
    }
}
