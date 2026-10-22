using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace Novalist.Sync.Client;

public sealed record JournalPayload(Guid SenderDeviceId, byte[] Payload);

public sealed partial class SessionJournal : IDisposable
{
    private readonly object gate = new();
    private readonly SqliteConnection db;
    private readonly Guid project;
    private readonly Guid device;
    private readonly DeviceSequence sequences;
    private SqliteTransaction? transaction;
    public Guid ProjectId => project;
    public Guid DeviceId => device;

    public bool IsClosed
    {
        get
        {
            lock (gate)
            {
                using var command = Command("SELECT closed FROM session WHERE id = 1");
                return (command.ExecuteScalar() as long? ?? throw new InvalidDataException("Missing journal session.")) != 0;
            }
        }
    }

    public SessionJournal(string path, Guid session, Guid project, DeviceSequence sequences)
    {
        if (session == Guid.Empty || project == Guid.Empty)
            throw new ArgumentException("Session, project and device identities are required.");
        this.project = project;
        this.sequences = sequences;
        device = sequences.DeviceId;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new ArgumentException("A journal file path is required.", nameof(path)));
        db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        try
        {
            db.Open();
            using (var command = Command("PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;")) command.ExecuteNonQuery();
            Initialize($"1/{session:D}/{project:D}/{device:D}");
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }

    private void Initialize(string identity) => Atomic(() =>
    {
        using var schema = Command("""
            CREATE TABLE IF NOT EXISTS session (id INTEGER PRIMARY KEY CHECK(id = 1), identity TEXT NOT NULL, closed INTEGER NOT NULL, transport TEXT);
            CREATE TABLE IF NOT EXISTS changes (
                id INTEGER PRIMARY KEY, item TEXT NOT NULL, sender TEXT NOT NULL, sender_seq INTEGER NOT NULL,
                payload BLOB NOT NULL, envelope BLOB NOT NULL, pending INTEGER NOT NULL,
                server_seq INTEGER UNIQUE, UNIQUE(sender, sender_seq));
            CREATE TABLE IF NOT EXISTS rejected_scene_updates (item TEXT NOT NULL, hash TEXT NOT NULL, PRIMARY KEY(item, hash));
            CREATE TABLE IF NOT EXISTS certificates (device TEXT PRIMARY KEY, certificate BLOB NOT NULL);
            CREATE TABLE IF NOT EXISTS authority (id INTEGER PRIMARY KEY CHECK(id = 1), value BLOB NOT NULL);
            CREATE TABLE IF NOT EXISTS checkpoint_base (id INTEGER PRIMARY KEY CHECK(id = 1), server_seq INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS field_edits (sender_seq INTEGER PRIMARY KEY, resource TEXT NOT NULL, epoch INTEGER NOT NULL, lease TEXT NOT NULL);
            INSERT OR IGNORE INTO session VALUES (1, $identity, 0, NULL);
            """, ("$identity", identity));
        schema.ExecuteNonQuery();
        using var check = Command("SELECT identity FROM session WHERE id = 1");
        if (!Equals(check.ExecuteScalar(), identity)) throw new InvalidOperationException("This journal belongs to another session.");
        return 0;
    });

    public void BindTransport(string identity) => Atomic(() =>
    {
        EnsureOpen();
        if (string.IsNullOrWhiteSpace(identity)) throw new ArgumentException("A transport identity is required.", nameof(identity));
        using var bind = Command("UPDATE session SET transport = $transport WHERE id = 1 AND (transport IS NULL OR transport = $transport)",
            ("$transport", identity));
        if (bind.ExecuteNonQuery() != 1) throw new InvalidOperationException("Close this session before starting another connection mode or server.");
        return 0;
    });

    public UpdateEnvelope Append(Guid item, byte[] payload, Func<ulong, UpdateEnvelope> seal) =>
        AppendMany(item, [payload], (sequence, _) => seal(sequence))[0];

    public IReadOnlyList<UpdateEnvelope> AppendMany(Guid item, IReadOnlyList<byte[]> payloads, Func<ulong, byte[], UpdateEnvelope> seal) =>
        AppendCore(item, payloads, seal, null);

    private IReadOnlyList<UpdateEnvelope> AppendCore(Guid item, IReadOnlyList<byte[]> payloads,
        Func<ulong, byte[], UpdateEnvelope> seal, FieldLeaseRequest? lease) => Atomic(() =>
    {
        EnsureOpen();
        if (item == Guid.Empty || payloads.Count == 0 || payloads.Any(payload => payload.Length == 0)) throw new ArgumentException("An item and update are required.");
        var envelopes = new List<UpdateEnvelope>();
        foreach (var payload in payloads)
        {
            var sequence = sequences.Next();
            var envelope = seal(sequence, payload);
            var header = UpdateHeader.Parse(envelope.Header);
            if (header.ProjectId != project || header.ItemId != item || header.SenderDeviceId != device || header.SenderSeq != sequence)
                throw new InvalidOperationException("The sealed update does not match this session.");
            Insert(header, payload, envelope, pending: true, serverSequence: null);
            if (lease is not null) StoreLease(header, lease);
            envelopes.Add(envelope);
        }
        return (IReadOnlyList<UpdateEnvelope>)envelopes;
    });

