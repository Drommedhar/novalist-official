using Xunit;
using Microsoft.AspNetCore.WebUtilities;
using System.Net;

namespace Novalist.Sync.Client.Tests;

public sealed class HostedSignInTests
{
    [Fact]
    public async Task SavedAccountRestoresAndRetainsRotatedCredentialsAcrossRestart()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var issuer = await TestIdentityProvider.StartAsync(ct);
        var credentials = new MemoryCredentials();
        await using (var accounts = new HostedAccounts(credentials))
        {
            var account = await accounts.SignInAsync(issuer.Authority, issuer.OpenBrowserAsync, ct);
            Assert.Equal("writer", account.Subject);
        }
        await using (var accounts = new HostedAccounts(credentials))
        {
            var account = await accounts.RestoreAsync(issuer.Authority, ct);
            Assert.NotNull(account);
            Assert.Equal("renewed-1", await account.GetAccessTokenAsync(ct));
            Assert.Equal(1, issuer.Refreshes);
        }
        await using (var accounts = new HostedAccounts(credentials))
        {
            var account = await accounts.RestoreAsync(issuer.Authority, ct);
            Assert.NotNull(account);
            Assert.Equal("renewed-2", await account.GetAccessTokenAsync(ct));
            await accounts.SignOutAsync(issuer.Authority, ct);
            Assert.Null(await accounts.RestoreAsync(issuer.Authority, ct));
        }
    }

    private sealed class MemoryCredentials : ICollaborationCredentials
    {
        private readonly Dictionary<string, string> entries = [];
        public Action<CancellationToken>? BeforeWrite { get; set; }
        public Func<string, Task>? BeforeOperation { get; set; }
        public async Task<string?> ReadAsync(string key, CancellationToken ct)
        {
            if (BeforeOperation is not null) await BeforeOperation("read");
            return entries.GetValueOrDefault(key);
        }
        public async Task WriteAsync(string key, string value, CancellationToken ct)
        {
            if (BeforeOperation is not null) await BeforeOperation("write");
            BeforeWrite?.Invoke(ct);
            ct.ThrowIfCancellationRequested();
            entries[key] = value;
        }
        public Task RemoveAsync(string key, CancellationToken ct) { entries.Remove(key); return Task.CompletedTask; }
    }

    [Fact]
    public async Task CancellationAfterRefreshCannotDiscardRotatedCredentials()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var issuer = await TestIdentityProvider.StartAsync(ct);
        issuer.AccessLifetime = 0;
        var credentials = new MemoryCredentials();
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await using (var accounts = new HostedAccounts(credentials))
        {
            var account = await accounts.SignInAsync(issuer.Authority, issuer.OpenBrowserAsync, ct);
            credentials.BeforeWrite = token => { cancelled.Cancel(); token.ThrowIfCancellationRequested(); };
            Assert.Equal("renewed-1", await account.GetAccessTokenAsync(cancelled.Token));
        }
        credentials.BeforeWrite = null;
        await using var reopened = new HostedAccounts(credentials);
        var restored = await reopened.RestoreAsync(issuer.Authority, ct);
        Assert.NotNull(restored);
        Assert.Equal("renewed-2", await restored.GetAccessTokenAsync(ct));
    }

    [Fact]
    public async Task ShutdownRetriesARefreshTokenSaveThatPreviouslyFailed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var issuer = await TestIdentityProvider.StartAsync(ct);
        issuer.AccessLifetime = 0;
        var credentials = new MemoryCredentials();
        var accounts = new HostedAccounts(credentials);
        var account = await accounts.SignInAsync(issuer.Authority, issuer.OpenBrowserAsync, ct);
        credentials.BeforeWrite = _ => throw new IOException("Credential store temporarily unavailable.");
        await Assert.ThrowsAsync<IOException>(() => account.GetAccessTokenAsync(ct));
        await Assert.ThrowsAsync<IOException>(() => accounts.DisposeAsync().AsTask());
        credentials.BeforeWrite = _ => throw new OperationCanceledException();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => accounts.DisposeAsync().AsTask());
        credentials.BeforeWrite = null;
        await accounts.DisposeAsync();
        await using var reopened = new HostedAccounts(credentials);
        var restored = await reopened.RestoreAsync(issuer.Authority, ct);
        Assert.NotNull(restored);
        Assert.Equal("renewed-2", await restored.GetAccessTokenAsync(ct));
    }

    [Fact]
    public async Task BrowserSignInValidatesIdentityAndExchangesPkceCodeForAccessToken()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var issuer = await TestIdentityProvider.StartAsync(ct);
        var settings = await HostedSignIn.DiscoverAsync(issuer.Authority, ct);
        Assert.Equal("novalist", settings.ClientId);
        await using var account = await HostedSignIn.SignInAsync(settings, issuer.OpenBrowserAsync, ct);
        Assert.Equal("writer", account.Subject);
        Assert.Equal(issuer.Authority, account.Authority);
        Assert.Equal("Writer", account.DisplayName);
        Assert.Equal("access", await account.GetAccessTokenAsync(ct));
        Assert.True(issuer.ExchangedWithPkce);
    }

    [Fact]
    public async Task ConcurrentRequestsRefreshAnExpiredTokenOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var issuer = await TestIdentityProvider.StartAsync(ct);
        issuer.AccessLifetime = 0;
        await using var account = await HostedSignIn.SignInAsync(new(issuer.Authority, "novalist", "openid offline_access"), issuer.OpenBrowserAsync, ct);
        var tokens = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => account.GetAccessTokenAsync(ct)));
        Assert.All(tokens, token => Assert.Equal("renewed-1", token));
        Assert.Equal(1, issuer.Refreshes);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("extra-audience")]
    [InlineData("expired")]
    [InlineData("unsigned")]
    [InlineData("nonce")]
    [InlineData("missing-nonce")]
    [InlineData("missing")]
    [InlineData("empty-subject")]
    public async Task UnverifiedIdentityCannotProduceASignedInAccount(string fault)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var issuer = await TestIdentityProvider.StartAsync(ct);
        issuer.TokenFault = fault;
        await Assert.ThrowsAsync<InvalidOperationException>(() => HostedSignIn.SignInAsync(new(issuer.Authority, "novalist", "openid"), issuer.OpenBrowserAsync, ct));
    }

    [Fact]
    public async Task UnrelatedCallbackCannotFinishSignInAndCancellationClosesTheListener()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var issuer = await TestIdentityProvider.StartAsync(ct);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var browser = new HttpClient();
        Uri? callback = null;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HostedSignIn.SignInAsync(new(issuer.Authority, "novalist", "openid"), async (url, token) =>
        {
            callback = new Uri(QueryHelpers.ParseQuery(url.Query)["redirect_uri"].ToString());
            using var rejected = await browser.GetAsync(new Uri(callback, "?state=unrelated&code=code"), token);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            cancelled.Cancel();
        }, cancelled.Token));
        Assert.NotNull(callback);
        await Assert.ThrowsAsync<HttpRequestException>(() => browser.GetAsync(callback, ct));
    }

    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    public async Task ShutdownWaitsForAccountStorageAndPreventsLatePublication(string operation)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var issuer = await TestIdentityProvider.StartAsync(ct);
        var credentials = new MemoryCredentials();
        await using (var previous = new HostedAccounts(credentials))
            await previous.SignInAsync(issuer.Authority, issuer.OpenBrowserAsync, ct);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        credentials.BeforeOperation = async action =>
        {
            if (action != operation) return;
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
        };
        await using var accounts = new HostedAccounts(credentials);
        Task pending = operation == "read" ? (Task)accounts.RestoreAsync(issuer.Authority, ct)
            : accounts.SignInAsync(issuer.Authority, issuer.OpenBrowserAsync, ct);
        await entered.Task.WaitAsync(ct);
        var closing = accounts.DisposeAsync().AsTask();
        Assert.False(closing.IsCompleted);
        release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await closing;
        await Assert.ThrowsAsync<ObjectDisposedException>(() => accounts.RestoreAsync(issuer.Authority, ct));
    }
}
