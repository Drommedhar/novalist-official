#if NOVALIST_COLLABORATION
using System.Net;
using System.Security.Cryptography;
using Novalist.Sync.Client;

namespace Novalist.Backend;

public sealed record HostedSessionDto(Guid SessionId, Guid ProjectId, string Server, string Connection, bool IsOwner, string? RecoveryPath = null, string Mode = "hosted");
public sealed record SavedHostedProjectDto(string Server, Guid ProjectId, string? RecoveryPath);

internal sealed partial class PeerProjectWorkspace : IDisposable
{
    private readonly Workspace workspace;
    private readonly DeviceIdentityStore devices;
    private readonly HostedAccounts? accounts;
    private readonly ICollaborationCredentials credentials;
    private readonly string storage;
    private readonly CancellationTokenSource stopping = new();
    private readonly object operationGate = new();
    private CancellationTokenSource? pending;
    private Runtime? current;
    internal bool IsActive => current is not null;
    internal HostedSessionDto? Status => current?.View();

    internal PeerProjectWorkspace(Workspace workspace, ICollaborationCredentials credentials, HostedAccounts? accounts = null)
    {
        this.workspace = workspace;
        this.credentials = credentials;
        this.accounts = accounts;
        devices = new(credentials);
        storage = Path.Combine(workspace.SettingsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Novalist"), "Collaboration", "peer");
    }

    internal Task<HostedSessionDto> HostAsync(string address, int port, string name, CancellationToken ct) => HostCoreAsync(address, port, name, false, null, null, ct);
    internal Task<HostedSessionDto> HostSteamAsync(string name, CancellationToken ct) => HostCoreAsync("127.0.0.1", 0, name, true, null, null, ct);

    internal async Task<HostedSessionDto> CreateAsync(Uri server, CancellationToken ct, string? name = null)
    {
        var account = await AccountAsync(server, ct);
        return await HostCoreAsync("127.0.0.1", 0, account?.DisplayName ?? name ?? "", false, server, account, ct);
    }

    private async Task<HostedSessionDto> HostCoreAsync(string address, int port, string name, bool steam, Uri? server, HostedAccount? account, CancellationToken ct)
    {
        using var operation = BeginOperation(ct);
        ct = operation.Token;
        workspace.RequireNoSharedSession();
        _ = workspace.Projects.ProjectRoot ?? throw new InvalidOperationException("Open a project first.");
        ValidateName(name);
        if (port is < 0 or > 65535 || Uri.CheckHostName(address) == UriHostNameType.Unknown)
            throw new ArgumentException("Choose a reachable host address and a valid listening port.");
        if (account is not null) _ = await account.GetAccessTokenAsync(ct);
        var device = await DeviceAsync(account, ct);
        TcpPeerHost? host = null;
        PeerProjectAccess? access = null;
        Runtime? runtime = null;
        try
        {
            var epochKey = RandomNumberGenerator.GetBytes(32);
            var bindAddress = steam || server is not null ? IPAddress.Loopback : Uri.CheckHostName(address) == UriHostNameType.IPv6 ? IPAddress.IPv6Any : IPAddress.Any;
            try { host = await TcpPeerHost.StartAsync(new(bindAddress, port), Path.Combine(storage, "hosts"), new(Guid.NewGuid(), 0, epochKey, device), ct); }
            finally { CryptographicOperations.ZeroMemory(epochKey); }
            access = host.CreateOwnerAccess(address);
            var wire = new TcpPeerTransport(access, device);
            var id = Guid.NewGuid();
            var session = await PeerProjectSession.OpenAsync(storage, id, wire, device);
            runtime = new(id, device, session, host, address, name) { Server = server };
            if (steam) runtime.Steam = await SteamPeerRelay.HostAsync(access.Address.Port, ct);
            if (server is not null)
                runtime.Relay = await RelayPeerTunnel.HostAsync(server, access.Address.Port, account is null ? null : account.GetAccessTokenAsync, ct);
            await CaptureHostAsync(runtime, ct);
            var prepared = runtime;
            host.SynchronizeBootstrap = (transfer, token) => SynchronizeBootstrapAsync(prepared, transfer, token);
        }
        catch
        {
            await workspace.DetachSharedScenesAsync();
            if (runtime is not null) await runtime.DisposeAsync();
            else
            {
                if (host is not null) await host.DisposeAsync();
                access?.Dispose();
                device.Dispose();
            }
            throw;
        }
        current = runtime;
        return runtime.View();
    }

    private async Task CaptureHostAsync(Runtime runtime, CancellationToken ct)
    {
        await using var archive = OpenArchive(runtime.Id);
        var state = await workspace.CaptureSharedProjectAsync(archive, ct);
        await archive.FlushAsync(ct);
        archive.Flush(true);
        runtime.RequireHost().SetBootstrap(archive.Name, 0);
        await runtime.Session.StartAsync(createProject: true, ct);
        archive.Position = 0;
        await workspace.AttachSharedProjectAsync(runtime.Id, runtime.Session.Updates, state, archive, ct);
        var files = ProjectContentSelection.Read(workspace.Projects.ProjectRoot ?? throw new InvalidOperationException("The project is closed."));
        await workspace.ReportCollaborationProgressAsync(new("complete", files.Count, files.Count, 0, 0));
    }

    internal Task<HostedSessionDto> JoinAsync(string link, string destination, string name, CancellationToken ct) =>
        JoinCoreAsync(PeerInvitation.Parse(link), null, 0, destination, name, null, ct);

    internal Task<HostedSessionDto> JoinSteamAsync(string link, string destination, string name, CancellationToken ct)
    {
        var invitation = SteamPeerInvitation.Parse(link);
        return JoinCoreAsync(invitation.Invitation, null, invitation.HostSteamId, destination, name, null, ct);
    }

    internal async Task<HostedSessionDto> JoinAsync(Uri server, string link, string destination, CancellationToken ct, string? name = null)
    {
        var invitation = RelayPeerInvitation.Parse(link);
        if (new Uri(server.AbsoluteUri.TrimEnd('/') + '/') != new Uri(invitation.Server.AbsoluteUri.TrimEnd('/') + '/'))
            throw new InvalidOperationException("Connect to the server named by this invitation.");
        var account = await AccountAsync(server, ct);
        return await JoinCoreAsync(invitation.Invitation, invitation, 0, destination, account?.DisplayName ?? name ?? "", account, ct);
    }

    private async Task<HostedSessionDto> JoinCoreAsync(PeerInvitation invitation, RelayPeerInvitation? hosted, ulong steamHost, string destination, string name, HostedAccount? account, CancellationToken ct)
    {
        using var operation = BeginOperation(ct);
        ct = operation.Token;
        workspace.RequireNoSharedSession();
        ValidateName(name);
        destination = Path.GetFullPath(destination);
        var device = await DeviceAsync(account, ct);
        Runtime? runtime = null;
        SteamPeerRelay? steam = null;
        RelayPeerTunnel? relay = null;
        TcpPeerTransport? wire = null;
        try
        {
            var address = invitation.Address.Host;
            if (hosted is not null)
            {
                relay = await RelayPeerTunnel.ConnectAsync(hosted.Server, hosted.SessionId, account is null ? null : account.GetAccessTokenAsync, ct);
                invitation = invitation with { Address = invitation.Address with { Host = "127.0.0.1", Port = relay.LocalPort } };
            }
            if (steamHost == 0) wire = await TcpPeerTransport.JoinInvitationAsync(invitation, device, ct);
            else (wire, steam) = await SteamPeerRelay.JoinInvitationAsync(new(steamHost, invitation), device, ct);
            var id = Guid.NewGuid();
            await using var archive = OpenArchive(id);
            var progress = new CollaborationProgress(workspace);
            var state = await PeerBootstrapTransfer.DownloadAsync(wire, destination, archive, progress, ct);
            await progress.FlushAsync();
            var session = await PeerProjectSession.OpenAsync(storage, id, wire, device, wire.BootstrapSequence);
            runtime = new(id, device, session, null, address, name) { Steam = steam, Relay = relay, Server = hosted?.Server };
            await session.StartAsync(createProject: false, ct);
            await workspace.OpenProjectAsync(destination);
            archive.Position = 0;
            await workspace.AttachSharedProjectAsync(id, session.Updates, state, archive, ct);
            if (hosted is not null) await RememberAsync(hosted, destination, name, ct);
            await wire.CompleteBootstrapAsync(ct);
        }
        catch (Exception error)
        {
            await workspace.DetachSharedScenesAsync();
            if (runtime is not null) await runtime.DisposeAsync();
            else
            {
                if (wire is not null) { await wire.DisposeAsync(); wire.Access.Dispose(); }
                if (steam is not null) await steam.DisposeAsync();
                if (relay is not null) await relay.DisposeAsync();
                device.Dispose();
            }
            if (error is System.Net.WebSockets.WebSocketException or System.Net.Sockets.SocketException)
                throw new InvalidOperationException("Cannot reach this live session. Keep the host online and ask for a new invitation if the session has ended.", error);
            throw;
        }
        current = runtime;
        return runtime.View();
    }

    internal string Invite(DateTimeOffset expiresAt)
    {
        var runtime = current ?? throw new InvalidOperationException("Host a project before inviting someone.");
        var host = runtime.Host ?? throw new InvalidOperationException("Only the host can invite someone to this session.");
        var invitation = host.CreateInvitation(runtime.Address, expiresAt: expiresAt);
        if (runtime.Relay is { } relay && runtime.Server is { } server) return new RelayPeerInvitation(server, relay.SessionId, invitation).ToLink();
        return runtime.Steam is { } steam ? new SteamPeerInvitation(steam.SteamId, invitation).ToLink() : invitation.ToLink();
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

    private async Task<HostedAccount?> AccountAsync(Uri server, CancellationToken ct)
    {
        var configuration = await RelayServerDiscovery.DiscoverAsync(server, ct);
        if (!configuration.AuthenticationRequired) return null;
        if (accounts is null) throw new InvalidOperationException("Desktop collaboration is unavailable.");
        return await accounts.RestoreAsync(server, ct) ?? throw new InvalidOperationException("Sign in to the collaboration server first.");
    }

    private Task<DeviceIdentity> DeviceAsync(HostedAccount? account, CancellationToken ct) => devices.GetOrCreateAsync(
        account?.Authority ?? new("https://novalist.invalid/peer-device"), account?.Subject ?? "desktop", ct);

    private FileStream OpenArchive(Guid id)
    {
        var directory = Path.Combine(storage, "sessions", id.ToString("N"));
        Directory.CreateDirectory(directory);
        return new(Path.Combine(directory, "checkpoint.zip"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl))
            throw new ArgumentException("Enter a name for the other writers to see.", nameof(name));
    }

    internal void StopPendingOperations()
    {
        stopping.Cancel();
        current?.Host?.CancelBootstrap();
    }
    public void Dispose() => stopping.Dispose();

    private sealed class Runtime(Guid id, DeviceIdentity device, PeerProjectSession session, TcpPeerHost? host, string address, string name) : IAsyncDisposable
    {
        internal Guid Id => id;
        internal DeviceIdentity Device => device;
        internal PeerProjectSession Session => session;
        internal TcpPeerHost? Host => host;
        internal TcpPeerHost RequireHost() => host ?? throw new UnauthorizedAccessException("Only the host can manage this session.");
        internal SteamPeerRelay? Steam { get; set; }
        internal RelayPeerTunnel? Relay { get; set; }
        internal Uri? Server { get; init; }
        internal List<string> BootstrapArchives { get; } = [];
        internal string Address => address;
        internal string Name => name;
        internal HostedSessionDto View() => new(id, session.Transport.Access.ProjectId,
            Server is not null ? Server.AbsoluteUri.TrimEnd('/') + "/" : (Steam is null ? new UriBuilder("tcp", address, session.Transport.Access.Address.Port).Uri.GetLeftPart(UriPartial.Authority) : "Steam"),
            Relay?.Failure is null ? session.Status.State.ToString() : "Failed", host is not null,
            Mode: Server is not null ? "hosted" : Steam is null ? "tcp" : "steam");
        public async ValueTask DisposeAsync()
        {
            if (host is not null) host.SynchronizeBootstrap = null;
            await session.DisposeAsync();
            if (host is not null) await host.DisposeAsync();
            foreach (var path in BootstrapArchives) File.Delete(path);
            if (Steam is not null) await Steam.DisposeAsync();
            if (Relay is not null) await Relay.DisposeAsync();
            device.Dispose();
        }
    }
}
#endif
