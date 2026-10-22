using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Novalist.Sync.Client;

internal sealed class PeerUpdateLog : IDisposable
{
    private readonly SqliteConnection db;

    public PeerUpdateLog(string storage, Guid session)
    {
        Directory.CreateDirectory(storage);
        db = new(new SqliteConnectionStringBuilder { DataSource = Path.Combine(storage, session.ToString("N") + ".sqlite"), Pooling = false }.ToString());
        try
        {
            db.Open();
            using var schema = Command("""
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=FULL;
                CREATE TABLE updates (sequence INTEGER PRIMARY KEY, device TEXT NOT NULL, sender_sequence INTEGER NOT NULL,
                    envelope BLOB NOT NULL, UNIQUE(device, sender_sequence));
                """);
            schema.ExecuteNonQuery();
        }
        catch { db.Dispose(); throw; }
    }

    public long LastSequence
    {
        get
        {
            using var query = Command("SELECT COALESCE(MAX(sequence), 0) FROM updates");
            return query.ExecuteScalar() as long? ?? throw new InvalidDataException("Missing peer log sequence.");
        }
    }

    public long? Find(UpdateHeader header, UpdateEnvelope envelope)
    {
        using var query = Command("SELECT sequence, envelope FROM updates WHERE device = $device AND sender_sequence = $sender",
            ("$device", header.SenderDeviceId.ToString("N")), ("$sender", checked((long)header.SenderSeq)));
        using var rows = query.ExecuteReader();
        if (!rows.Read()) return null;
        if (!rows.GetFieldValue<byte[]>(1).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(envelope)))
            throw new InvalidDataException("A peer update sequence was reused with different content.");
        return rows.GetInt64(0);
    }

    public long Append(UpdateHeader header, UpdateEnvelope envelope)
    {
        using var insert = Command("""
            INSERT INTO updates(device, sender_sequence, envelope) VALUES ($device, $sender, $envelope);
            SELECT last_insert_rowid();
            """, ("$device", header.SenderDeviceId.ToString("N")), ("$sender", checked((long)header.SenderSeq)),
            ("$envelope", JsonSerializer.SerializeToUtf8Bytes(envelope)));
        return insert.ExecuteScalar() as long? ?? throw new InvalidDataException("Missing committed peer sequence.");
    }

    public IReadOnlyList<SequencedUpdate> Read(long after)
    {
        using var query = Command("SELECT sequence, envelope FROM updates WHERE sequence > $after ORDER BY sequence LIMIT 16", ("$after", after));
        using var rows = query.ExecuteReader();
        var result = new List<SequencedUpdate>();
        while (rows.Read()) result.Add(new(rows.GetInt64(0), JsonSerializer.Deserialize<UpdateEnvelope>(rows.GetFieldValue<byte[]>(1))
            ?? throw new InvalidDataException("Missing peer update.")));
        return result;
    }

    private SqliteCommand Command(string sql, params (string Name, object Value)[] parameters)
    {
        var command = db.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }

    public void Dispose() => db.Dispose();
}
