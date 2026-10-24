using System.Net;
using System.Net.Sockets;
using Novalist.Sync.Client;
using Novalist.Sync.Client.Tests;
using Xunit;

namespace Novalist.Sync.IntegrationTests;

public sealed class RelayTokenRefreshTests
{
    [Fact]
    public async Task ClientRefreshTokensRenewTheExistingHostWithoutChangingItsSession()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var ct = deadline.Token;
        await using var server = new RelayServerFixture(authorizationGraceSeconds: 0);
        await using var provider = await TestIdentityProvider.StartAsync(ct);
        provider.AccessLifetime = 1;
        provider.RefreshLifetime = 2;
        provider.IssueAccessToken = () => server.Token("writer", DateTime.UtcNow.AddSeconds(provider.RefreshLifetime));
        await using var account = await HostedSignIn.SignInAsync(new(provider.Authority, "novalist", "openid profile offline_access sync"),
            provider.OpenBrowserAsync, ct);
        var renewed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<string> Token(CancellationToken cancellation)
        {
            var result = await account.GetAccessTokenAsync(cancellation);
            provider.RefreshLifetime = 300;
            if (provider.Refreshes > 1) renewed.TrySetResult();
            return result;
        }
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        await using var host = await RelayPeerTunnel.HostAsync(server.Address, ((IPEndPoint)listener.LocalEndpoint).Port, Token, ct);
        var session = host.SessionId;
        await renewed.Task.WaitAsync(ct);
        await Task.Delay(TimeSpan.FromSeconds(3), ct);
        await using var guest = await RelayPeerTunnel.ConnectAsync(server.Address, session, _ => Task.FromResult(server.Token("guest")), ct);
        using var guestClient = new TcpClient();
        await guestClient.ConnectAsync(IPAddress.Loopback, guest.LocalPort, ct);
        using var accepted = await listener.AcceptTcpClientAsync(ct);
        await guestClient.GetStream().WriteAsync(new byte[] { 42 }, ct);
        var received = new byte[1];
        await accepted.GetStream().ReadExactlyAsync(received, ct);
        Assert.Equal(42, received[0]);
        Assert.Equal(session, host.SessionId);
        Assert.Null(host.Failure);
        Assert.True(provider.Refreshes >= 2);
    }
}
