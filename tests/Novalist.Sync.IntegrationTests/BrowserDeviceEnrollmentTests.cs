using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Novalist.Sync.Client;
using Novalist.Sync.Client.Tests;
using NSubstitute;
using Xunit;

namespace Novalist.Sync.IntegrationTests;

public sealed class BrowserDeviceEnrollmentTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BrowserEnrollmentUsesFreshMfaWithoutAccessTokenKeyBinding(bool mfa)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = await TestIdentityProvider.StartAsync(ct);
        await using var server = new CommunityServerFixture(provider);
        await server.InitializeAsync();
        provider.IssueAccessToken = () => server.Token(provider.Subject);
        await using var account = await HostedSignIn.SignInAsync(new(provider.Authority, "novalist", "openid profile offline_access sync"),
            provider.OpenBrowserAsync, ct);
        Assert.False(new JsonWebTokenHandler().ReadJsonWebToken(await account.GetAccessTokenAsync(ct)).TryGetClaim("cnf", out _));
        var credentials = Substitute.For<ICollaborationCredentials>();
        credentials.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));
        using var device = await new DeviceIdentityStore(credentials).GetOrCreateAsync(provider.Authority, provider.Subject, ct);
        using var recovery = RecoveryDeviceIdentity.Derive(RandomNumberGenerator.GetBytes(32));
        provider.MultiFactorAuthentication = mfa;
        if (!mfa)
        {
            var error = await Assert.ThrowsAsync<HttpRequestException>(() => HostedDeviceEnrollment.EnrollAsync(server.Address,
                device, recovery, account, provider.OpenBrowserAsync, ct));
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, error.StatusCode);
            return;
        }
        await HostedDeviceEnrollment.EnrollAsync(server.Address, device, recovery, account, provider.OpenBrowserAsync, ct);
        Assert.Equal("0", provider.RequestedMaxAge);
        Assert.True(provider.ExchangedWithPkce);
        await using var transport = new HostedTransport(server.Address, device, () => account.GetAccessTokenAsync(ct));
        await transport.ConnectAsync(ct);
        await transport.CreateProjectAsync(Guid.NewGuid(), ct);
        Assert.True(transport.IsConnected);
    }
}
