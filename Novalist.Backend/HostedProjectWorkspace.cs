#if NOVALIST_COLLABORATION
using Novalist.Sync.Client;

namespace Novalist.Backend;

public sealed record HostedSessionDto(Guid SessionId, Guid ProjectId, string Server, string Connection, bool IsOwner, string? RecoveryPath = null, string Mode = "hosted");

internal sealed partial class HostedProjectWorkspace : IDisposable
{
    private readonly Workspace workspace;
    private readonly HostedAccounts accounts;
    private readonly DeviceIdentityStore devices;
    private readonly HostedProjectAccessStore accessStore;
    private readonly string storage;
    private readonly CancellationTokenSource stopping = new();
    private Runtime? current;
    internal bool IsActive => current is not null;
    internal HostedSessionDto? Status => current?.View();

    internal HostedProjectWorkspace(Workspace workspace, HostedAccounts accounts, ICollaborationCredentials credentials)
    {
        this.workspace = workspace;
        this.accounts = accounts;
        devices = new(credentials);
        accessStore = new(credentials);
        storage = Path.Combine(workspace.SettingsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Novalist"), "Collaboration");
    }

    internal async Task<HostedSessionDto> CreateAsync(Uri server, CancellationToken ct)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct, stopping.Token);
        ct = operation.Token;
        workspace.RequireNoSharedSession();
        var root = workspace.Projects.ProjectRoot ?? throw new InvalidOperationException("Open a project first.");
        var runtime = await PrepareAsync(server, null, ct);
        try
        {
            await using var archive = OpenArchive(runtime.Id);
            var states = await workspace.CaptureSharedProjectAsync(archive, ct);
            await archive.FlushAsync(ct);
            archive.Flush(flushToDisk: true);
            archive.Position = 0;
            await runtime.Session.StartAsync(createProject: true, ct);
            runtime.Checkpoint = await CheckpointTransfer.PublishAsync(runtime.Session.Updates, archive, ct);
            await RememberAsync(runtime, root, ct);
            await workspace.AttachSharedProjectAsync(runtime.Id, runtime.Session.Updates, states, archive, ct);
            current = runtime;
            return runtime.View();
        }
        catch { await runtime.DisposeAsync(); throw; }
    }

    internal async Task<HostedSessionDto> JoinAsync(Uri server, string link, string destination, CancellationToken ct)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct, stopping.Token);
        ct = operation.Token;
        workspace.RequireNoSharedSession();
        destination = Path.GetFullPath(destination);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new folder for the shared project.");
        var runtime = await PrepareAsync(server, link, ct);
        try
        {
            await runtime.Session.ConnectAsync(ct);
            await accessStore.ActivateAsync(runtime.Access, runtime.Session.Transport, ct);
            await runtime.Session.StartAsync(createProject: false, ct);
            runtime.Checkpoint = runtime.Access.Checkpoint ?? throw new InvalidDataException("The invitation has no project checkpoint.");
            await using var archive = OpenArchive(runtime.Id);
            var available = new DriveInfo(Path.GetPathRoot(destination) ?? throw new IOException("Choose a local project folder.")).AvailableFreeSpace;
            await CheckpointTransfer.DownloadAsync(runtime.Session.Updates, runtime.Checkpoint, archive, available, ct);
            await archive.FlushAsync(ct);
            archive.Flush(flushToDisk: true);
            archive.Position = 0;
            var states = await ProjectCheckpoint.InstallAsync(archive, destination, available, ct);
            await RememberAsync(runtime, destination, ct);
            await workspace.OpenProjectAsync(destination);
            await workspace.AttachSharedProjectAsync(runtime.Id, runtime.Session.Updates, states, archive, ct);
            current = runtime;
            return runtime.View();
        }
        catch { await runtime.DisposeAsync(); throw; }
    }

    internal async Task<string> InviteAsync(DateTimeOffset expiresAt, CancellationToken ct)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct, stopping.Token);
        var runtime = current ?? throw new InvalidOperationException("Start sharing a project first.");
        var checkpoint = runtime.Checkpoint ?? throw new InvalidOperationException("Publish the project before inviting someone.");
        return await HostedProjectInvitations.CreateAsync(runtime.Access, runtime.Device, runtime.Session.Transport, new(checkpoint, expiresAt), operation.Token);
    }

    internal async Task ReleaseAsync()
    {
        if (current is not { } runtime) return;
        await runtime.Session.EndAsync(CancellationToken.None);
        await runtime.DisposeAsync();
        current = null;
    }

    internal void StopPendingOperations() => stopping.Cancel();
    public void Dispose() => stopping.Dispose();

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

    private async Task<Runtime> PrepareAsync(Uri server, string? link, CancellationToken ct)
    {
        var account = await accounts.RestoreAsync(server, ct) ?? throw new InvalidOperationException("Sign in to the collaboration server first.");
        var device = await devices.GetOrCreateAsync(account.Authority, account.Subject, ct);
        HostedProjectAccess? access = null;
        try
        {
            access = link is null ? await accessStore.CreateOwnerAsync(server, device, ct) : await accessStore.PrepareJoinAsync(link, device, ct);
            if (access.Server != new Uri(server.AbsoluteUri.TrimEnd('/') + '/'))
                throw new InvalidOperationException("Sign in to the server named by this invitation.");
            var id = Guid.NewGuid();
            var session = await HostedProjectSession.OpenAsync(storage, id, access, device, () => account.GetAccessTokenAsync(CancellationToken.None));
            return new(id, device, access, session, account.DisplayName);
        }
        catch { access?.Dispose(); device.Dispose(); throw; }
    }

    private FileStream OpenArchive(Guid sessionId) => new(Path.Combine(storage, "sessions", sessionId.ToString("N"), "checkpoint.zip"),
        FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);

    private Task RememberAsync(Runtime runtime, string root, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var record = new SavedHostedProject(2, runtime.Id, runtime.Access.ProjectId, runtime.Access.Server,
            runtime.Device.Id, root, ReadProjectId(root),
            runtime.Checkpoint ?? throw new InvalidOperationException("The project checkpoint is missing."), runtime.RecoveryPath, runtime.Access.Epoch);
        return RememberAsync(record);
    }

    private sealed class Runtime(Guid id, DeviceIdentity device, HostedProjectAccess access, HostedProjectSession session, string name) : IAsyncDisposable
    {
        internal Guid Id => id;
        internal DeviceIdentity Device => device;
        internal HostedProjectAccess Access => access;
        internal HostedProjectSession Session => session;
        internal string Name => name;
        internal CheckpointReference? Checkpoint { get; set; }
        internal string? RecoveryPath { get; set; }
        internal HostedSessionDto View() => new(id, access.ProjectId, access.Server.AbsoluteUri, session.Status.State.ToString(), access.IsOwner, RecoveryPath);
        public async ValueTask DisposeAsync() { await session.DisposeAsync(); access.Dispose(); device.Dispose(); }
    }
}
#endif
