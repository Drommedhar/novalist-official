using System.Security.Cryptography;
using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class RelayInvitationTests
{
    [Fact]
    public void InvitationPreservesPeerCertificateAndSecretAndBindsSelectedRelay()
    {
        var peer = new PeerInvitation(new("127.0.0.1", 12345, Guid.NewGuid(), Guid.NewGuid(), 0,
            RandomNumberGenerator.GetBytes(32)), RandomNumberGenerator.GetBytes(32), DateTimeOffset.UtcNow.AddHours(1));
        var invitation = new RelayPeerInvitation(new("https://relay.example/base"), Guid.NewGuid(), peer);
        var decoded = RelayPeerInvitation.Parse(invitation.ToLink(), new("https://relay.example/base/"));
        Assert.Equal(invitation.Server, decoded.Server);
        Assert.Equal(invitation.SessionId, decoded.SessionId);
        Assert.Equal(peer.ToLink(), decoded.Invitation.ToLink());
        Assert.Throws<InvalidDataException>(() => RelayPeerInvitation.Parse(invitation.ToLink(), new("https://another.example")));
        Assert.Throws<ArgumentException>(() => (invitation with { Server = new("http://relay.example") }).ToLink());
        Assert.Throws<InvalidDataException>(() => RelayPeerInvitation.Parse(peer.ToLink()));
        Assert.Throws<InvalidDataException>(() => (invitation with { SessionId = Guid.Empty }).ToLink());
    }
}
