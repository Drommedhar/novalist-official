using System.Text.Json;
using Nerdbank.Streams;
using Novalist.Core.Services;
using Novalist.Sync.Client.Tests;
using StreamJsonRpc;

namespace Novalist.Backend.Tests.TestHelpers;

internal sealed class HostedDesktop : IAsyncDisposable
{
    internal BackendHost Host { get; }
    internal JsonRpc Rpc { get; }
    internal CollaborationDocumentPeer Documents { get; } = new();
    internal Dictionary<string, string> Credentials { get; }

    internal HostedDesktop(string settings, TestIdentityProvider identity, Dictionary<string, string>? credentials = null, IFileService? fileService = null)
    {
        Credentials = credentials ?? [];
        Host = new BackendHost(settings, fileService: fileService);
        var (backend, desktop) = FullDuplexStream.CreatePair();
        Host.Attach(backend, backend);
        var formatter = new SystemTextJsonFormatter();
        formatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        Rpc = new(new HeaderDelimitedMessageHandler(desktop, desktop, formatter));
        Rpc.AddLocalRpcTarget(Documents);
        Rpc.AddLocalRpcTarget(new AccountPeer(identity, Credentials));
        Rpc.StartListening();
    }

    internal Task SignInAsync(Uri server) => Rpc.InvokeAsync("collaboration/account/signIn", server);

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        Rpc.Dispose();
    }

    private sealed class AccountPeer(TestIdentityProvider identity, Dictionary<string, string> credentials)
    {
        [JsonRpcMethod("collaboration/openBrowser")]
        public Task OpenBrowserAsync(Uri url, CancellationToken cancellationToken) => identity.OpenBrowserAsync(url, cancellationToken);

        [JsonRpcMethod("collaboration/credentials")]
        public string? Credential(string action, string key, string? value = null)
        {
            if (action == "read") return credentials.GetValueOrDefault(key);
            if (action == "remove") credentials.Remove(key);
            else if (action == "write") credentials[key] = value ?? throw new ArgumentException("Missing credential.");
            else throw new ArgumentException("Unknown credential operation.");
            return null;
        }
    }
}
