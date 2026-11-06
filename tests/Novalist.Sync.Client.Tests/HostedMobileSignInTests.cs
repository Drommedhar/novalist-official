using System.Net;
using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class HostedMobileSignInTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeCallbackExchangesPkceAndRejectsChangedState(bool tamper)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var issuer = await TestIdentityProvider.StartAsync(ct);
        var settings = await HostedSignIn.DiscoverAsync(issuer.Authority, ct);
        using var browser = new NativeBrowser(tamper);
        if (tamper)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => HostedSignIn.SignInAsync(settings, browser, ct));
            Assert.False(issuer.ExchangedWithPkce);
        }
        else
        {
            await using var account = await HostedSignIn.SignInAsync(settings, browser, ct);
            Assert.Equal("writer", account.Subject);
            Assert.True(issuer.ExchangedWithPkce);
            Assert.Equal("access", await account.GetAccessTokenAsync(ct));
        }
    }

    private sealed class NativeBrowser(bool tamper) : IHostedSignInBrowser, IDisposable
    {
        private readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false });
        public Uri RedirectUri => new("com.novalist.app:/oauth/callback");

        public async Task<string> AuthenticateAsync(Uri url, CancellationToken ct)
        {
            using var response = await client.GetAsync(url, ct);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var callback = response.Headers.Location ?? throw new InvalidDataException("Missing callback.");
            return tamper ? RedirectUri + "?code=code&state=incorrect" : callback.AbsoluteUri;
        }

        public void Dispose() => client.Dispose();
    }
}
