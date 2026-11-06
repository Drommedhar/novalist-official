#if NOVALIST_STEAMWORKS
using Steamworks;

namespace Novalist.Sync.Client;

internal static class SteamNativeRuntime
{
    internal static readonly object Gate = new();
    internal static readonly SteamInvitationInbox Invitations = new();
    private static SteamRuntimeStatus? status;
    private static Callback<GameRichPresenceJoinRequested_t>? joinRequested;

    internal static SteamRuntimeStatus Status()
    {
        lock (Gate)
        {
            if (status?.Available == true) return status;
            status = SteamNativeRelay.Initialize();
            if (!status.Available) return status;
            joinRequested = Callback<GameRichPresenceJoinRequested_t>.Create(request =>
                Invitations.Receive(request.m_rgchConnect, request.m_steamIDFriend.m_SteamID,
                    SteamFriends.GetFriendPersonaName(request.m_steamIDFriend)));
            SteamApps.GetLaunchCommandLine(out var commandLine, 4096);
            foreach (var argument in commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (argument.StartsWith(SteamInviteConnectString.Prefix, StringComparison.Ordinal))
                    Invitations.Receive(argument.Trim('"'));
            return status;
        }
    }

    internal static SteamJoinRequest? PendingInvitation()
    {
        lock (Gate)
        {
            if ((status ?? Status()).Available) SteamAPI.RunCallbacks();
            return Invitations.Pending;
        }
    }

    internal static void Shutdown()
    {
        lock (Gate)
        {
            joinRequested?.Dispose();
            joinRequested = null;
            if (status?.Available == true) SteamAPI.Shutdown();
            status = null;
        }
    }
}
#endif
