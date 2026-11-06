using System.Security.Cryptography;
using System.Text.Json;

namespace Novalist.Sync.Client;

public sealed record CursorSelection(byte[] Anchor, byte[] Focus);
public sealed record LocalPresence(Guid ItemId, Guid ViewId, string Name, CursorSelection? Selection, bool Active);
public sealed record ParticipantPresence(Guid DeviceId, Guid ViewId, Guid ItemId, string Name, CursorSelection? Selection);

public sealed class SessionPresence : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SessionContext context;
    private readonly CertifiedSessionIdentity identity;
    private readonly DeviceSequence sequence;
    private readonly EnvelopeCipher cipher;
    private readonly IPresenceTransport transport;
    private readonly TimeProvider clock;
    private readonly object gate = new();
    private readonly SemaphoreSlim publishing = new(1, 1);
    private readonly CancellationTokenSource stopping = new();
    private readonly Dictionary<Guid, ulong> sequences = [];
    private readonly Dictionary<(Guid Device, Guid View), (ParticipantPresence Value, DateTimeOffset Until)> participants = [];
    private bool disposed;
    private Task? closing;
    public bool RejectedMessages { get; private set; }

    private sealed record Message(int Version, Guid ViewId, string Name, CursorSelection? Selection, bool Active, DateTimeOffset SentAt);

    public SessionPresence(SessionContext context, CertifiedSessionIdentity identity, DeviceSequence sequence,
        EnvelopeCipher cipher, IPresenceTransport transport, TimeProvider? clock = null)
    {
        identity.Membership.CheckSession(context);
        if (context.DeviceId != identity.Device.Id || sequence.DeviceId != context.DeviceId)
            throw new ArgumentException("Presence belongs to another device.", nameof(context));
        this.context = context;
        this.identity = identity with { Certificate = identity.Certificate.ToArray() };
        this.sequence = sequence;
        this.cipher = cipher;
        this.transport = transport;
        this.clock = clock ?? TimeProvider.System;
        transport.PresenceReceived += ReceiveAsync;
    }

    public async Task PublishAsync(LocalPresence value, CancellationToken ct)
    {
        await publishing.WaitAsync(ct);
        try
        {
            UpdateEnvelope envelope;
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                var message = new Message(1, value.ViewId, value.Name, value.Selection, value.Active, clock.GetUtcNow());
                Validate(message);
                if (!transport.IsConnected) return;
                envelope = cipher.SealAuthorizedPresence(new(context.ProjectId, value.ItemId, context.Epoch, context.DeviceId, sequence.Next()),
                    JsonSerializer.SerializeToUtf8Bytes(message, Json), identity.Device.SigningKey, identity.Certificate);
            }
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct, stopping.Token);
            await transport.PublishPresenceAsync(envelope, operation.Token);
        }
        finally { publishing.Release(); }
    }

    public IReadOnlyList<ParticipantPresence> Snapshot()
    {
        lock (gate)
        {
            Expire();
            return participants.Values.Select(entry => entry.Value with
            {
                Selection = entry.Value.Selection is { } selection ? new(selection.Anchor.ToArray(), selection.Focus.ToArray()) : null
            }).ToArray();
        }
    }

    private Task ReceiveAsync(UpdateEnvelope envelope)
    {
        lock (gate)
        {
            if (disposed) return Task.CompletedTask;
            try { Receive(envelope); }
            catch (Exception ex) when (ex is CryptographicException or FormatException or InvalidDataException or JsonException)
            { RejectedMessages = true; }
        }
        return Task.CompletedTask;
    }

    private void Receive(UpdateEnvelope envelope)
    {
        var header = UpdateHeader.Parse(envelope.Header, EnvelopeKind.Presence);
        var authorized = cipher.OpenAuthorizedPresence(envelope, identity.Membership);
        var message = JsonSerializer.Deserialize<Message>(authorized.Payload, Json) ?? throw new InvalidDataException("Missing participant presence.");
        Validate(message);
        var now = clock.GetUtcNow();
        if (header.SenderDeviceId == context.DeviceId || header.SenderSeq <= sequences.GetValueOrDefault(header.SenderDeviceId) ||
            message.SentAt < now.AddSeconds(-30) || message.SentAt > now.AddSeconds(30)) return;
        sequences[header.SenderDeviceId] = header.SenderSeq;
        Expire();
        var key = (header.SenderDeviceId, message.ViewId);
        if (!message.Active) participants.Remove(key);
        else participants[key] = (new(header.SenderDeviceId, message.ViewId, header.ItemId, message.Name, message.Selection), now.AddSeconds(15));
    }

    private void Expire()
    {
        var now = clock.GetUtcNow();
        foreach (var key in participants.Where(entry => entry.Value.Until <= now).Select(entry => entry.Key).ToArray()) participants.Remove(key);
    }

    private static void Validate(Message message)
    {
        if (message.Version != 1 || message.ViewId == Guid.Empty || string.IsNullOrWhiteSpace(message.Name) ||
            message.Name.Length > 128 || message.Name.Any(char.IsControl) ||
            (message.Selection is { } selection && (selection.Anchor is not { Length: > 0 and <= 512 } || selection.Focus is not { Length: > 0 and <= 512 })))
            throw new InvalidDataException("Invalid participant presence.");
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (closing is not null) return new(closing);
            disposed = true;
            transport.PresenceReceived -= ReceiveAsync;
            participants.Clear();
            stopping.Cancel();
            closing = FinishClosingAsync();
            return new(closing);
        }
    }

    private async Task FinishClosingAsync()
    {
        await publishing.WaitAsync();
        try { stopping.Dispose(); }
        finally { publishing.Release(); }
    }
}
