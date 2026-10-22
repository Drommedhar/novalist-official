namespace Novalist.Sync.Client;

public sealed record CommittedUpdate(long ServerSeq, byte[] Payload);

public sealed partial class CollaborationSession
{
    private readonly object fieldOperationGate = new();
    private readonly object fieldDraftGate = new();
    private readonly HashSet<CancellationTokenSource> fieldOperations = [];
    private bool fieldsStopped;

    public Task CommitFieldAsync(Guid item, byte[] update, FieldLeaseRequest lease, CancellationToken ct) =>
        WriteFieldAsync(lease, (item, update.ToArray()), ct);

    public Task FlushFieldAsync(FieldLeaseRequest lease, CancellationToken ct) => WriteFieldAsync(lease, null, ct);

    private async Task WriteFieldAsync(FieldLeaseRequest lease, (Guid Item, byte[] Update)? edit, CancellationToken ct)
    {
        EnsureOpen();
        ValidateFieldLease(lease);
        if (transport is not IFieldLeaseTransport fields)
            throw new InvalidOperationException("This connection does not support field locking.");
        if (edit is { } value)
        {
            lock (fieldDraftGate)
                if (journal.LatestFieldPayload(value.Item, lease) is not { } previous || !previous.AsSpan().SequenceEqual(value.Update))
                    PrepareUpdate(value.Item, value.Update, lease);
        }
        var operation = BeginFieldOperation(ct);
        try
        {
            await connectionGate.WaitAsync(operation.Token);
            try
            {
                EnsureOpen();
                if (!IsConnected || paused) throw new InvalidOperationException("Editing shared fields requires an active connection.");
                foreach (var pending in journal.PendingFieldEdits().Where(pending => pending.Lease == lease))
                {
                    var sequence = await fields.PushFieldEditAsync(lease, pending.Envelope, operation.Token);
                    Acknowledge(pending.Envelope, sequence);
                }
                await foreach (var update in transport.CatchUpAsync(project, journal.LastServerSequence, operation.Token)) Receive(update);
            }
            finally { connectionGate.Release(); }
        }
        finally { EndFieldOperation(operation); }
    }

    private void ValidateFieldLease(FieldLeaseRequest lease)
    {
        if (lease.ProjectId != project || lease.Epoch != epoch || lease.ResourceId == Guid.Empty || lease.LeaseId == Guid.Empty)
            throw new ArgumentException("This field's editing lease belongs to another session context.", nameof(lease));
    }

    private void EndFieldOperation(CancellationTokenSource operation)
    {
        lock (fieldOperationGate) { fieldOperations.Remove(operation); operation.Dispose(); }
    }

    private CancellationTokenSource BeginFieldOperation(CancellationToken ct)
    {
        lock (fieldOperationGate)
        {
            var operation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            operation.CancelAfter(TimeSpan.FromSeconds(10));
            fieldOperations.Add(operation);
            if (fieldsStopped) operation.Cancel();
            return operation;
        }
    }

    private void StopFieldOperations()
    {
        lock (fieldOperationGate)
        {
            fieldsStopped = true;
            foreach (var operation in fieldOperations) operation.Cancel();
        }
    }

    public long CommittedSequence => journal.LastServerSequence;
    internal IReadOnlyList<Guid> CommittedItemsSince(long after, long through) => journal.CommittedItemsSince(after, through);

    public IReadOnlyList<CommittedUpdate> ReadCommittedItem(Guid item, long through = long.MaxValue) => journal.ReadCommittedItemRecords(item, through)
        .Select(record => (record.SenderDeviceId, record.ServerSeq, Packet: UpdatePacket.Parse(record.Payload)))
        .GroupBy(record => (record.SenderDeviceId, record.Packet.MessageId))
        .Select(group => (Sequence: group.Max(record => record.ServerSeq), Update: UpdatePacket.Assemble(group.Select(record => record.Packet))))
        .Where(value => value.Update is not null && !journal.IsRejectedUpdate(item, value.Update))
        .Select(value => new CommittedUpdate(value.Sequence, value.Update ?? throw new InvalidDataException("Incomplete confirmed field update.")))
        .OrderBy(value => value.ServerSeq).ToArray();
}
