#if NOVALIST_COLLABORATION
using StreamJsonRpc;
using Novalist.Sync.Client;

namespace Novalist.Backend.Rpc;

public sealed class CollaborationRpc
{
    private readonly Workspace workspace;

    public CollaborationRpc(Workspace workspace) => this.workspace = workspace;

    [JsonRpcMethod("collaboration/end")]
    public Task EndAsync() => workspace.DetachSharedScenesAsync();

    [JsonRpcMethod("collaboration/status")]
    public HostedSessionDto? Status() => workspace.HostedProjects?.Status ?? workspace.PeerProjects?.Status;

    [JsonRpcMethod("collaboration/saved")]
    public Task<SavedHostedProjectDto?> SavedAsync() => workspace.HostedProjects?.SavedAsync() ?? Task.FromResult<SavedHostedProjectDto?>(null);

    [JsonRpcMethod("collaboration/resume")]
    public Task<HostedSessionDto> ResumeAsync(CancellationToken cancellationToken = default) => Hosted().ResumeAsync(cancellationToken);

    [JsonRpcMethod("collaboration/create")]
    public Task<HostedSessionDto> CreateAsync(Uri server, CancellationToken cancellationToken = default) =>
        Hosted().CreateAsync(server, cancellationToken);

    [JsonRpcMethod("collaboration/join")]
    public Task<HostedSessionDto> JoinAsync(Uri server, string link, string destination, CancellationToken cancellationToken = default) =>
        Hosted().JoinAsync(server, link, destination, cancellationToken);

    [JsonRpcMethod("collaboration/invite")]
    public Task<string> InviteAsync(DateTimeOffset expiresAt, CancellationToken cancellationToken = default) =>
        workspace.PeerProjects?.IsActive == true ? Task.FromResult(workspace.PeerProjects.Invite()) : Hosted().InviteAsync(expiresAt, cancellationToken);

    [JsonRpcMethod("collaboration/peer/host")]
    public Task<HostedSessionDto> HostPeerAsync(string address, int port, string name, CancellationToken cancellationToken = default) =>
        Peers().HostAsync(address, port, name, cancellationToken);

    [JsonRpcMethod("collaboration/peer/join")]
    public Task<HostedSessionDto> JoinPeerAsync(string link, string destination, string name, CancellationToken cancellationToken = default) =>
        Peers().JoinAsync(link, destination, name, cancellationToken);

    private PeerProjectWorkspace Peers() => workspace.PeerProjects ?? throw new InvalidOperationException("Desktop collaboration is unavailable.");

    [JsonRpcMethod("collaboration/steam/status")]
    public SteamRuntimeStatus SteamStatus() => SteamPeerRelay.Status();

    [JsonRpcMethod("collaboration/steam/host")]
    public Task<HostedSessionDto> HostSteamAsync(string name, CancellationToken cancellationToken = default) => Peers().HostSteamAsync(name, cancellationToken);

    [JsonRpcMethod("collaboration/steam/join")]
    public Task<HostedSessionDto> JoinSteamAsync(string link, string destination, string name, CancellationToken cancellationToken = default) =>
        Peers().JoinSteamAsync(link, destination, name, cancellationToken);

    [JsonRpcMethod("collaboration/copy")]
    public Task CreateIndependentCopyAsync(string destination, CancellationToken cancellationToken = default) =>
        RetainedProjectCopies.CreateIndependentAsync(workspace.Projects.ProjectRoot
            ?? throw new InvalidOperationException("Open a project first."), destination, cancellationToken);

    private HostedProjectWorkspace Hosted() => workspace.HostedProjects ?? throw new InvalidOperationException("Desktop collaboration is unavailable.");

    [JsonRpcMethod("collaboration/acquireField")]
    public async Task<SharedFieldFocus> AcquireFieldAsync(Guid sessionId, Guid itemId, Guid focusId, CancellationToken cancellationToken = default)
    {
        var field = await workspace.RequireEditableSharedFieldAsync(sessionId, itemId, focusId);
        return await field.Fields.AcquireAsync(field.Lease, cancellationToken);
    }

    [JsonRpcMethod("collaboration/writeField")]
    public async Task WriteFieldAsync(Guid sessionId, Guid itemId, Guid focusId, string value, CancellationToken cancellationToken = default)
    {
        var field = await workspace.RequireEditableSharedFieldAsync(sessionId, itemId, focusId);
        await field.Fields.WriteAsync(field.Lease, value, cancellationToken);
    }

    [JsonRpcMethod("collaboration/renewField")]
    public async Task<FieldFocusResult?> RenewFieldAsync(Guid sessionId, Guid itemId, Guid focusId, CancellationToken cancellationToken = default)
    {
        var field = await workspace.RequireEditableSharedFieldAsync(sessionId, itemId, focusId);
        return await field.Session.RenewFieldAsync(field.Lease, cancellationToken);
    }

    [JsonRpcMethod("collaboration/recoverField")]
    public Task<string> RecoverFieldAsync(Guid sessionId, Guid itemId, Guid focusId, string value, CancellationToken cancellationToken = default) =>
        workspace.RecoverSharedFieldAsync(sessionId, itemId, focusId, value, cancellationToken);

    [JsonRpcMethod("collaboration/releaseField")]
    public Task<bool> ReleaseFieldAsync(Guid sessionId, Guid itemId, Guid focusId, CancellationToken cancellationToken = default)
    {
        var field = workspace.RequireSharedField(sessionId, itemId, focusId);
        return field.Session.ReleaseFieldAsync(field.Lease, cancellationToken);
    }

    [JsonRpcMethod("collaboration/presence")]
    public Task PublishPresenceAsync(Guid sessionId, Guid itemId, Guid viewId, CursorSelection? selection, bool active, CancellationToken cancellationToken = default) =>
        workspace.PeerProjects?.IsActive == true
            ? workspace.PeerProjects.PublishPresenceAsync(sessionId, itemId, viewId, selection, active, cancellationToken)
            : Hosted().PublishPresenceAsync(sessionId, itemId, viewId, selection, active, cancellationToken);

    [JsonRpcMethod("collaboration/participants")]
    public IReadOnlyList<ParticipantPresence> Participants(Guid sessionId) => workspace.PeerProjects?.IsActive == true
        ? workspace.PeerProjects.Participants(sessionId) : Hosted().Participants(sessionId);

    [JsonRpcMethod("collaboration/readScene")]
    public async Task<byte[]> ReadSceneAsync(Guid sessionId, Guid itemId, CancellationToken cancellationToken = default)
        => (await workspace.RequireSharedScenes(sessionId).ReadAsync(itemId, cancellationToken)).State;

    [JsonRpcMethod("collaboration/writeScene")]
    public Task WriteSceneAsync(Guid sessionId, Guid itemId, byte[] update, CancellationToken cancellationToken = default)
        => workspace.RequireSharedScenes(sessionId).WriteAsync(itemId, update, cancellationToken);
}
#endif
