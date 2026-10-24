namespace Novalist.Sync.Client;

public sealed record FieldLeaseCondition(Guid ResourceId, Guid LeaseId);
public sealed record FieldLeaseRequest(Guid ProjectId, Guid ResourceId, uint Epoch, Guid LeaseId, DateTimeOffset? AcquireBefore = null)
{
    public FieldLeaseCondition? RequiredLease { get; init; }
    public static FieldLeaseRequest Create(Guid projectId, Guid resourceId, uint epoch) =>
        new(projectId, resourceId, epoch, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(1));
}
public sealed record FieldLeaseGrant(Guid DeviceId, Guid LeaseId, DateTimeOffset ExpiresAt, DateTimeOffset ServerTime, long ServerSeq);

public interface IFieldLeaseTransport
{
    Task<FieldLeaseGrant> AcquireFieldLeaseAsync(FieldLeaseRequest request, CancellationToken ct);
    Task<FieldLeaseGrant?> RenewFieldLeaseAsync(FieldLeaseRequest request, CancellationToken ct);
    Task<bool> ReleaseFieldLeaseAsync(FieldLeaseRequest request, CancellationToken ct);
    Task<long> PushFieldEditAsync(FieldLeaseRequest request, UpdateEnvelope envelope, CancellationToken ct);
}
