using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Novalist.Sync.Client;

public sealed class HostedProjectRotation(string storage, DeviceIdentity owner, HostedTransport transport)
{
    public async Task RotateAsync(HostedProjectAccess access, Guid removedMember,
        Func<long, CancellationToken, Task<Stream>> createCheckpoint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(createCheckpoint);
        access.CheckTransport(transport);
        if (!access.IsOwner) throw new UnauthorizedAccessException("Only the project owner can rotate sharing keys.");
        var rotation = Guid.NewGuid();
        try
        {
            var boundary = await transport.ReserveProjectEpochAsync(access.ProjectId, rotation, ct);
            if (boundary.Epoch != access.Epoch || boundary.LastServerSeq < 0)
                throw new InvalidDataException("The project's reserved encryption epoch changed. Refresh its keys before rotating.");
            await using var checkpoint = await createCheckpoint(boundary.LastServerSeq, ct);
            await CompleteAsync(access, removedMember, rotation, boundary.LastServerSeq, checkpoint, ct);
        }
        catch (Exception error)
        {
            await CancelFailedRotationAsync(rotation, error);
            throw;
        }
    }

    private async Task CancelFailedRotationAsync(Guid rotation, Exception failure)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await transport.CancelProjectEpochAsync(rotation, cleanup.Token); }
        catch (Exception cleanupFailure)
        {
            throw new AggregateException("Key rotation failed and its reservation could not be cancelled. The reservation will expire automatically.",
                failure, cleanupFailure);
        }
    }

    private async Task CompleteAsync(HostedProjectAccess access, Guid removedMember, Guid rotation,
        long observedSequence, Stream checkpoint, CancellationToken ct)
    {
        var devices = await transport.ListProjectDevicesAsync(access.ProjectId, ct);
        var previous = access.CreateSessionIdentity(owner).Membership;
        var retained = devices.Where(value => value.UserId != removedMember).Select(value =>
        {
            if (value.Epoch != access.Epoch) throw new CryptographicException("A receiving device has stale project authorization.");
            return previous.VerifyCertificate(value.DeviceId, value.Certificate);
        }).ToArray();
        await transport.BeginProjectEpochAsync(new(access.ProjectId, rotation, access.Epoch, observedSequence), ct);
        var nextMembership = new ProjectMembership(access.ProjectId, checked(access.Epoch + 1), owner.SigningKey.PublicKey);
        using var next = new HostedProjectAccess(access.State with { Epoch = checked(access.Epoch + 1), EpochKey = RandomNumberGenerator.GetBytes(32),
            Certificate = nextMembership.CreateOwnerCertificate(owner), FirstServerSequence = checked(observedSequence + 1) }, owner);
        var reference = await StageCheckpointAsync(rotation, next, checkpoint, ct);
        foreach (var device in retained)
        {
            var certificate = nextMembership.RenewCertificate(device, owner.SigningKey);
            await transport.StageProjectEpochGrantAsync(rotation, new(device.DeviceId, certificate,
                ProjectEpochGrant.Create(access, next, owner, device, certificate, reference)), ct);
        }
        await transport.CommitProjectEpochAsync(new(rotation, removedMember, []), ct);
    }

    private async Task<CheckpointReference> StageCheckpointAsync(Guid rotation, HostedProjectAccess access, Stream checkpoint, CancellationToken ct)
    {
        using var sequence = new DeviceSequence(Path.Combine(storage, "devices", owner.Id.ToString("N") + ".sqlite"), owner.Id);
        using var journal = new SessionJournal(Path.Combine(storage, "rotations", rotation.ToString("N"), "journal.sqlite"), rotation, access.ProjectId, sequence);
        using var cipher = access.CreateCipher();
        await using var staged = new StagedTransport(transport, rotation);
        await using var session = new CollaborationSession(new(access.ProjectId, owner.Id, access.Epoch), journal, cipher, access.CreateSessionIdentity(owner), staged);
        return await CheckpointTransfer.PublishAsync(session, checkpoint, ct);
    }

    private sealed class StagedTransport(HostedTransport hosted, Guid rotation) : IUpdateTransport
    {
        public event Func<SequencedUpdate, Task>? Received { add { } remove { } }
        public bool IsConnected => hosted.IsConnected;
        public string ConnectionIdentity => hosted.ConnectionIdentity + "/epoch/" + rotation;
        public Task ConnectAsync(CancellationToken ct) => Task.CompletedTask;
        public Task CreateProjectAsync(Guid project, CancellationToken ct) => throw new InvalidOperationException("A transition cannot create a project.");
        public Task JoinAsync(Guid project, CancellationToken ct) => Task.CompletedTask;
        public Task<long> PushAsync(UpdateEnvelope envelope, CancellationToken ct) => hosted.StageProjectEpochAsync(rotation, envelope, ct);
        public async IAsyncEnumerable<SequencedUpdate> CatchUpAsync(Guid project, long after, [EnumeratorCancellation] CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); await Task.CompletedTask; yield break; }
        public Task DisconnectAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
