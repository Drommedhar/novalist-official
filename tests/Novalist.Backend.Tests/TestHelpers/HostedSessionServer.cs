using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Novalist.Backend.Tests.TestHelpers;

internal sealed class HostedSessionServer : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly ConcurrentDictionary<Guid, Session> sessions = new();
    internal Uri Address => new(app.Urls.Single());
    internal bool RejectJoins { get; set; }
    internal int StorageRequests { get; private set; }

    private HostedSessionServer(Uri authority)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        app = builder.Build();
        app.UseWebSockets();
        app.MapGet("/client-configuration", () => new { protocolVersion = 1, authority = authority.AbsoluteUri, clientId = "novalist", scope = "openid profile sync offline_access" });
        app.Map("/relay/v1/host", context => HandleAsync(context, HostAsync));
        app.Map("/relay/v1/sessions/{id:guid}/guest", context => HandleAsync(context, GuestAsync));
        app.Map("/relay/v1/sessions/{id:guid}/routes/{route:guid}", context => HandleAsync(context, RouteAsync));
        app.MapFallback(context => { StorageRequests++; context.Response.StatusCode = 404; return Task.CompletedTask; });
    }

    internal static async Task<HostedSessionServer> StartAsync(Uri authority, CancellationToken ct)
    {
        var server = new HostedSessionServer(authority);
        try { await server.app.StartAsync(ct); return server; }
        catch { await server.DisposeAsync(); throw; }
    }

    private async Task HandleAsync(HttpContext context, Func<HttpContext, Task> action)
    {
        if (RejectJoins || !context.WebSockets.IsWebSocketRequest || !context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.Ordinal))
        { context.Response.StatusCode = 403; return; }
        try { await action(context); }
        catch (Exception error) when (error is WebSocketException or OperationCanceledException or IOException) { }
    }

    private async Task HostAsync(HttpContext context)
    {
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var session = new Session();
        var id = Guid.NewGuid();
        sessions[id] = session;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, session.Stop.Token);
        try
        {
            await SendIdAsync(socket, id, lifetime.Token);
            var send = SendRoutesAsync(socket, session, lifetime.Token);
            var receive = socket.ReceiveAsync(new byte[1], lifetime.Token);
            await Task.WhenAny(send, receive);
            await lifetime.CancelAsync();
            socket.Abort();
            await Task.WhenAll(send, receive);
        }
        finally { sessions.TryRemove(id, out _); session.Stop.Cancel(); }
    }

    private static async Task SendRoutesAsync(WebSocket socket, Session session, CancellationToken ct)
    {
        await foreach (var route in session.Pending.Reader.ReadAllAsync(ct)) await SendIdAsync(socket, route, ct);
    }

    private static Task SendIdAsync(WebSocket socket, Guid id, CancellationToken ct) =>
        socket.SendAsync(Encoding.ASCII.GetBytes(id.ToString("N")), WebSocketMessageType.Text, true, ct);

    private async Task GuestAsync(HttpContext context)
    {
        if (!sessions.TryGetValue(Guid.Parse((string)context.Request.RouteValues["id"]!), out var session))
        { context.Response.StatusCode = 404; return; }
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var route = new Route();
        var id = Guid.NewGuid();
        session.Routes[id] = route;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, session.Stop.Token);
        try
        {
            await session.Pending.Writer.WriteAsync(id, lifetime.Token);
            var host = await route.Host.Task.WaitAsync(lifetime.Token);
            var forward = CopyAsync(socket, host, lifetime.Token);
            var backward = CopyAsync(host, socket, lifetime.Token);
            await Task.WhenAny(forward, backward);
            await lifetime.CancelAsync();
            socket.Abort();
            host.Abort();
            await Task.WhenAll(forward, backward);
        }
        finally { session.Routes.TryRemove(id, out _); route.Closed.TrySetResult(); }
    }

    private async Task RouteAsync(HttpContext context)
    {
        if (!sessions.TryGetValue(Guid.Parse((string)context.Request.RouteValues["id"]!), out var session) ||
            !session.Routes.TryGetValue(Guid.Parse((string)context.Request.RouteValues["route"]!), out var route))
        { context.Response.StatusCode = 404; return; }
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        route.Host.TrySetResult(socket);
        await route.Closed.Task.WaitAsync(session.Stop.Token);
    }

    private static async Task CopyAsync(WebSocket source, WebSocket destination, CancellationToken ct)
    {
        var buffer = new byte[16384];
        while (true)
        {
            var message = await source.ReceiveAsync(buffer.AsMemory(), ct);
            if (message.MessageType == WebSocketMessageType.Close) return;
            await destination.SendAsync(buffer.AsMemory(0, message.Count), message.MessageType, message.EndOfMessage, ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in sessions.Values) session.Stop.Cancel();
        await app.DisposeAsync();
    }

    private sealed class Route
    {
        internal TaskCompletionSource<WebSocket> Host { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Session : IDisposable
    {
        internal CancellationTokenSource Stop { get; } = new();
        internal Channel<Guid> Pending { get; } = Channel.CreateUnbounded<Guid>();
        internal ConcurrentDictionary<Guid, Route> Routes { get; } = new();
        public void Dispose() { Stop.Cancel(); Stop.Dispose(); }
    }
}
