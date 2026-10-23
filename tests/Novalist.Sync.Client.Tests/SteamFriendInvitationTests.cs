using System.Security.Cryptography;
using Novalist.Sync.Client;
using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class SteamFriendInvitationTests
{
    private static SteamPeerInvitation Invitation() => new(76561198000000001,
        new(new("127.0.0.1", 49152, Guid.NewGuid(), Guid.NewGuid(), 7, RandomNumberGenerator.GetBytes(32)),
            RandomNumberGenerator.GetBytes(32), DateTimeOffset.UtcNow.AddHours(1)));

    [Fact]
    public void FriendInvitationFitsSteamLimitAndPreservesSessionCredentials()
    {
        var original = Invitation();
        var text = SteamInviteConnectString.Encode(original);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(text) < 256);
        var parsed = SteamInviteConnectString.Decode(text);
        Assert.Equal(original.HostSteamId, parsed.HostSteamId);
        Assert.Equal(original.Invitation.Address.SessionId, parsed.Invitation.Address.SessionId);
        Assert.Equal(original.Invitation.Address.ProjectId, parsed.Invitation.Address.ProjectId);
        Assert.Equal(original.Invitation.Address.Epoch, parsed.Invitation.Address.Epoch);
        Assert.Equal(original.Invitation.Address.Port, parsed.Invitation.Address.Port);
        Assert.Equal(original.Invitation.Address.CertificateHash, parsed.Invitation.Address.CertificateHash);
        Assert.Equal(original.Invitation.Secret, parsed.Invitation.Secret);
        Assert.Equal(original.Invitation.ExpiresAt.ToUnixTimeSeconds(), parsed.Invitation.ExpiresAt.ToUnixTimeSeconds());
    }

    [Fact]
    public void InvitationsWaitForTheUserAndDismissalCannotClearANewerInvitation()
    {
        var inbox = new SteamInvitationInbox();
        var first = Invitation();
        var text = SteamInviteConnectString.Encode(first);
        inbox.Receive(text, first.HostSteamId, "Morgan");
        inbox.Receive(text, first.HostSteamId, "Morgan");
        var request = inbox.Pending!;
        Assert.Equal("Morgan", request.FriendName);
        Assert.Null(request.Error);
        Assert.NotNull(request.Link);
        inbox.Receive(SteamInviteConnectString.Encode(Invitation()));
        inbox.Dismiss(Guid.NewGuid());
        Assert.Equal(request, inbox.Pending);
        inbox.Dismiss(request.Id);
        Assert.NotEqual(request, inbox.Pending);
        inbox.Dismiss(request.Id);
        Assert.NotNull(inbox.Pending);
        inbox.Dismiss(inbox.Pending!.Id);
        Assert.Null(inbox.Pending);
    }

    [Fact]
    public void MalformedExpiredAndMismatchedInvitationsNeverBecomeJoinLinks()
    {
        var inbox = new SteamInvitationInbox();
        var invitation = Invitation();
        foreach (var (text, sender) in new[] {
            ("not an invitation", 0UL),
            (SteamInviteConnectString.Prefix + "broken", 0UL),
            (SteamInviteConnectString.Encode(invitation), invitation.HostSteamId + 1),
            (SteamInviteConnectString.Encode(invitation with { Invitation = invitation.Invitation with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) } }), 0UL)
        })
        {
            inbox.Receive(text, sender);
            Assert.Null(inbox.Pending!.Link);
            Assert.NotEmpty(inbox.Pending.Error!);
            inbox.Dismiss(inbox.Pending.Id);
        }
    }
}
