#if NOVALIST_COLLABORATION
using System.Net;
using System.Security.Cryptography;
using Novalist.Sync.Client;

namespace Novalist.Backend;

internal sealed class PeerProjectWorkspace : IDisposable
{
    private readonly Workspace workspace;
    private readonly DeviceIdentityStore devices;
    private readonly string storage;
    private readonly CancellationTokenSource stopping = new();
    private Runtime? current;
    internal bool IsActive => current is not null;
    internal HostedSessionDto? Status => current?.View();

    internal PeerProjectWorkspace(Workspace workspace, ICollaborationCredentials credentials)
    {
        this.workspace = workspace;
        devices = new(credentials);
        storage = Path.Combine(workspace.SettingsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Novalist"), "Collaboration", "peer");
    }

    internal Task<HostedSessionDto> HostAsync(string address, int port, string name, CancellationToken ct) => HostCoreAsync(address, port, name, false, ct);
    internal Task<HostedSessionDto> HostSteamAsync(string name, CancellationToken ct) => HostCoreAsync("127.0.0.1", 0, name, true, ct);

    private async Task<HostedSessionDto> HostCoreAsync(string address, int port, string name, bool steam, CancellationToken ct)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct, stopping.Token);
        ct = operation.Token;
        workspace.RequireNoSharedSession();
        _ = workspace.Projects.ProjectRoot ?? throw new InvalidOperationException("Open a project first.");
        ValidateName(name);
        if (port is < 0 or > 65535 || Uri.CheckHostName(address) == UriHostNameType.Unknown)
            throw new ArgumentException("Choose a reachable host address and a valid listening port.");
        var device = await DeviceAsync(ct);
        TcpPeerHost? host = null;
        SteamPeerRelay? relay = null;
        PeerProjectAccess? access = null;
        Runtime? runtime = null;
        try
        {
            var epochKey = RandomNumberGenerator.GetBytes(32);
            try { host = await TcpPeerHost.StartAsync(new(steam ? IPAddress.Loopback : IPAddress.Any, port), Path.Combine(storage, "hosts"), new(Guid.NewGuid(), 0, epochKey, device), ct); }
            finally { CryptographicOperations.ZeroMemory(epochKey); }
            access = host.CreateOwnerAccess(address);
            if (steam) relay = await SteamPeerRelay.HostAsync(access.Address.Port, ct);
            var wire = new TcpPeerTransport(access, device);
            var id = Guid.NewGuid();
            var session = await PeerProjectSession.OpenAsync(storage, id, wire, device);
            runtime = new(id, device, session, host, address, name) { Relay = relay };
            await using var archive = OpenArchive(id);
            var states = await workspace.CaptureSharedProjectAsync(archive, ct);
            await archive.FlushAsync(ct);
            archive.Flush(flushToDisk: true);
            archive.Position = 0;
            await session.StartAsync(createProject: true, ct);
            runtime.Checkpoint = await CheckpointTransfer.PublishAsync(session.Updates, archive, ct);
            archive.Position = 0;
            await workspace.AttachSharedProjectAsync(id, session.Updates, states, archive, ct);
            current = runtime;
            return runtime.View();
        }
        catch
        {
            if (runtime is not null) await runtime.DisposeAsync();
            else
            {
                if (relay is not null) await relay.DisposeAsync();
                if (host is not null) await host.DisposeAsync();
                access?.Dispose();
                device.Dispose();
            }
            throw;
        }
    }

    internal Task<HostedSessionDto> JoinAsync(string link, string destination, string name, CancellationToken ct) =>
        JoinCoreAsync(PeerInvitation.Parse(link), 0, destination, name, ct);

    internal Task<HostedSessionDto> JoinSteamAsync(string link, string destination, string name, CancellationToken ct)
    {
        var invitation = SteamPeerInvitation.Parse(link);
        return JoinCoreAsync(invitation.Invitation, invitation.HostSteamId, destination, name, ct);
    }

    private async Task<HostedSessionDto> JoinCoreAsync(PeerInvitation invitation, ulong steamHost, string destination, string name, CancellationToken ct)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct, stopping.Token);
        ct = operation.Token;
        workspace.RequireNoSharedSession();
        ValidateName(name);
        var checkpoint = invitation.Checkpoint ?? throw new InvalidDataException("The invitation has no project checkpoint.");
        destination = Path.GetFullPath(destination);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new folder for the shared project.");
        var device = await DeviceAsync(ct);
        Runtime? runtime = null;
        SteamPeerRelay? relay = null;
        try
        {
            if (steamHost != 0)
            {
                relay = await SteamPeerRelay.ConnectAsync(steamHost, ct);
                invitation = invitation with { Address = invitation.Address with { Host = "127.0.0.1", Port = relay.LocalPort } };
            }
            var wire = await TcpPeerTransport.JoinInvitationAsync(invitation, device, ct);
            var id = Guid.NewGuid();
            var session = await PeerProjectSession.OpenAsync(storage, id, wire, device);
            runtime = new(id, device, session, null, invitation.Address.Host, name) { Checkpoint = checkpoint, Relay = relay };
            await session.StartAsync(createProject: false, ct);
            await using var archive = OpenArchive(id);
            var available = new DriveInfo(Path.GetPathRoot(destination) ?? throw new IOException("Choose a local project folder.")).AvailableFreeSpace;
            await CheckpointTransfer.DownloadAsync(session.Updates, checkpoint, archive, available, ct);
            await archive.FlushAsync(ct);
            archive.Flush(flushToDisk: true);
            archive.Position = 0;
            var states = await ProjectCheckpoint.InstallAsync(archive, destination, available, ct);
            await workspace.OpenProjectAsync(destination);
            archive.Position = 0;
            await workspace.AttachSharedProjectAsync(id, session.Updates, states, archive, ct);
            current = runtime;
            return runtime.View();
        }
        catch
        {
            if (runtime is not null) await runtime.DisposeAsync();
            else { if (relay is not null) await relay.DisposeAsync(); device.Dispose(); }
            throw;
        }
    }

    internal string Invite()
    {
        var runtime = current ?? throw new InvalidOperationException("Host a project before inviting someone.");
        var host = runtime.Host ?? throw new InvalidOperationException("Only the host can invite someone to this session.");
        var invitation = host.CreateInvitation(runtime.Address, runtime.Checkpoint);
        return runtime.Relay is { } relay ? new SteamPeerInvitation(relay.SteamId, invitation).ToLink() : invitation.ToLink();
    }

    internal async Task ReleaseAsync()
    {
        if (current is not { } runtime) return;
        await runtime.Session.EndAsync(CancellationToken.None);
        await runtime.DisposeAsync();
        current = null;
    }

    internal async Task PublishPresenceAsync(Guid sessionId, Guid itemId, Guid viewId, CursorSelection? selection, bool active, CancellationToken ct)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct, stopping.Token);
        operation.CancelAfter(TimeSpan.FromSeconds(10));
        var runtime = RequireSession(sessionId);
        await runtime.Session.Presence.PublishAsync(new(itemId, viewId, runtime.Name, selection, active), operation.Token);
    }

    internal IReadOnlyList<ParticipantPresence> Participants(Guid sessionId) => RequireSession(sessionId).Session.Presence.Snapshot();
    private Runtime RequireSession(Guid sessionId) => current is { } runtime && runtime.Id == sessionId
        ? runtime : throw new InvalidOperationException("This collaboration session is no longer active.");

    private Task<DeviceIdentity> DeviceAsync(CancellationToken ct) => devices.GetOrCreateAsync(new("https://novalist.invalid/peer-device"), "desktop", ct);
    private FileStream OpenArchive(Guid id) => new(Path.Combine(storage, "sessions", id.ToString("N"), "checkpoint.zip"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl))
            throw new ArgumentException("Enter a name for the other writers to see.", nameof(name));
    }

    internal void StopPendingOperations() => stopping.Cancel();
    public void Dispose() => stopping.Dispose();

    private sealed class Runtime(Guid id, DeviceIdentity device, PeerProjectSession session, TcpPeerHost? host, string address, string name) : IAsyncDisposable
    {
        internal Guid Id => id;
        internal PeerProjectSession Session => session;
        internal TcpPeerHost? Host => host;
        internal SteamPeerRelay? Relay { get; init; }
        internal string Address => address;
        internal string Name => name;
        internal CheckpointReference? Checkpoint { get; set; }
        internal HostedSessionDto View() => new(id, session.Transport.Access.ProjectId, Relay is null ? "tcp://" + address + ":" + session.Transport.Access.Address.Port : "Steam",
            session.Status.State.ToString(), host is not null, Mode: Relay is null ? "tcp" : "steam");
        public async ValueTask DisposeAsync()
        {
            await session.DisposeAsync();
            if (host is not null) await host.DisposeAsync();
            if (Relay is not null) await Relay.DisposeAsync();
            device.Dispose();
        }
    }
}
#endif
