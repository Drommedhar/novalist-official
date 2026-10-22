using System.Net;
using System.Security.Cryptography;
using Novalist.Sync.Client;
using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class SteamPeerTests
{
    [Fact]
    public void SteamInvitationPreservesPrivateProjectCredentialsWithoutPublishingAnIpAddress()
    {
        var invitation = new PeerInvitation(new("127.0.0.1", 49152, Guid.NewGuid(), Guid.NewGuid(), 0,
            RandomNumberGenerator.GetBytes(32)), RandomNumberGenerator.GetBytes(32), DateTimeOffset.UtcNow.AddHours(1));
        var link = new SteamPeerInvitation(76561198000000001, invitation).ToLink();
        Assert.StartsWith("novalist://collaboration/steam#", link);
        var parsed = SteamPeerInvitation.Parse(link);
        Assert.Equal(76561198000000001UL, parsed.HostSteamId);
        Assert.Equal(invitation.Secret, parsed.Invitation.Secret);
        Assert.Equal(invitation.Address.CertificateHash, parsed.Invitation.Address.CertificateHash);
        Assert.Equal(IPAddress.Loopback.ToString(), parsed.Invitation.Address.Host);
        Assert.Throws<InvalidDataException>(() => SteamPeerInvitation.Parse(invitation.ToLink()));
        Assert.Throws<InvalidDataException>(() => new SteamPeerInvitation(0, invitation).ToLink());
    }

#if !NOVALIST_STEAMWORKS
    [Fact]
    public async Task BuildsWithoutSteamExplainAvailabilityAndRejectStartingASteamSession()
    {
        Assert.False(SteamPeerRelay.Status().Available);
        Assert.NotEmpty(SteamPeerRelay.Status().Reason);
        await Assert.ThrowsAsync<NotSupportedException>(() => SteamPeerRelay.HostAsync(12345, CancellationToken.None));
        await Assert.ThrowsAsync<NotSupportedException>(() => SteamPeerRelay.ConnectAsync(76561198000000001, CancellationToken.None));
    }
#endif
}
