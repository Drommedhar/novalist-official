namespace Novalist.Sync.Client;

public sealed record SteamFriend(string SteamId, string Name, bool Online);
public sealed record SteamJoinRequest(Guid Id, string? Link, string? FriendName, string? Error);

public interface ISteamFriendsClient
{
    IReadOnlyList<SteamFriend> GetFriends();
    void Invite(string friendId, SteamPeerInvitation invitation);
    SteamJoinRequest? PendingInvitation();
    void DismissInvitation(Guid id);
    void ReceiveLaunchInvitation(string connectString);
}

public class SteamFriendsClient : ISteamFriendsClient
{
    public static ISteamFriendsClient Create()
    {
#if NOVALIST_STEAMWORKS
        return new SteamNativeFriendsClient();
#else
        return new SteamFriendsClient();
#endif
    }

    public virtual IReadOnlyList<SteamFriend> GetFriends() => throw new NotSupportedException(SteamPeerRelay.Status().Reason);
    public virtual void Invite(string friendId, SteamPeerInvitation invitation) => throw new NotSupportedException(SteamPeerRelay.Status().Reason);
    public virtual SteamJoinRequest? PendingInvitation() => null;
    public virtual void DismissInvitation(Guid id) { }
    public virtual void ReceiveLaunchInvitation(string connectString) => throw new NotSupportedException(SteamPeerRelay.Status().Reason);
}
