namespace Novalist.Sync.Client;

public sealed record PendingCompoundEdit(Guid ItemId, byte[] Payload, long AfterSequence, bool Confirmed = false);

public sealed partial class SessionJournal
{
    private void ConfirmCompoundEdit(UpdateHeader header, long serverSequence)
    {
        if (header.SenderDeviceId != device) return;
        using var command = Command("UPDATE compound_edits SET confirmed = 1 WHERE item = $item AND after_seq < $sequence",
            ("$item", header.ItemId.ToString("D")), ("$sequence", serverSequence));
        command.ExecuteNonQuery();
    }

    internal void SaveCompoundEdit(PendingCompoundEdit edit) => Atomic(() =>
    {
        EnsureOpen();
        using var command = Command("INSERT OR REPLACE INTO compound_edits VALUES ($item, $payload, $after, $confirmed)",
            ("$item", edit.ItemId.ToString("D")), ("$payload", edit.Payload), ("$after", edit.AfterSequence), ("$confirmed", edit.Confirmed ? 1 : 0));
        return command.ExecuteNonQuery();
    });

    internal IReadOnlyList<PendingCompoundEdit> PendingCompoundEdits()
    {
        lock (gate)
        {
            using var command = Command("SELECT item, payload, after_seq, confirmed FROM compound_edits ORDER BY after_seq, item");
            using var reader = command.ExecuteReader();
            var result = new List<PendingCompoundEdit>();
            while (reader.Read()) result.Add(new(Guid.Parse(reader.GetString(0)), (byte[])reader[1], reader.GetInt64(2), reader.GetInt64(3) != 0));
            return result;
        }
    }

    internal void CompleteCompoundEdit(Guid item) => Atomic(() =>
    {
        EnsureOpen();
        using var command = Command("DELETE FROM compound_edits WHERE item = $item", ("$item", item.ToString("D")));
        return command.ExecuteNonQuery();
    });
}
