namespace Novalist.Sync.Client;

public sealed class HostedProjectSession : IAsyncDisposable
{
    private readonly DeviceSequence sequence;
    private readonly SessionJournal journal;
    private readonly EnvelopeCipher cipher;
    private readonly HostedTransport transport;
    private readonly SessionSynchronizer synchronizer;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime;
    private readonly object lifetimeGate = new();
    private bool disposed;
    public CollaborationSession Updates { get; }
    public HostedTransport Transport => transport;
    private bool accessEnded;
    private bool epochChanged;
    public SessionConnectionStatus Status => accessEnded ? new(SessionConnectionState.AccessEnded) :
        epochChanged ? new(SessionConnectionState.KeyRefreshRequired) : synchronizer.Status;
    public SessionPresence Presence { get; }
    public bool IsClosed => journal.IsClosed;

    private sealed record Resources(DeviceSequence Sequence, SessionJournal Journal, EnvelopeCipher Cipher,
        HostedTransport Transport, CancellationTokenSource Lifetime);

    private HostedProjectSession(Resources resources, CollaborationSession updates, SessionPresence presence)
    {
        sequence = resources.Sequence;
        journal = resources.Journal;
        cipher = resources.Cipher;
        transport = resources.Transport;
        lifetime = resources.Lifetime;
        Updates = updates;
        Presence = presence;
        synchronizer = new(updates);
        transport.AccessEnded += OnAccessEnded;
        transport.EpochChanged += OnEpochChanged;
    }

    private void OnAccessEnded(Guid project)
    {
        if (project != Updates.Context.ProjectId) return;
        accessEnded = true;
        Updates.ReportAccessEnded();
    }

    private void OnEpochChanged(Guid project, uint epoch)
    {
        if (project != Updates.Context.ProjectId || epoch <= Updates.Context.Epoch) return;
        epochChanged = true;
        Updates.ReportAccessEnded();
    }

    public static async Task<HostedProjectSession> OpenAsync(string storage, Guid sessionId, HostedProjectAccess access,
        DeviceIdentity device, Func<Task<string>> accessToken)
    {
        var sequence = new DeviceSequence(Path.Combine(storage, "devices", device.Id.ToString("N") + ".sqlite"), device.Id);
        SessionJournal? journal = null;
        EnvelopeCipher? cipher = null;
        HostedTransport? transport = null;
        var lifetime = new CancellationTokenSource();
        try
        {
            journal = new(Path.Combine(storage, "sessions", sessionId.ToString("N"), "journal.sqlite"), sessionId, access.ProjectId, sequence);
            journal.BindFirstServerSequence(access.FirstServerSequence);
            cipher = access.CreateCipher();
            transport = new(access.Server, device, () => accessToken().WaitAsync(lifetime.Token));
            var context = new SessionContext(access.ProjectId, device.Id, access.Epoch);
            transport.BindProjectEpoch(context);
            var identity = access.CreateSessionIdentity(device);
            var updates = new CollaborationSession(context, journal, cipher, identity, transport);
            var presence = new SessionPresence(context, identity, sequence, cipher, transport);
            return new(new(sequence, journal, cipher, transport, lifetime), updates, presence);
        }
        catch
        {
            if (transport is not null) await transport.DisposeAsync();
            cipher?.Dispose();
            journal?.Dispose();
            sequence.Dispose();
            lifetime.Dispose();
            throw;
        }
    }

    public Task ConnectAsync(CancellationToken ct) => RunAsync(transport.ConnectAsync, ct);

    public Task StartAsync(bool createProject, CancellationToken ct) => RunAsync(async token =>
    {
        await synchronizer.StartAsync(createProject, token);
        if (Updates.Identity is { } identity)
            await transport.RegisterProjectDeviceAsync(Updates.Context.ProjectId, Updates.Context.Epoch, identity.Certificate, token);
    }, ct);

    private async Task RunAsync(Func<CancellationToken, Task> action, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            using var cancellation = ct.Register(Stop);
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
            await action(operation.Token);
        }
        finally { gate.Release(); }
    }

    public async Task EndAsync(CancellationToken ct)
    {
        Stop();
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
        Stop();
        await gate.WaitAsync();
        try
        {
            if (disposed) return;
            await Presence.DisposeAsync();
            await synchronizer.DisposeAsync();
            await Updates.DisposeAsync();
            transport.AccessEnded -= OnAccessEnded;
            transport.EpochChanged -= OnEpochChanged;
            await transport.DisposeAsync();
            cipher.Dispose();
            journal.Dispose();
            sequence.Dispose();
            lock (lifetimeGate) { disposed = true; lifetime.Dispose(); }
        }
        finally { gate.Release(); }
    }

    private void Stop()
    {
        lock (lifetimeGate) { if (!disposed) lifetime.Cancel(); }
    }
}
