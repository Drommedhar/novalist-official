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

    [JsonRpcMethod("system/shutdown")]
    public void Shutdown() => _requestShutdown();

    internal static string ResolveVersion(string? informationalVersion) =>
        string.IsNullOrEmpty(informationalVersion) ? "0.0.0" : informationalVersion;
}

public sealed record PingResult(bool Pong, string Version);
