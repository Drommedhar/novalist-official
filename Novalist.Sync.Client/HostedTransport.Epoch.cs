using Microsoft.AspNetCore.SignalR.Client;

namespace Novalist.Sync.Client;

public sealed record HostedProjectDevice(Guid UserId, Guid DeviceId, uint Epoch, byte[] Certificate);
public sealed record HostedProjectEpoch(uint Epoch, long LastServerSeq);
public sealed record BeginHostedEpoch(Guid ProjectId, Guid RotationId, uint PreviousEpoch, long PreviousServerSeq);
public sealed record HostedEpochGrant(Guid DeviceId, byte[] Certificate, byte[] Payload);
public sealed record CommitHostedEpoch(Guid RotationId, Guid RemovedMemberId, IReadOnlyList<HostedEpochGrant> Grants);
public sealed record StoredHostedEpochGrant(uint Epoch, long FirstServerSeq, Guid GrantorDeviceId, byte[] Payload);

public sealed partial class HostedTransport
{
    private SessionContext? projectContext;
    public event Action<Guid, uint>? EpochChanged;
    internal void BindProjectEpoch(SessionContext context) => projectContext = context;
    private async Task CheckProjectEpochAsync(Guid project, CancellationToken ct)
    {
        if (projectContext is not { } expected || expected.ProjectId != project) return;
        var current = await GetProjectEpochAsync(project, ct);
        if (current.Epoch == expected.Epoch) return;
        if (current.Epoch < expected.Epoch) throw new InvalidDataException("The server returned an older project key version.");
        ready = false;
        EpochChanged?.Invoke(project, current.Epoch);
        throw new UnauthorizedAccessException("Shared access changed. Refresh the project keys to continue.");
    }

    public Task<HostedProjectEpoch> GetProjectEpochAsync(Guid project, CancellationToken ct) => hub.InvokeAsync<HostedProjectEpoch>("GetProjectEpoch", project, ct);
    public Task RegisterProjectDeviceAsync(Guid project, uint epoch, byte[] certificate, CancellationToken ct) =>
        hub.InvokeAsync("RegisterProjectDevice", project, epoch, certificate, ct);
    public Task<IReadOnlyList<HostedProjectDevice>> ListProjectDevicesAsync(Guid project, CancellationToken ct) =>
        hub.InvokeAsync<IReadOnlyList<HostedProjectDevice>>("ListProjectDevices", project, ct);
    public Task BeginProjectEpochAsync(BeginHostedEpoch request, CancellationToken ct) => hub.InvokeAsync("BeginProjectEpoch", request, ct);
    public Task<long> StageProjectEpochAsync(Guid rotation, UpdateEnvelope envelope, CancellationToken ct) => hub.InvokeAsync<long>("StageProjectEpoch", rotation, envelope, ct);
    public Task StageProjectEpochGrantAsync(Guid rotation, HostedEpochGrant grant, CancellationToken ct) => hub.InvokeAsync("StageProjectEpochGrant", rotation, grant, ct);
    public Task CommitProjectEpochAsync(CommitHostedEpoch request, CancellationToken ct) => hub.InvokeAsync("CommitProjectEpoch", request, ct);
    public Task<StoredHostedEpochGrant?> GetProjectEpochGrantAsync(Guid project, uint afterEpoch, CancellationToken ct) =>
        hub.InvokeAsync<StoredHostedEpochGrant?>("GetProjectEpochGrant", project, afterEpoch, ct);
}
