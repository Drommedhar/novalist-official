using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Novalist.Sync.Client.Tests;

internal sealed class TestPacketRelay : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly CancellationTokenSource stop = new();
    private readonly TaskCompletionSource<WebSocket> control = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Channel<Connection> connections = Channel.CreateUnbounded<Connection>();
    public Guid SessionId { get; } = Guid.NewGuid();
    public Uri Address => new(app.Urls.Single());
    public Guid RejectedRoute { get; set; }

    private TestPacketRelay()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        app = builder.Build();
        app.UseWebSockets();
        app.MapGet("/relay/v1/host", HostAsync);
        app.MapGet("/relay/v1/sessions/{sessionId:guid}/guest", context => ConnectAsync(context, Guid.Empty));
        app.MapGet("/relay/v1/sessions/{sessionId:guid}/routes/{routeId:guid}", ConnectAsync);
    }

    public static async Task<TestPacketRelay> StartAsync(CancellationToken ct)
    {
        var relay = new TestPacketRelay();
        try { await relay.app.StartAsync(ct); return relay; }
        catch { await relay.DisposeAsync(); throw; }
    }

    public async Task<Guid> RequestRouteAsync(CancellationToken ct)
    {
        var id = Guid.NewGuid();
        await RequestRouteAsync(id, ct);
        return id;
    }

    public async Task RequestRouteAsync(Guid id, CancellationToken ct)
    {
        var socket = await control.Task.WaitAsync(ct);
        await socket.SendAsync(Encoding.ASCII.GetBytes(id.ToString("N")), WebSocketMessageType.Text, true, ct);
    }

    public ValueTask<Connection> AcceptAsync(CancellationToken ct) => connections.Reader.ReadAsync(ct);

    private async Task HostAsync(HttpContext context)
    {
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, stop.Token);
        await socket.SendAsync(Encoding.ASCII.GetBytes(SessionId.ToString("N")), WebSocketMessageType.Text, true, lifetime.Token);
        control.SetResult(socket);
        await socket.ReceiveAsync(new byte[1], lifetime.Token);
    }

    private async Task ConnectAsync(HttpContext context, Guid routeId)
    {
        if (routeId != Guid.Empty && routeId == RejectedRoute)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, stop.Token);
        using var connection = new Connection(routeId, socket);
        await connections.Writer.WriteAsync(connection, lifetime.Token);
        try { await connection.Closed.WaitAsync(lifetime.Token); }
        finally { socket.Abort(); }
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        await app.DisposeAsync();
        stop.Dispose();
    }

    internal sealed class Connection(Guid routeId, WebSocket socket) : IDisposable
    {
        private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Guid RouteId { get; } = routeId;
        public WebSocket Socket { get; } = socket;
        public Task Closed => closed.Task;
        public void Dispose() => closed.TrySetResult();
    }
}
