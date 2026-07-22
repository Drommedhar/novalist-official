using System.Text.Json;
using Nerdbank.Streams;
using Novalist.Backend;
using Novalist.Backend.Rpc;
using StreamJsonRpc;

namespace Novalist.Mobile.Services;

/// <summary>
/// Phase 0 seam proof. Stands up <see cref="BackendHost"/> in-process over an
/// in-memory <see cref="FullDuplexStream"/> pair - no child process, no stdio -
/// and round-trips system/ping. This is the same wiring the backend tests use;
/// running it on a real device confirms the shared C# core boots and answers RPC
/// inside the mobile sandbox.
/// </summary>
public static class SeamProbe
{
    public sealed record Result(bool Ok, string Detail);

    public static async Task<Result> PingAsync(string settingsDirectory)
    {
        try
        {
            var (serverStream, clientStream) = FullDuplexStream.CreatePair();
            using var host = new BackendHost(settingsDirectory);
            host.Attach(serverStream, serverStream);

            var formatter = new SystemTextJsonFormatter();
            formatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            using var client = new JsonRpc(
                new HeaderDelimitedMessageHandler(clientStream, clientStream, formatter));
            client.StartListening();

            var ping = await client.InvokeAsync<PingResult>("system/ping");
            return ping.Pong
                ? new Result(true, $"backend {ping.Version}")
                : new Result(false, "ping returned pong=false");
        }
        catch (Exception ex)
        {
            return new Result(false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
