namespace Novalist.Sync.Client;

public sealed partial class TcpPeerTransport : IFieldLeaseTransport
{
    public async Task<FieldLeaseGrant> AcquireFieldLeaseAsync(FieldLeaseRequest request, CancellationToken ct) =>
        (await RequestAsync(new(NextId(), "field-acquire", request.ProjectId, Lease: request), ct)).Lease
        ?? throw new InvalidDataException("Missing peer field lease.");

    public async Task<FieldLeaseGrant?> RenewFieldLeaseAsync(FieldLeaseRequest request, CancellationToken ct) =>
        (await RequestAsync(new(NextId(), "field-renew", request.ProjectId, Lease: request), ct)).Lease;

    public async Task<bool> ReleaseFieldLeaseAsync(FieldLeaseRequest request, CancellationToken ct) =>
        (await RequestAsync(new(NextId(), "field-release", request.ProjectId, Lease: request), ct)).Released;

    public async Task<long> PushFieldEditAsync(FieldLeaseRequest request, UpdateEnvelope envelope, CancellationToken ct) =>
        (await RequestAsync(new(NextId(), request.RequiredLease is null ? "field-push" : "guarded-field-push", request.ProjectId, Envelope: envelope, Lease: request), ct)).Sequence;
}
