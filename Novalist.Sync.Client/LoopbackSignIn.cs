using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Novalist.Sync.Client;

internal sealed class LoopbackSignIn : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly TaskCompletionSource<string> received = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? expectedState;
    public Uri Address => new(new Uri(app.Urls.Single()), "/callback");

    private LoopbackSignIn()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0);
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = 0;
            options.Limits.MaxRequestLineSize = 16 * 1024;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
        });
        app = builder.Build();
        app.MapGet("/callback", Accept);
    }

    public static async Task<LoopbackSignIn> StartAsync(CancellationToken ct)
    {
        var callback = new LoopbackSignIn();
        try { await callback.app.StartAsync(ct); return callback; }
        catch { await callback.DisposeAsync(); throw; }
    }

    public void Expect(string state) => expectedState = state;
    public Task<string> ReceiveAsync(CancellationToken ct) => received.Task.WaitAsync(ct);

    private IResult Accept(HttpContext context)
    {
        var query = context.Request.Query;
        if (expectedState is null || context.Request.Host.Value != Address.Authority ||
            query["state"].Count != 1 || query["state"] != expectedState || context.Request.QueryString.Value is not { } response)
            return Results.BadRequest();
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.ContentSecurityPolicy = "default-src 'none'";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.OnCompleted(() => { received.TrySetResult(response); return Task.CompletedTask; });
        return Results.Text("You can return to Novalist to finish signing in.");
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
