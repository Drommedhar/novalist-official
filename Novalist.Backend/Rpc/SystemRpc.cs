using System.Reflection;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Process-level RPC surface: liveness, version, shutdown.</summary>
public sealed class SystemRpc
{
    private readonly Action _requestShutdown;

    public SystemRpc(Action requestShutdown)
    {
        _requestShutdown = requestShutdown;
    }

    [JsonRpcMethod("system/ping")]
    public PingResult Ping() => new(
        Pong: true,
        Version: ResolveVersion(typeof(SystemRpc).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion));

    /// <summary>
    /// Waits behind all earlier workspace requests in the serial dispatcher.
    /// The updater uses this as a persistence fence without waiting for
    /// unrelated long-running work that deliberately bypasses that dispatcher.
    /// </summary>
    [JsonRpcMethod("system/barrier")]
    public bool Barrier() => true;

    [JsonRpcMethod("system/shutdown")]
    public void Shutdown() => _requestShutdown();

    internal static string ResolveVersion(string? informationalVersion) =>
        string.IsNullOrEmpty(informationalVersion) ? "0.0.0" : informationalVersion;
}

public sealed record PingResult(bool Pong, string Version);
