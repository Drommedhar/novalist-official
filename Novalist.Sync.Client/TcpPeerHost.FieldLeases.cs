namespace Novalist.Sync.Client;

public sealed partial class TcpPeerHost
{
    private readonly Dictionary<Guid, FieldLeaseGrant> leases = [];
    private readonly HashSet<(Guid Device, Guid Lease)> leaseAttempts = [];

    private FieldLeaseRequest RequireLease(PeerRequest request)
    {
        var lease = request.Lease;
        if (lease is null || lease.ProjectId != project || lease.Epoch != epoch || lease.LeaseId == Guid.Empty || lease.ResourceId == Guid.Empty)
            throw new InvalidDataException("The field's editing lease belongs to another session context.");
        return lease;
    }

    private FieldLeaseGrant Acquire(PeerClient client, FieldLeaseRequest request)
    {
        var now = DateTimeOffset.UtcNow;
        var previous = leases.GetValueOrDefault(request.ResourceId);
        if (previous is not null && Owns(client, request, now)) return previous with { ServerTime = now, ServerSeq = log.LastSequence };
        if (!leaseAttempts.Add((client.Device, request.LeaseId)))
            throw new InvalidOperationException("A finished focus request needs a new lease identifier.");
        if (previous is not null && previous.ExpiresAt > now) return previous with { ServerTime = now, ServerSeq = log.LastSequence };
        var grant = new FieldLeaseGrant(client.Device, request.LeaseId, now.AddSeconds(30), now, log.LastSequence);
        leases[request.ResourceId] = grant;
        return grant;
    }

    private FieldLeaseGrant? Renew(PeerClient client, FieldLeaseRequest request)
    {
        var now = DateTimeOffset.UtcNow;
        if (!Owns(client, request, now)) return null;
        var grant = new FieldLeaseGrant(client.Device, request.LeaseId, now.AddSeconds(30), now, log.LastSequence);
        leases[request.ResourceId] = grant;
        return grant;
    }

    private bool Release(PeerClient client, FieldLeaseRequest request)
    {
        leaseAttempts.Add((client.Device, request.LeaseId));
        if (!Owns(client, request, DateTimeOffset.UtcNow)) return false;
        leases.Remove(request.ResourceId);
        return true;
    }

    private bool Owns(PeerClient client, FieldLeaseRequest request, DateTimeOffset now) =>
        leases.TryGetValue(request.ResourceId, out var grant) && grant.DeviceId == client.Device &&
        grant.LeaseId == request.LeaseId && grant.ExpiresAt > now;
}
