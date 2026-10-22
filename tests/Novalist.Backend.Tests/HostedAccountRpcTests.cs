using System.Text.Json;
using Nerdbank.Streams;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Sync.Client.Tests;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class HostedAccountRpcTests
{
    [Fact]
    public async Task DesktopSignInKeepsTokensPrivateAndSignOutClearsRememberedAccount()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var issuer = await TestIdentityProvider.StartAsync(ct);
        using var directory = new TempDir();
        var (backendStream, desktopStream) = FullDuplexStream.CreatePair();
        await using var host = new BackendHost(directory.Path);
        host.Attach(backendStream, backendStream);
        using var desktop = Connect(desktopStream, new DesktopPeer(issuer));
        var parameters = new { server = issuer.Authority };
        var account = await desktop.InvokeWithParameterObjectAsync<JsonElement>("collaboration/account/signIn", parameters, ct);
        Assert.Equal("Writer", account.GetProperty("displayName").GetString());
        Assert.Equal("writer", account.GetProperty("subject").GetString());
        Assert.DoesNotContain("refresh", account.GetRawText());
        Assert.DoesNotContain("access", account.GetRawText());
        var restored = await desktop.InvokeWithParameterObjectAsync<JsonElement>("collaboration/account/status", parameters, ct);
        Assert.Equal(account.GetRawText(), restored.GetRawText());
        await Assert.ThrowsAsync<RemoteMethodNotFoundException>(() => desktop.InvokeWithParameterObjectAsync("collaboration/credentials", new { action = "read", key = "account" }, ct));
        await desktop.InvokeWithParameterObjectAsync("collaboration/account/signOut", parameters, ct);
        Assert.Equal(JsonValueKind.Null, (await desktop.InvokeWithParameterObjectAsync<JsonElement>("collaboration/account/status", parameters, ct)).ValueKind);
    }

    [Fact]
    public async Task WaitingForBrowserDoesNotBlockSavesAndCanBeCancelled()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var issuer = await TestIdentityProvider.StartAsync(ct);
        using var directory = new TempDir();
        var (backendStream, desktopStream) = FullDuplexStream.CreatePair();
        await using var host = new BackendHost(directory.Path);
        host.Attach(backendStream, backendStream);
        var peer = new DesktopPeer(issuer) { HoldBrowser = true };
        using var desktop = Connect(desktopStream, peer);
        var signingIn = desktop.InvokeWithParameterObjectAsync<JsonElement>("collaboration/account/signIn", new { server = issuer.Authority }, ct);
        await peer.BrowserOpened.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.True(await desktop.InvokeAsync<bool>("system/barrier").WaitAsync(TimeSpan.FromSeconds(5), ct));
        await desktop.InvokeAsync("collaboration/account/cancel");
        await Assert.ThrowsAnyAsync<Exception>(() => signingIn);
    }

    private static JsonRpc Connect(Stream stream, DesktopPeer peer)
    {
        var formatter = new SystemTextJsonFormatter();
        formatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(stream, stream, formatter));
        rpc.AddLocalRpcTarget(peer);
        rpc.StartListening();
        return rpc;
    }

    private sealed class DesktopPeer(TestIdentityProvider issuer)
    {
        private readonly Dictionary<string, string> credentials = [];
        public bool HoldBrowser { get; init; }
        public TaskCompletionSource BrowserOpened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        [JsonRpcMethod("collaboration/openBrowser")]
        public async Task OpenBrowserAsync(Uri url, CancellationToken cancellationToken)
        {
            BrowserOpened.TrySetResult();
            if (!HoldBrowser) await issuer.OpenBrowserAsync(url, cancellationToken);
        }

        [JsonRpcMethod("collaboration/credentials")]
        public string? Credentials(string action, string key, string? value = null)
        {
            if (action == "read") return credentials.GetValueOrDefault(key);
            if (action == "remove") credentials.Remove(key);
            else if (action == "write") credentials[key] = value ?? throw new ArgumentException("Missing credential.");
            else throw new ArgumentException("Unknown credential operation.");
            return null;
        }
    }
}
