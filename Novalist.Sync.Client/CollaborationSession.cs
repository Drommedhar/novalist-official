using System.Security.Cryptography;
using NSec.Cryptography;

namespace Novalist.Sync.Client;

public sealed record SessionContext(Guid ProjectId, Guid DeviceId, uint Epoch);
public sealed record CertifiedSessionIdentity(ProjectMembership Membership, byte[] Certificate, DeviceIdentity Device);

public sealed partial class CollaborationSession : IAsyncDisposable
{
    private readonly Guid project;
    private readonly Guid device;
    private readonly uint epoch;
    private readonly SessionJournal journal;
    private readonly EnvelopeCipher cipher;
    private readonly Key signing;
    private readonly IReadOnlyDictionary<Guid, PublicKey> writers;
    private readonly CertifiedSessionIdentity? identity;
    private readonly IUpdateTransport transport;
    private readonly SemaphoreSlim connectionGate = new(1);
    private readonly object receiveGate = new();
    private bool paused;
    private volatile bool closed;
    private volatile bool disposed;

    public event Action<Guid, byte[]>? UpdateReceived;
    public event Action? CommittedHistoryChanged;
    public event Action<Exception>? ReceiveFailed;
    internal event Action? Saved;
    internal event Action? Stopping;
    public Exception? ReceiveError { get; private set; }
    public bool IsConnected => !closed && transport.IsConnected;
    public SessionContext Context => new(project, device, epoch);

    public CollaborationSession(SessionContext context, SessionJournal journal, EnvelopeCipher cipher,
        Key signing, IReadOnlyDictionary<Guid, PublicKey> writers, IUpdateTransport transport)
        : this(context, journal, cipher, new SessionKeys(signing, writers, null), transport) { }

    public CollaborationSession(SessionContext context, SessionJournal journal, EnvelopeCipher cipher,
        CertifiedSessionIdentity identity, IUpdateTransport transport)
        : this(context, journal, cipher, new SessionKeys(identity.Device.SigningKey, new Dictionary<Guid, PublicKey>(), identity), transport) { }

    private sealed record SessionKeys(Key Signing, IReadOnlyDictionary<Guid, PublicKey> Writers, CertifiedSessionIdentity? Identity);

    private CollaborationSession(SessionContext context, SessionJournal journal, EnvelopeCipher cipher,
        SessionKeys keys, IUpdateTransport transport)
    {
        project = context.ProjectId;
        device = context.DeviceId;
        epoch = context.Epoch;
        this.journal = journal;
        closed = journal.IsClosed;
        if (project != journal.ProjectId || device != journal.DeviceId) throw new ArgumentException("The journal belongs to another project or device.", nameof(context));
        this.cipher = cipher;
        signing = keys.Signing;
        writers = new Dictionary<Guid, PublicKey>(keys.Writers);
        identity = keys.Identity is { } certificate ? certificate with { Certificate = certificate.Certificate.ToArray() } : null;
        if (identity is not null)
        {
            identity.Membership.CheckSession(context);
            var verified = identity.Membership.VerifyCertificate(device, identity.Certificate);
            if (!verified.SigningKey.Equals(signing.PublicKey) || !verified.ReceivingKey.Equals(identity.Device.ReceivingKey.PublicKey))
                throw new CryptographicException("The session keys do not match its certificate.");
        }
        journal.BindAuthorization(identity?.Membership.ExportAuthority(), identity?.Certificate);
        if (!closed) journal.BindTransport(transport.ConnectionIdentity);
        this.transport = transport;
        transport.Received += ReceiveAsync;
    }

    public void Save(Guid item, byte[] update)
    {
        EnsureOpen();
        PrepareUpdate(item, update, null);
        Saved?.Invoke();
    }

    private void PrepareUpdate(Guid item, byte[] update, FieldLeaseRequest? lease)
    {
        var capacity = EnvelopeCipher.MaxPayloadBytes - (identity is null ? 0 : EnvelopeCipher.AuthorizationOverhead);
        UpdateEnvelope Seal(ulong sequence, byte[] packet) => identity is null
            ? cipher.Seal(new(project, item, epoch, device, sequence), packet, signing)
            : cipher.SealAuthorized(new(project, item, epoch, device, sequence), packet, signing, identity.Certificate);
        var packets = UpdatePacket.Split(update, capacity);
        if (lease is null) journal.AppendMany(item, packets, Seal);
        else journal.AppendField(item, packets, lease, Seal);
    }

