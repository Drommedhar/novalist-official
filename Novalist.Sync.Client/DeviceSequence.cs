using Microsoft.Data.Sqlite;

namespace Novalist.Sync.Client;

public sealed class DeviceSequence : IDisposable
{
    private readonly SqliteConnection db;
    private readonly object gate = new();
    public Guid DeviceId { get; }

    public DeviceSequence(string path, Guid deviceId)
    {
        if (deviceId == Guid.Empty) throw new ArgumentException("A device identity is required.", nameof(deviceId));
        DeviceId = deviceId;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new ArgumentException("A device database path is required.", nameof(path)));
        db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        try
        {
            db.Open();
            using var initialize = db.CreateCommand();
            initialize.CommandText = """
                PRAGMA synchronous=FULL;
                CREATE TABLE IF NOT EXISTS device (id INTEGER PRIMARY KEY CHECK(id = 1), identity TEXT NOT NULL, sequence INTEGER NOT NULL);
                INSERT OR IGNORE INTO device VALUES (1, $identity, 0);
                SELECT identity FROM device WHERE id = 1;
                """;
            initialize.Parameters.AddWithValue("$identity", deviceId.ToString("D"));
            if (!Equals(initialize.ExecuteScalar(), deviceId.ToString("D")))
                throw new InvalidOperationException("The sequence database belongs to another device.");
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }

    public ulong Next()
    {
        lock (gate)
        {
            using var command = db.CreateCommand();
            command.CommandText = "UPDATE device SET sequence = sequence + 1 WHERE id = 1 AND sequence < 9223372036854775807 RETURNING sequence";
            return (ulong)(command.ExecuteScalar() as long? ?? throw new InvalidOperationException("The device sequence is exhausted."));
        }
    }

    public void Observe(ulong sequence)
    {
        if (sequence > long.MaxValue) throw new ArgumentOutOfRangeException(nameof(sequence));
        lock (gate)
        {
            using var command = db.CreateCommand();
            command.CommandText = "UPDATE device SET sequence = MAX(sequence, $observed) WHERE id = 1";
            command.Parameters.AddWithValue("$observed", (long)sequence);
            command.ExecuteNonQuery();
        }
    }

    public void Dispose()
    {
        lock (gate) db.Dispose();
    }
}
