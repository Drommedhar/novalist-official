using System.Text.Json;
using Nerdbank.Streams;
using Novalist.Backend;
using Novalist.Backend.Rpc;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

public class BackendHostTests
{
    private static JsonRpc CreateClient(Stream duplex)
    {
        var formatter = new SystemTextJsonFormatter();
        formatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(duplex, duplex, formatter));
        rpc.StartListening();
        return rpc;
    }

    [Fact]
    public async Task Ping_RoundTrips_WithVersion()
    {
        var (serverStream, clientStream) = FullDuplexStream.CreatePair();
        using var host = new BackendHost();
        host.Attach(serverStream, serverStream);
        using var client = CreateClient(clientStream);

        var result = await client.InvokeAsync<PingResult>("system/ping");

        Assert.True(result.Pong);
        Assert.False(string.IsNullOrEmpty(result.Version));
    }

    [Fact]
    public async Task Shutdown_CompletesRunAsync()
    {
        var (serverStream, clientStream) = FullDuplexStream.CreatePair();
        using var host = new BackendHost();
        var runTask = host.RunAsync(serverStream, serverStream);
        using var client = CreateClient(clientStream);

        await client.InvokeAsync("system/shutdown");
        await runTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(host.IsShutdownRequested);
    }

    [Fact]
    public async Task PeerDisconnect_CompletesRunAsync()
    {
        var (serverStream, clientStream) = FullDuplexStream.CreatePair();
        using var host = new BackendHost();
        var runTask = host.RunAsync(serverStream, serverStream);
        using (var client = CreateClient(clientStream))
        {
            await client.InvokeAsync<PingResult>("system/ping");
        }

        await runTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(host.IsShutdownRequested);
    }

    [Fact]
    public void RequestShutdown_IsIdempotent()
    {
        using var host = new BackendHost();
        host.RequestShutdown();
        host.RequestShutdown();
        Assert.True(host.IsShutdownRequested);
    }

    [Fact]
    public void Dispose_WithoutAttach_DoesNotThrow()
    {
        var host = new BackendHost();
        host.Dispose();
    }

    [Fact]
    public void GuardStandardOutput_RoutesConsoleOutToError()
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            using var captured = new StringWriter();
            Console.SetError(captured);
            BackendHost.GuardStandardOutput();

            Console.Out.Write("stray");

            Assert.Equal("stray", captured.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [Theory]
    [InlineData(null, "0.0.0")]
    [InlineData("", "0.0.0")]
    [InlineData("1.13.0-dev", "1.13.0-dev")]
    public void ResolveVersion_FallsBackWhenMissing(string? informational, string expected)
    {
        Assert.Equal(expected, SystemRpc.ResolveVersion(informational));
    }
}