    public IReadOnlyList<byte[]> ReadItem(Guid item) => journal.ReadItemRecords(item)
        .Select(record => (record.SenderDeviceId, Packet: UpdatePacket.Parse(record.Payload)))
        .GroupBy(record => (record.SenderDeviceId, record.Packet.MessageId))
        .Select(group => UpdatePacket.Assemble(group.Select(record => record.Packet)))
        .OfType<byte[]>().Where(update => !journal.IsRejectedUpdate(item, update)).ToArray();

    public void RejectUpdate(Guid item, string hash) => journal.RejectUpdate(item, hash);

    internal void ReportAccessEnded()
    {
        ReceiveError = new UnauthorizedAccessException("Your access to this shared project has ended. Your local copy is still available.");
        ReceiveFailed?.Invoke(ReceiveError);
    }

    public async Task SynchronizeAsync(bool createProject, CancellationToken cancellationToken)
    {
        await connectionGate.WaitAsync(cancellationToken);
        try
        {
            EnsureOpen();
            lock (receiveGate) paused = false;
            lock (fieldOperationGate) fieldsStopped = false;
            if (!transport.IsConnected) await transport.ConnectAsync(cancellationToken);
            if (createProject) await transport.CreateProjectAsync(project, cancellationToken);
            await transport.JoinAsync(project, cancellationToken);
            await foreach (var update in transport.CatchUpAsync(project, journal.LastServerSequence, cancellationToken))
                Receive(update);
            foreach (var envelope in journal.Pending())
            {
                var sequence = await transport.PushAsync(envelope, cancellationToken);
                Acknowledge(envelope, sequence);
            }
        }
        finally { connectionGate.Release(); }
    }

    private Task ReceiveAsync(SequencedUpdate update)
    {
        lock (receiveGate)
        {
            if (closed || disposed || paused) return Task.CompletedTask;
            try { Receive(update); }
            catch (Exception error) when (error is CryptographicException or FormatException or InvalidOperationException or InvalidDataException)
            {
                ReceiveError = error;
                ReceiveFailed?.Invoke(error);
            }
        }
        return Task.CompletedTask;
    }

    private void Receive(SequencedUpdate update)
    {
        var header = UpdateHeader.Parse(update.Envelope.Header);
        byte[] plaintext;
        byte[]? certificate = null;
        if (identity is not null)
        {
            var authorized = cipher.OpenAuthorized(update.Envelope, identity.Membership);
            plaintext = authorized.Payload;
            certificate = authorized.Certificate;
        }
        else
        {
            if (!writers.TryGetValue(header.SenderDeviceId, out var publicKey))
                throw new CryptographicException("The update sender is not an authorized writer.");
            plaintext = cipher.Open(update.Envelope, publicKey);
        }
        var packet = UpdatePacket.Parse(plaintext);
        var before = journal.LastServerSequence;
        var added = journal.Receive(update, plaintext, certificate);
        if (journal.LastServerSequence > before) CommittedHistoryChanged?.Invoke();
        if (!added) return;
        var fragments = journal.ReadItemRecords(header.ItemId)
            .Where(record => record.SenderDeviceId == header.SenderDeviceId)
            .Select(record => UpdatePacket.Parse(record.Payload))
            .Where(fragment => fragment.MessageId == packet.MessageId);
        if (UpdatePacket.Assemble(fragments) is { } completed) UpdateReceived?.Invoke(header.ItemId, completed);
    }

    private void Acknowledge(UpdateEnvelope envelope, long sequence)
    {
        var before = journal.LastServerSequence;
        journal.Acknowledge(envelope, sequence);
        if (journal.LastServerSequence > before) CommittedHistoryChanged?.Invoke();
    }

    public async Task PauseAsync(CancellationToken cancellationToken)
    {
        StopFieldOperations();
        Stopping?.Invoke();
        await connectionGate.WaitAsync(cancellationToken);
        try
        {
            lock (receiveGate) paused = true;
            await transport.DisconnectAsync(cancellationToken);
        }
        finally { connectionGate.Release(); }
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        StopFieldOperations();
        Stopping?.Invoke();
        await connectionGate.WaitAsync(cancellationToken);
        try
        {
            EnsureOpen();
            lock (receiveGate) paused = true;
            await transport.DisconnectAsync(cancellationToken);
            journal.Close();
            closed = true;
        }
        finally { connectionGate.Release(); }
    }

    private void EnsureOpen()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (closed) throw new InvalidOperationException("The collaboration session is closed.");
    }

    public async ValueTask DisposeAsync()
    {
        StopFieldOperations();
        Stopping?.Invoke();
        await connectionGate.WaitAsync();
        try
        {
            if (disposed) return;
            lock (receiveGate) disposed = true;
            transport.Received -= ReceiveAsync;
            await transport.DisconnectAsync(CancellationToken.None);
        }
        finally { connectionGate.Release(); }
    }
}
