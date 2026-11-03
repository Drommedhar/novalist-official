namespace Novalist.Sync.Client;

public sealed record SteamRuntimeStatus(bool Available, string Reason, ulong SteamId = 0, string? DisplayName = null);

public abstract class SteamPeerRelay : IAsyncDisposable
{
    public abstract int LocalPort { get; }
    public abstract ulong SteamId { get; }
    public abstract Exception? Failure { get; protected set; }
    public abstract ValueTask DisposeAsync();

    public static void ShutdownRuntime()
    {
#if NOVALIST_STEAMWORKS
        SteamNativeRuntime.Shutdown();
#endif
    }

    public static async Task<(TcpPeerTransport Transport, SteamPeerRelay Relay)> JoinInvitationAsync(
        SteamPeerInvitation invitation, DeviceIdentity device, CancellationToken ct)
    {
        var relay = await ConnectAsync(invitation.HostSteamId, ct);
        try
        {
            var local = invitation.Invitation with
            {
                Address = invitation.Invitation.Address with { Host = "127.0.0.1", Port = relay.LocalPort }
            };
            return (await TcpPeerTransport.JoinInvitationAsync(local, device, ct), relay);
        }
        catch { await relay.DisposeAsync(); throw; }
    }

    public static SteamRuntimeStatus Status()
    {
#if NOVALIST_STEAMWORKS
        return SteamNativeRelay.Status();
#else
        return new(false, "This build does not include Steamworks. Use a Steam-enabled build or choose a hosted server or TCP.");
#endif
    }

    public static Task<SteamPeerRelay> HostAsync(int localPort, CancellationToken ct)
    {
#if NOVALIST_STEAMWORKS
        return SteamNativeRelay.CreateAsync(localPort, 0, ct);
#else
        return Task.FromException<SteamPeerRelay>(new NotSupportedException(Status().Reason));
#endif
    }

    public static Task<SteamPeerRelay> ConnectAsync(ulong hostSteamId, CancellationToken ct)
    {
#if NOVALIST_STEAMWORKS
        return SteamNativeRelay.CreateAsync(0, hostSteamId, ct);
#else
        return Task.FromException<SteamPeerRelay>(new NotSupportedException(Status().Reason));
#endif
    }
}
