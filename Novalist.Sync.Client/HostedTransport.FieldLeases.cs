using Microsoft.AspNetCore.SignalR.Client;

namespace Novalist.Sync.Client;

public sealed partial class HostedTransport
{
    public async Task<FieldLeaseGrant> AcquireFieldLeaseAsync(FieldLeaseRequest request, CancellationToken ct) =>
        ValidateLease(await hub.InvokeAsync<FieldLeaseGrant>("AcquireFieldLease", request, ct));

    public async Task<FieldLeaseGrant?> RenewFieldLeaseAsync(FieldLeaseRequest request, CancellationToken ct)
    {
        var grant = await hub.InvokeAsync<FieldLeaseGrant?>("RenewFieldLease", request, ct);
        return grant is null ? null : ValidateLease(grant);
    }

    public Task<bool> ReleaseFieldLeaseAsync(FieldLeaseRequest request, CancellationToken ct) =>
        hub.InvokeAsync<bool>("ReleaseFieldLease", request, ct);

    public Task<long> PushFieldEditAsync(FieldLeaseRequest request, UpdateEnvelope envelope, CancellationToken ct) =>
        request.RequiredLease is { } required
            ? hub.InvokeAsync<long>("PushGuardedFieldEdit", request with { RequiredLease = null },
                new FieldLeaseRequest(request.ProjectId, required.ResourceId, request.Epoch, required.LeaseId), envelope, ct)
            : hub.InvokeAsync<long>("PushFieldEdit", request, envelope, ct);

    private static FieldLeaseGrant ValidateLease(FieldLeaseGrant grant)
    {
        if (grant is null || grant.DeviceId == Guid.Empty || grant.LeaseId == Guid.Empty || grant.ServerSeq < 0 ||
            grant.ExpiresAt <= grant.ServerTime || grant.ExpiresAt - grant.ServerTime > TimeSpan.FromSeconds(30))
            throw new InvalidDataException("The server returned an invalid field editing lease.");
        return grant;
    }
}
