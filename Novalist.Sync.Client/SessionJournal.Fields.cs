using System.Text.Json;

namespace Novalist.Sync.Client;

public sealed record PendingFieldEdit(FieldLeaseRequest Lease, byte[] Payload, UpdateEnvelope Envelope);
public sealed record CommittedJournalPayload(Guid SenderDeviceId, byte[] Payload, long ServerSeq);

public sealed partial class SessionJournal
{
    public IReadOnlyList<UpdateEnvelope> AppendField(Guid item, IReadOnlyList<byte[]> payloads,
        FieldLeaseRequest lease, Func<ulong, byte[], UpdateEnvelope> seal)
    {
        if (lease.ProjectId != project || lease.ResourceId == Guid.Empty || lease.LeaseId == Guid.Empty)
            throw new ArgumentException("The editing lease does not belong to this project.", nameof(lease));
        return AppendCore(item, payloads, seal, lease);
    }

    private void StoreLease(UpdateHeader header, FieldLeaseRequest lease)
    {
        if (header.Epoch != lease.Epoch) throw new InvalidOperationException("The editing lease belongs to another epoch.");
        using var command = Command("INSERT INTO field_edits VALUES ($sequence, $resource, $epoch, $lease)",
            ("$sequence", (long)header.SenderSeq), ("$resource", lease.ResourceId.ToString("D")),
            ("$epoch", (long)lease.Epoch), ("$lease", lease.LeaseId.ToString("D")));
        command.ExecuteNonQuery();
        if (lease.RequiredLease is { } required)
        {
            using var condition = Command("INSERT INTO field_conditions VALUES ($sequence, $resource, $lease)",
                ("$sequence", (long)header.SenderSeq), ("$resource", required.ResourceId.ToString("D")), ("$lease", required.LeaseId.ToString("D")));
            condition.ExecuteNonQuery();
        }
    }

    internal byte[]? LatestFieldPayload(Guid item, FieldLeaseRequest lease)
    {
        lock (gate)
        {
            using var command = Command("""
                SELECT c.payload FROM changes c JOIN field_edits f ON c.sender_seq = f.sender_seq
                LEFT JOIN field_conditions guard ON c.sender_seq = guard.sender_seq
                WHERE c.sender = $device AND c.item = $item AND f.resource = $resource AND f.epoch = $epoch AND f.lease = $lease
                AND guard.resource IS $guardResource AND guard.lease IS $guardLease
                ORDER BY c.sender_seq DESC
                """, ("$device", device.ToString("D")), ("$item", item.ToString("D")),
                ("$resource", lease.ResourceId.ToString("D")), ("$epoch", (long)lease.Epoch), ("$lease", lease.LeaseId.ToString("D")),
                ("$guardResource", (object?)lease.RequiredLease?.ResourceId.ToString("D") ?? DBNull.Value),
                ("$guardLease", (object?)lease.RequiredLease?.LeaseId.ToString("D") ?? DBNull.Value));
            using var reader = command.ExecuteReader();
            var packets = new List<UpdatePacket>();
            while (reader.Read())
            {
                var packet = UpdatePacket.Parse((byte[])reader[0]);
                if (packets.Count > 0 && packet.MessageId != packets[0].MessageId) break;
                packets.Add(packet);
            }
            return UpdatePacket.Assemble(packets);
        }
    }

    public IReadOnlyList<PendingFieldEdit> PendingFieldEdits()
    {
        lock (gate)
        {
            using var command = Command("""
                SELECT f.resource, f.epoch, f.lease, c.payload, c.envelope, guard.resource, guard.lease FROM changes c
                JOIN field_edits f ON c.sender_seq = f.sender_seq
                LEFT JOIN field_conditions guard ON c.sender_seq = guard.sender_seq
                WHERE c.sender = $device AND c.pending = 1 ORDER BY c.sender_seq
                """, ("$device", device.ToString("D")));
            using var reader = command.ExecuteReader();
            var result = new List<PendingFieldEdit>();
            while (reader.Read()) result.Add(new(
                new(project, Guid.Parse(reader.GetString(0)), checked((uint)reader.GetInt64(1)), Guid.Parse(reader.GetString(2)))
                { RequiredLease = reader.IsDBNull(5) ? null : new(Guid.Parse(reader.GetString(5)), Guid.Parse(reader.GetString(6))) },
                (byte[])reader[3], JsonSerializer.Deserialize<UpdateEnvelope>((byte[])reader[4])
                    ?? throw new InvalidDataException("Invalid field edit in the local journal.")));
            return result;
        }
    }

    internal IReadOnlyList<Guid> CommittedItemsSince(long after, long through)
    {
        lock (gate)
        {
            using var command = Command("SELECT DISTINCT item FROM changes WHERE server_seq > $after AND server_seq <= $through",
                ("$after", after), ("$through", Math.Min(through, LastServerSequence)));
            using var reader = command.ExecuteReader();
            var result = new List<Guid>();
            while (reader.Read()) result.Add(Guid.Parse(reader.GetString(0)));
            return result;
        }
    }

    public IReadOnlyList<CommittedJournalPayload> ReadCommittedItemRecords(Guid item, long through = long.MaxValue)
    {
        lock (gate)
        {
            using var command = Command("""
                SELECT sender, payload, server_seq FROM changes
                WHERE item = $item AND server_seq <= $through ORDER BY server_seq
                """, ("$item", item.ToString("D")), ("$through", Math.Min(through, LastServerSequence)));
            using var reader = command.ExecuteReader();
            var result = new List<CommittedJournalPayload>();
            while (reader.Read()) result.Add(new(Guid.Parse(reader.GetString(0)), (byte[])reader[1], reader.GetInt64(2)));
            return result;
        }
    }
}
