using Microsoft.AspNetCore.SignalR;
using Novalist.Sync.Client;
using Xunit;

namespace Novalist.Sync.IntegrationTests;

public sealed class HostedInvitationTests(CommunityServerFixture server) : IClassFixture<CommunityServerFixture>
{
    [Fact]
    public async Task InvitedAccountCanJoinAfterOwnerDisconnects_WithoutDatabaseProvisioning()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = Guid.NewGuid();
        var ownerToken = server.Token(Guid.NewGuid().ToString());
        var guestToken = server.Token(Guid.NewGuid().ToString());
        await using var owner = new HostedTransport(server.Address, Guid.NewGuid(), () => Task.FromResult(ownerToken));
        await using var guest = new HostedTransport(server.Address, Guid.NewGuid(), () => Task.FromResult(guestToken));
        await owner.ConnectAsync(ct);
        await owner.CreateProjectAsync(project, ct);
        await guest.ConnectAsync(ct);
        await Assert.ThrowsAsync<HubException>(() => guest.JoinAsync(project, ct));
        var invitation = Guid.NewGuid();

        await owner.CreateInvitationAsync(project, invitation, DateTimeOffset.UtcNow.AddHours(1), ct);
        await owner.DisconnectAsync(ct);
        Assert.Equal(project, await guest.RedeemInvitationAsync(invitation, ct));
        await guest.JoinAsync(project, ct);
    }
}
