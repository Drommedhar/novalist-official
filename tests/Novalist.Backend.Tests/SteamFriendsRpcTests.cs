using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Sync.Client;
using NSubstitute;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class SteamFriendsRpcTests
{
    [Fact]
    public void SteamFriendsAndPendingInvitationsUseTheDesktopServiceWithoutOpeningAProject()
    {
        using var temporary = new TempDir();
        using var workspace = new Workspace(temporary.Path);
        var service = Substitute.For<ISteamFriendsClient>();
        var friend = new SteamFriend("76561198000000001", "Morgan", true);
        var invitation = new SteamJoinRequest(Guid.NewGuid(), "private invitation", "Morgan", null);
        service.GetFriends().Returns(new[] { friend });
        service.PendingInvitation().Returns(invitation);
        var rpc = new CollaborationRpc(workspace, service);
        Assert.Equal(friend, Assert.Single(rpc.SteamFriends()));
        Assert.Equal(invitation, rpc.PendingSteamInvitation());
        rpc.ReceiveSteamInvitation("launch invitation");
        service.Received().ReceiveLaunchInvitation("launch invitation");
        rpc.DismissSteamInvitation(invitation.Id);
        service.Received().DismissInvitation(invitation.Id);
        Assert.Throws<InvalidOperationException>(() => rpc.InviteSteamFriend(friend.SteamId));
        service.DidNotReceive().Invite(Arg.Any<string>(), Arg.Any<SteamPeerInvitation>());
        Assert.Null(workspace.Projects.ProjectRoot);
    }
}
