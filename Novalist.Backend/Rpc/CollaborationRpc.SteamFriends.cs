#if NOVALIST_COLLABORATION
using Novalist.Sync.Client;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class CollaborationRpc
{
    [JsonRpcMethod("collaboration/steam/friends")]
    public IReadOnlyList<SteamFriend> SteamFriends() => steamFriends.GetFriends();

    [JsonRpcMethod("collaboration/steam/inviteFriend")]
    public void InviteSteamFriend(string friendId) => steamFriends.Invite(friendId,
        SteamPeerInvitation.Parse(Peers().Invite(DateTimeOffset.UtcNow.AddHours(24))));

    [JsonRpcMethod("collaboration/steam/pendingInvitation")]
    public SteamJoinRequest? PendingSteamInvitation() => steamFriends.PendingInvitation();

    [JsonRpcMethod("collaboration/steam/dismissInvitation")]
    public void DismissSteamInvitation(Guid id) => steamFriends.DismissInvitation(id);

    [JsonRpcMethod("collaboration/steam/receiveInvitation")]
    public void ReceiveSteamInvitation(string connectString) => steamFriends.ReceiveLaunchInvitation(connectString);
}
#endif
