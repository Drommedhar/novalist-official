using System.Net.WebSockets;
using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class RelayEndpointTests
{
    [Fact]
    public async Task MissingDesktopConfigurationExplainsWhichAddressToUse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var issuer = await TestIdentityProvider.StartAsync(ct);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            HostedSignIn.DiscoverAsync(new Uri(issuer.Authority, "/missing/"), ct));

        Assert.Contains("collaboration server address", error.Message);
        Assert.Contains("sign-in provider", error.Message);
    }

    [Fact]
    public async Task SigningInToAnIdentityProviderDoesNotMakeItARelayServer()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var issuer = await TestIdentityProvider.StartAsync(ct);
        var settings = await HostedSignIn.DiscoverAsync(issuer.Authority, ct);
        await using var account = await HostedSignIn.SignInAsync(settings, issuer.OpenBrowserAsync, ct);

        var error = await Assert.ThrowsAsync<WebSocketException>(() =>
            RelayPeerTunnel.HostAsync(issuer.Authority, 12345, account.GetAccessTokenAsync, ct));

        Assert.Contains("relay server address", error.Message);
        Assert.Contains("sign-in provider", error.Message);
    }
}
