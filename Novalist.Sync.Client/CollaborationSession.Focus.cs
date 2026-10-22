using System.Diagnostics;

namespace Novalist.Sync.Client;

public sealed record FieldFocusResult(bool Acquired, Guid DeviceId, int RemainingMilliseconds, long ServerSeq);

public sealed partial class CollaborationSession
{
    public Task<FieldFocusResult> AcquireFieldAsync(FieldLeaseRequest request, CancellationToken ct) =>
        WithFieldTransportAsync(request, async (fields, token) =>
        {
            var started = Stopwatch.GetTimestamp();
            var grant = await fields.AcquireFieldLeaseAsync(request, token);
            if (grant.DeviceId == device && grant.LeaseId == request.LeaseId)
            {
                await foreach (var update in transport.CatchUpAsync(project, journal.LastServerSequence, token)) Receive(update);
                if (journal.LastServerSequence < grant.ServerSeq)
                    throw new InvalidDataException("The field's previous edits have not arrived yet.");
            }
            return FocusResult(request, grant, Stopwatch.GetElapsedTime(started));
        }, ct);

    public Task<FieldFocusResult?> RenewFieldAsync(FieldLeaseRequest request, CancellationToken ct) =>
        WithFieldTransportAsync<FieldFocusResult?>(request, async (fields, token) =>
        {
            var started = Stopwatch.GetTimestamp();
            var grant = await fields.RenewFieldLeaseAsync(request, token);
            if (grant is null) return null;
            var result = FocusResult(request, grant, Stopwatch.GetElapsedTime(started));
            if (!result.Acquired) throw new InvalidDataException("The server renewed a different field editing lease.");
            return result;
        }, ct);

    public Task<bool> ReleaseFieldAsync(FieldLeaseRequest request, CancellationToken ct) =>
        WithFieldTransportAsync(request, (fields, token) => fields.ReleaseFieldLeaseAsync(request, token), ct);

    private FieldFocusResult FocusResult(FieldLeaseRequest request, FieldLeaseGrant grant, TimeSpan elapsed)
    {
        var remaining = grant.ExpiresAt - grant.ServerTime - elapsed;
        if (grant.DeviceId == Guid.Empty || grant.LeaseId == Guid.Empty || grant.ServerSeq < 0 ||
            grant.ExpiresAt - grant.ServerTime > TimeSpan.FromSeconds(30))
            throw new InvalidDataException("The server returned an invalid field editing lease.");
        if (remaining.TotalMilliseconds < 1) throw new TimeoutException("The field editing lease expired before it was ready.");
        return new(grant.DeviceId == device && grant.LeaseId == request.LeaseId, grant.DeviceId,
            (int)remaining.TotalMilliseconds, grant.ServerSeq);
    }

    private async Task<T> WithFieldTransportAsync<T>(FieldLeaseRequest request,
        Func<IFieldLeaseTransport, CancellationToken, Task<T>> action, CancellationToken ct)
    {
        EnsureOpen();
        ValidateFieldLease(request);
        var fields = transport as IFieldLeaseTransport ?? throw new InvalidOperationException("This connection does not support field locking.");
        var operation = BeginFieldOperation(ct);
        try
        {
            await connectionGate.WaitAsync(operation.Token);
            try
            {
                EnsureOpen();
                if (!IsConnected || paused) throw new InvalidOperationException("Editing shared fields requires an active connection.");
                return await action(fields, operation.Token);
            }
            finally { connectionGate.Release(); }
        }
        finally { EndFieldOperation(operation); }
    }
}