    public bool Receive(SequencedUpdate update, byte[] plaintext, byte[]? certificate = null) => Atomic(() =>
    {
        EnsureOpen();
        var header = UpdateHeader.Parse(update.Envelope.Header);
        if (header.ProjectId != project || update.ServerSeq <= 0) throw new InvalidOperationException("Invalid received update context.");
        if (certificate is not null) PinCertificate(header.SenderDeviceId, certificate);
        if (header.SenderDeviceId == device) sequences.Observe(header.SenderSeq);
        using var existing = Command("SELECT envelope FROM changes WHERE sender = $sender AND sender_seq = $seq",
            ("$sender", header.SenderDeviceId.ToString("D")), ("$seq", (long)header.SenderSeq));
        if (existing.ExecuteScalar() is byte[] bytes)
        {
            if (!bytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(update.Envelope)))
                throw new InvalidOperationException("A sender sequence was reused with different content.");
            SetAcknowledged(header, update.ServerSeq);
            return false;
        }
        Insert(header, plaintext, update.Envelope, pending: false, update.ServerSeq);
        return true;
    });

    private void PinCertificate(Guid sender, byte[] certificate)
    {
        using var read = Command("SELECT certificate FROM certificates WHERE device = $device", ("$device", sender.ToString("D")));
        if (read.ExecuteScalar() is not byte[] existing)
        {
            EnsureOpen();
            using var insert = Command("INSERT INTO certificates VALUES ($device, $certificate)",
                ("$device", sender.ToString("D")), ("$certificate", certificate));
            insert.ExecuteNonQuery();
        }
        else if (!existing.AsSpan().SequenceEqual(certificate))
            throw new CryptographicException("The device's project authorization changed.");
    }

    internal void BindAuthorization(byte[]? authority, byte[]? certificate) => Atomic(() =>
    {
        using var read = Command("SELECT value FROM authority WHERE id = 1");
        var existing = read.ExecuteScalar() as byte[];
        if (existing is not null && (authority is null || !existing.AsSpan().SequenceEqual(authority)))
            throw new CryptographicException("The session's pinned project authority changed.");
        if (authority is null) return 0;
        if (certificate is null) throw new ArgumentException("An authorized session requires a device certificate.", nameof(certificate));
        if (existing is null)
        {
            EnsureOpen();
            using var history = Command("SELECT COUNT(*) FROM changes");
            if (!Equals(history.ExecuteScalar(), 0L)) throw new CryptographicException("Existing history has no pinned project authority.");
            using var insert = Command("INSERT INTO authority VALUES (1, $value)", ("$value", authority));
            insert.ExecuteNonQuery();
        }
        PinCertificate(device, certificate);
        return 0;
    });

    public void Acknowledge(UpdateEnvelope envelope, long serverSequence) => Atomic(() =>
    {
        EnsureOpen();
        var header = UpdateHeader.Parse(envelope.Header);
        if (header.ProjectId != project || header.SenderDeviceId != device || serverSequence <= 0)
            throw new InvalidOperationException("Invalid update acknowledgement.");
        using var existing = Command("SELECT envelope FROM changes WHERE sender = $sender AND sender_seq = $seq",
            ("$sender", device.ToString("D")), ("$seq", (long)header.SenderSeq));
        if (existing.ExecuteScalar() is not byte[] bytes || !bytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(envelope)))
            throw new InvalidOperationException("The acknowledged update is not in the local journal.");
        SetAcknowledged(header, serverSequence);
        return 0;
    });

    private void SetAcknowledged(UpdateHeader header, long serverSequence)
    {
        using var command = Command("""
            UPDATE changes SET pending = 0, server_seq = $server
            WHERE sender = $sender AND sender_seq = $seq AND (server_seq IS NULL OR server_seq = $server)
            """, ("$server", serverSequence), ("$sender", header.SenderDeviceId.ToString("D")), ("$seq", (long)header.SenderSeq));
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("A stored update changed its server sequence.");
    }

    private void Insert(UpdateHeader header, byte[] payload, UpdateEnvelope envelope, bool pending, long? serverSequence)
    {
        using var command = Command("""
            INSERT INTO changes (item, sender, sender_seq, payload, envelope, pending, server_seq)
            VALUES ($item, $sender, $seq, $payload, $envelope, $pending, $server)
            """, ("$item", header.ItemId.ToString("D")), ("$sender", header.SenderDeviceId.ToString("D")),
            ("$seq", (long)header.SenderSeq), ("$payload", payload), ("$envelope", JsonSerializer.SerializeToUtf8Bytes(envelope)),
            ("$pending", pending ? 1 : 0), ("$server", serverSequence ?? (object)DBNull.Value));
        command.ExecuteNonQuery();
    }

    public long LastServerSequence
    {
        get
        {
            lock (gate)
            {
                using var command = Command("""
                    SELECT CASE WHEN NOT EXISTS (SELECT 1 FROM changes WHERE server_seq = COALESCE((SELECT server_seq FROM checkpoint_base), 0) + 1)
                    THEN COALESCE((SELECT server_seq FROM checkpoint_base), 0)
                    ELSE (SELECT MIN(server_seq) FROM changes c WHERE server_seq IS NOT NULL
                        AND NOT EXISTS (SELECT 1 FROM changes n WHERE n.server_seq = c.server_seq + 1)) END
                    """);
                return command.ExecuteScalar() as long? ?? throw new InvalidDataException("Missing journal sequence.");
            }
        }
    }

    internal void BindFirstServerSequence(long first) => Atomic(() =>
    {
        if (first < 1) throw new ArgumentOutOfRangeException(nameof(first));
        using var read = Command("SELECT server_seq FROM checkpoint_base WHERE id = 1");
        if (read.ExecuteScalar() is long existing)
        {
            if (existing != first - 1) throw new CryptographicException("The session checkpoint boundary changed.");
            return 0;
        }
        if (first > 1)
        {
            using var count = Command("SELECT COUNT(*) FROM changes");
            if (!Equals(count.ExecuteScalar(), 0L)) throw new CryptographicException("A checkpoint boundary requires a fresh journal.");
        }
        using var insert = Command("INSERT INTO checkpoint_base VALUES (1, $base)", ("$base", first - 1));
        insert.ExecuteNonQuery();
        return 0;
    });

    public IReadOnlyList<byte[]> ReadItem(Guid item) => ReadItemRecords(item).Select(record => record.Payload).ToArray();

    public void RejectUpdate(Guid item, string hash) => Atomic(() =>
    {
        if (hash.Length != 64 || hash.Any(value => !char.IsAsciiHexDigit(value))) throw new ArgumentException("Invalid update fingerprint.", nameof(hash));
        using var command = Command("INSERT OR IGNORE INTO rejected_scene_updates VALUES ($item, $hash)",
            ("$item", item.ToString("D")), ("$hash", hash.ToLowerInvariant()));
        return command.ExecuteNonQuery();
    });

    public bool IsRejectedUpdate(Guid item, byte[] update)
    {
        lock (gate)
        {
            using var command = Command("SELECT COUNT(*) FROM rejected_scene_updates WHERE item = $item AND hash = $hash",
                ("$item", item.ToString("D")), ("$hash", Convert.ToHexStringLower(SHA256.HashData(update))));
            return Equals(command.ExecuteScalar(), 1L);
        }
    }

    public IReadOnlyList<JournalPayload> ReadItemRecords(Guid item)
    {
        lock (gate)
        {
            using var command = Command("""
                SELECT sender, payload FROM changes c WHERE item = $item AND
                    (pending = 0 OR sender != $device OR NOT EXISTS (SELECT 1 FROM field_edits f WHERE f.sender_seq = c.sender_seq))
                ORDER BY id
                """, ("$item", item.ToString("D")), ("$device", device.ToString("D")));
            using var reader = command.ExecuteReader();
            var updates = new List<JournalPayload>();
            while (reader.Read()) updates.Add(new(Guid.Parse(reader.GetString(0)), (byte[])reader[1]));
            return updates;
        }
    }

    public IReadOnlyList<UpdateEnvelope> Pending()
    {
        lock (gate)
        {
            EnsureOpen();
            using var command = Command("""
                SELECT envelope FROM changes c WHERE pending = 1 AND
                    NOT EXISTS (SELECT 1 FROM field_edits f WHERE f.sender_seq = c.sender_seq)
                ORDER BY sender_seq
                """);
            using var reader = command.ExecuteReader();
            var envelopes = new List<UpdateEnvelope>();
            while (reader.Read()) envelopes.Add(JsonSerializer.Deserialize<UpdateEnvelope>((byte[])reader[0])
                ?? throw new InvalidDataException("Invalid envelope in the local journal."));
            return envelopes;
        }
    }

    public void Close() => Atomic(() =>
    {
        using var command = Command("UPDATE session SET closed = 1 WHERE id = 1");
        return command.ExecuteNonQuery();
    });

    private void EnsureOpen()
    {
        if (IsClosed) throw new InvalidOperationException("The collaboration session is closed.");
    }

    private T Atomic<T>(Func<T> action)
    {
        lock (gate)
        {
            using var current = db.BeginTransaction();
            transaction = current;
            try
            {
                var result = action();
                current.Commit();
                return result;
            }
            finally { transaction = null; }
        }
    }

    private SqliteCommand Command(string sql, params (string Name, object Value)[] parameters)
    {
        var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }

    public void Dispose()
    {
        lock (gate) db.Dispose();
    }
}
