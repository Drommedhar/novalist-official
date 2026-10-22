namespace Novalist.Sync.Client;

public sealed record FieldLeaseRequest(Guid ProjectId, Guid ResourceId, uint Epoch, Guid LeaseId);
public sealed record FieldLeaseGrant(Guid DeviceId, Guid LeaseId, DateTimeOffset ExpiresAt, DateTimeOffset ServerTime, long ServerSeq);

public interface IFieldLeaseTransport
{
    Task<FieldLeaseGrant> AcquireFieldLeaseAsync(FieldLeaseRequest request, CancellationToken ct);
    Task<FieldLeaseGrant?> RenewFieldLeaseAsync(FieldLeaseRequest request, CancellationToken ct);
    Task<bool> ReleaseFieldLeaseAsync(FieldLeaseRequest request, CancellationToken ct);
    Task<long> PushFieldEditAsync(FieldLeaseRequest request, UpdateEnvelope envelope, CancellationToken ct);
}
