#if NOVALIST_STEAMWORKS
using Steamworks;

namespace Novalist.Sync.Client;

internal sealed class SteamNativeFriendsClient : SteamFriendsClient
{
    public override IReadOnlyList<SteamFriend> GetFriends()
    {
        lock (SteamNativeRuntime.Gate)
        {
            RequireSteam();
            var friends = new List<SteamFriend>();
            var count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            for (var index = 0; index < count; index++)
            {
                var id = SteamFriends.GetFriendByIndex(index, EFriendFlags.k_EFriendFlagImmediate);
                friends.Add(new(id.m_SteamID.ToString(), SteamFriends.GetFriendPersonaName(id),
                    SteamFriends.GetFriendPersonaState(id) != EPersonaState.k_EPersonaStateOffline));
            }
            return friends.OrderByDescending(friend => friend.Online).ThenBy(friend => friend.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
    }

    public override void Invite(string friendId, SteamPeerInvitation invitation)
    {
        lock (SteamNativeRuntime.Gate)
        {
            var status = RequireSteam();
            if (invitation.HostSteamId != status.SteamId)
                throw new InvalidOperationException("Only the Steam host can invite friends.");
            if (!ulong.TryParse(friendId, out var id) || !SteamFriends.HasFriend(new CSteamID(id), EFriendFlags.k_EFriendFlagImmediate))
                throw new InvalidOperationException("Choose someone from your Steam friends list.");
            if (!SteamFriends.InviteUserToGame(new CSteamID(id), SteamInviteConnectString.Encode(invitation)))
                throw new IOException("Steam could not send the invitation. Check your Steam connection and try again.");
        }
    }

    public override SteamJoinRequest? PendingInvitation() => SteamNativeRuntime.PendingInvitation();
    public override void DismissInvitation(Guid id)
    {
        lock (SteamNativeRuntime.Gate) SteamNativeRuntime.Invitations.Dismiss(id);
    }
    public override void ReceiveLaunchInvitation(string connectString)
    {
        lock (SteamNativeRuntime.Gate) SteamNativeRuntime.Invitations.Receive(connectString);
    }

    private static SteamRuntimeStatus RequireSteam()
    {
        var status = SteamNativeRuntime.Status();
        if (!status.Available) throw new InvalidOperationException(status.Reason);
        return status;
    }
}
#endif
