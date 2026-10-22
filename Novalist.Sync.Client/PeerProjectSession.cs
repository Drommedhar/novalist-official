namespace Novalist.Sync.Client;

public sealed class PeerProjectSession : IAsyncDisposable
{
    private readonly DeviceSequence sequence;
    private readonly SessionJournal journal;
    private readonly EnvelopeCipher cipher;
    private readonly SessionSynchronizer synchronizer;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource stopping = new();
    private bool disposed;
    public TcpPeerTransport Transport { get; }
    public CollaborationSession Updates { get; }
    public SessionPresence Presence { get; }
    public SessionConnectionStatus Status => synchronizer.Status;

    private PeerProjectSession(DeviceSequence sequence, SessionJournal journal, EnvelopeCipher cipher, TcpPeerTransport transport, DeviceIdentity device)
    {
        this.sequence = sequence;
        this.journal = journal;
        this.cipher = cipher;
        Transport = transport;
        var access = transport.Access;
        var context = new SessionContext(access.ProjectId, device.Id, access.Epoch);
        var identity = access.CreateSessionIdentity(device);
        Updates = new(context, journal, cipher, identity, transport);
        Presence = new(context, identity, sequence, cipher, transport);
        synchronizer = new(Updates);
    }

    public static async Task<PeerProjectSession> OpenAsync(string storage, Guid id, TcpPeerTransport transport, DeviceIdentity device)
    {
        DeviceSequence? sequence = null;
        SessionJournal? journal = null;
        EnvelopeCipher? cipher = null;
        try
        {
            sequence = new(Path.Combine(storage, "devices", device.Id.ToString("N") + ".sqlite"), device.Id);
            journal = new(Path.Combine(storage, "sessions", id.ToString("N"), "journal.sqlite"), id, transport.Access.ProjectId, sequence);
            cipher = transport.Access.CreateCipher();
            return new(sequence, journal, cipher, transport, device);
        }
        catch
        {
            cipher?.Dispose();
            journal?.Dispose();
            sequence?.Dispose();
            await transport.DisposeAsync();
            transport.Access.Dispose();
            throw;
        }
    }

    public async Task StartAsync(bool createProject, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct, stopping.Token);
            await synchronizer.StartAsync(createProject, operation.Token);
        }
        finally { gate.Release(); }
    }

    public async Task EndAsync(CancellationToken ct)
    {
        stopping.Cancel();
        await gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            await Presence.DisposeAsync();
            if (!journal.IsClosed) await Updates.CloseAsync(ct);
            await synchronizer.DisposeAsync();
        }
        finally { gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (disposed) return;
            disposed = true;
            stopping.Cancel();
            await Presence.DisposeAsync();
            await synchronizer.DisposeAsync();
            await Updates.DisposeAsync();
            await Transport.DisposeAsync();
            Transport.Access.Dispose();
            cipher.Dispose();
            journal.Dispose();
            sequence.Dispose();
            stopping.Dispose();
        }
        finally { gate.Release(); }
    }
}
