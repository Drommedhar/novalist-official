using System.Text.Json;
using Novalist.Backend.Rpc;
using StreamJsonRpc;

namespace Novalist.Backend;

/// <summary>
/// Owns the JSON-RPC endpoint over a duplex stream pair and the lifetime of the
/// RPC facades. stdout framing is LSP-style (Content-Length headers) so the
/// renderer can use vscode-jsonrpc unchanged.
/// </summary>
public sealed class BackendHost : IDisposable
{
    private readonly TaskCompletionSource _shutdownRequested =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private JsonRpc? _rpc;

    /// <summary>Reroutes Console.Out to stderr so stray writes cannot corrupt RPC framing.</summary>
    public static void GuardStandardOutput()
    {
        Console.SetOut(Console.Error);
    }

    /// <summary>Attaches the RPC endpoint and all facades to the given streams. Does not block.</summary>
    public JsonRpc Attach(Stream sending, Stream receiving)
    {
        var formatter = new SystemTextJsonFormatter();
        formatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        var handler = new HeaderDelimitedMessageHandler(sending, receiving, formatter);
        var rpc = new JsonRpc(handler);
        rpc.AddLocalRpcTarget(new SystemRpc(RequestShutdown), new JsonRpcTargetOptions { DisposeOnDisconnect = false });
        rpc.StartListening();
        _rpc = rpc;
        return rpc;
    }

    /// <summary>Runs until the peer disconnects or a shutdown request arrives.</summary>
    public async Task RunAsync(Stream sending, Stream receiving)
    {
        var rpc = Attach(sending, receiving);
        await Task.WhenAny(rpc.Completion, _shutdownRequested.Task);
    }

    internal void RequestShutdown() => _shutdownRequested.TrySetResult();

    internal bool IsShutdownRequested => _shutdownRequested.Task.IsCompleted;

    public void Dispose()
    {
        _rpc?.Dispose();
    }
}
