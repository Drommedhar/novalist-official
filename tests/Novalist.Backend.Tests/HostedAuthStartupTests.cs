using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Sync.Client.Tests;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class HostedAuthStartupTests
{
    [Fact]
    public async Task TemporarilyUnavailableSignInPreservesTheAccountForRetry()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        var credentials = new Dictionary<string, string>();
        await using (var first = new HostedDesktop(directory.Combine("first"), identity, credentials))
            await first.SignInAsync(server.Address);
        identity.RefreshUnavailable = true;
        await using var restored = new HostedDesktop(directory.Combine("restored"), identity, credentials);

        await Assert.ThrowsAsync<RemoteInvocationException>(() => restored.Rpc.InvokeAsync<HostedAccountDto?>("collaboration/account/status", server.Address));
        Assert.NotEmpty(credentials);

        identity.RefreshUnavailable = false;
        Assert.Equal("Writer", (await restored.Rpc.InvokeAsync<HostedAccountDto>("collaboration/account/status", server.Address)).DisplayName);
    }

    [Fact]
    public async Task UnreachableSavedIdentityFailsBeforePreparingTheProject()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        var credentials = new Dictionary<string, string>();
        await using (var first = new HostedDesktop(directory.Combine("first"), identity, credentials))
            await first.SignInAsync(server.Address);
        await identity.StopAsync(ct);
        await using var owner = new HostedDesktop(directory.Combine("restored"), identity, credentials);
        await owner.Rpc.InvokeAsync("project/create", directory.Path, "Shared", "Book");

        await Assert.ThrowsAsync<RemoteInvocationException>(() => owner.Rpc.InvokeAsync("collaboration/create", server.Address));

        Assert.Empty(owner.Progress);
        Assert.Null(await owner.Rpc.InvokeAsync<HostedSessionDto?>("collaboration/status"));
        Assert.True(await owner.Rpc.InvokeAsync<bool>("system/barrier"));
    }

    [Fact]
    public async Task RejectedSavedSignInReturnsToSignedOutAndAllowsFreshSignIn()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        var credentials = new Dictionary<string, string>();
        await using (var first = new HostedDesktop(directory.Combine("first"), identity, credentials))
            await first.SignInAsync(server.Address);
        identity.RejectRefresh = true;
        await using var restored = new HostedDesktop(directory.Combine("restored"), identity, credentials);

        Assert.Null(await restored.Rpc.InvokeAsync<HostedAccountDto?>("collaboration/account/status", server.Address));

        await restored.SignInAsync(server.Address);
        Assert.Equal("Writer", (await restored.Rpc.InvokeAsync<HostedAccountDto>("collaboration/account/status", server.Address)).DisplayName);
    }
}
