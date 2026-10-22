using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;
using NSec.Cryptography;

namespace Novalist.Sync.Client;

public sealed partial class TcpPeerTransport : IUpdateTransport, IPresenceTransport
{
    private readonly DeviceIdentity device;
    private readonly bool ownsAccess;
    private readonly SemaphoreSlim connectionGate = new(1, 1);
    private readonly SemaphoreSlim sending = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<PeerMessage>> pending = new();
    private TcpClient? socket;
    private SslStream? stream;
    private CancellationTokenSource? connection;
    private Task receiving = Task.CompletedTask;
    private long requestId;
    private volatile bool connected;
    private bool disposed;
    public PeerProjectAccess Access { get; }
    public bool IsConnected => connected;
    public string ConnectionIdentity => "peer:" + Access.Address.SessionId.ToString("D");
    public event Func<SequencedUpdate, Task>? Received;
    public event Func<UpdateEnvelope, Task>? PresenceReceived;
    public Exception? ReceiveError { get; private set; }

    public TcpPeerTransport(PeerProjectAccess access, DeviceIdentity device) : this(access, device, false) { }

    private TcpPeerTransport(PeerProjectAccess access, DeviceIdentity device, bool ownsAccess)
    {
        access.CreateSessionIdentity(device);
        Access = access;
        this.device = device;
        this.ownsAccess = ownsAccess;
    }

    public static async Task<TcpPeerTransport> JoinInvitationAsync(PeerInvitation invitation, DeviceIdentity device, CancellationToken ct)
    {
        if (invitation.Secret is not { Length: 32 } || invitation.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidDataException("The peer invitation is invalid or expired.");
        var (socket, stream, welcome) = await OpenAsync(invitation.Address, device, invitation.Secret, null, ct);
        try
        {
            var access = new PeerProjectAccess(invitation.Address, welcome, device);
            var transport = new TcpPeerTransport(access, device, true);
            transport.Attach(socket, stream);
            return transport;
        }
        catch { stream.Dispose(); socket.Dispose(); throw; }
        finally { CryptographicOperations.ZeroMemory(welcome.EpochKey); }
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        await connectionGate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (connected) return;
            await DisconnectCoreAsync();
            var opened = await OpenAsync(Access.Address, device, null, Access.Certificate, ct);
            try
            {
                if (!opened.Welcome.Certificate.AsSpan().SequenceEqual(Access.Certificate))
                    throw new CryptographicException("The peer session returned different project credentials.");
                Attach(opened.Socket, opened.Stream);
            }
            catch { opened.Stream.Dispose(); opened.Socket.Dispose(); throw; }
            finally { CryptographicOperations.ZeroMemory(opened.Welcome.EpochKey); }
        }
        finally { connectionGate.Release(); }
    }

    private void Attach(TcpClient socket, SslStream stream)
    {
        this.socket = socket;
        this.stream = stream;
        connection = new();
        connected = true;
        ReceiveError = null;
        receiving = ReceiveAsync(stream, connection.Token);
    }

    private static async Task<(TcpClient Socket, SslStream Stream, PeerWelcome Welcome)> OpenAsync(PeerAddress address,
        DeviceIdentity device, byte[]? invitation, byte[]? certificate, CancellationToken ct)
    {
        PeerProjectAccess.ValidateAddress(address);
        var socket = new TcpClient { NoDelay = true };
        SslStream? stream = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await socket.ConnectAsync(address.Host, address.Port, timeout.Token);
            stream = new(socket.GetStream(), false, (_, presented, _, _) => presented is not null &&
                CryptographicOperations.FixedTimeEquals(SHA256.HashData(presented.GetRawCertData()), address.CertificateHash));
            await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "Novalist peer session", EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }, timeout.Token);
            var challenge = await PeerProtocol.ReadAsync<PeerChallenge>(stream, timeout.Token);
            if (challenge.SessionId != address.SessionId || challenge.Nonce is not { Length: 32 })
                throw new CryptographicException("The peer host belongs to a different session.");
            var hello = new PeerHello(device.Id, device.SigningKey.PublicKey.Export(KeyBlobFormat.RawPublicKey),
                device.ReceivingKey.PublicKey.Export(KeyBlobFormat.RawPublicKey), [], invitation, certificate);
            hello = hello with { Signature = SignatureAlgorithm.Ed25519.Sign(device.SigningKey, PeerProtocol.Proof(challenge, hello)) };
            await PeerProtocol.WriteAsync(stream, hello, timeout.Token);
            var welcome = await PeerProtocol.ReadAsync<PeerWelcome>(stream, timeout.Token);
            if (welcome.Error is not null) throw new CryptographicException(welcome.Error);
            return (socket, stream, welcome);
        }
        catch { stream?.Dispose(); socket.Dispose(); throw; }
    }

    public async Task CreateProjectAsync(Guid project, CancellationToken ct) => await RequestAsync(new(NextId(), "create", project), ct);
    public async Task JoinAsync(Guid project, CancellationToken ct) => await RequestAsync(new(NextId(), "join", project), ct);
    public async Task<long> PushAsync(UpdateEnvelope envelope, CancellationToken ct) =>
        (await RequestAsync(new(NextId(), "push", Access.ProjectId, Envelope: envelope), ct)).Sequence;
    public async Task PublishPresenceAsync(UpdateEnvelope envelope, CancellationToken ct) =>
        await RequestAsync(new(NextId(), "presence", Access.ProjectId, Envelope: envelope), ct);

    public async IAsyncEnumerable<SequencedUpdate> CatchUpAsync(Guid project, long after, [EnumeratorCancellation] CancellationToken ct)
    {
        while (true)
        {
            var response = await RequestAsync(new(NextId(), "catch-up", project, after), ct);
            var updates = response.Updates ?? throw new InvalidDataException("Missing peer catch-up updates.");
            foreach (var update in updates)
            {
                if (update.ServerSeq != after + 1) throw new InvalidDataException("The peer history is not contiguous.");
                after = update.ServerSeq;
                yield return update;
            }
            if (updates.Count < 16) yield break;
        }
    }

    private long NextId() => Interlocked.Increment(ref requestId);

    private async Task<PeerMessage> RequestAsync(PeerRequest request, CancellationToken ct)
    {
        var active = stream;
        if (!connected || active is null) throw new IOException("The peer session is disconnected.");
        var result = new TaskCompletionSource<PeerMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[request.Id] = result;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        operation.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await sending.WaitAsync(operation.Token);
            try { await PeerProtocol.WriteAsync(active, request, operation.Token); }
            catch { socket?.Dispose(); throw; }
            finally { sending.Release(); }
            var response = await result.Task.WaitAsync(operation.Token);
            if (response.Error is not null) throw new InvalidOperationException(response.Error);
            return response;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new TimeoutException("The peer host did not answer in time."); }
        finally { pending.TryRemove(request.Id, out _); }
    }

    private async Task ReceiveAsync(Stream active, CancellationToken ct)
    {
        Exception failure = new IOException("The peer host closed the connection.");
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var message = await PeerProtocol.ReadAsync<PeerMessage>(active, ct);
                if (message.Type == "reply" && pending.TryRemove(message.Id, out var request)) request.TrySetResult(message);
                else if (message.Type == "update" && message.Update is { } update && Received is { } handlers)
                    foreach (Func<SequencedUpdate, Task> handler in handlers.GetInvocationList()) await handler(update);
                else if (message.Type == "presence" && message.Presence is { } presence && PresenceReceived is { } presenceHandlers)
                    foreach (Func<UpdateEnvelope, Task> handler in presenceHandlers.GetInvocationList()) await handler(presence);
            }
        }
        catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or JsonException or InvalidDataException)
        { failure = error; if (!ct.IsCancellationRequested) ReceiveError = error; }
        finally
        {
            connected = false;
            socket?.Dispose();
            foreach (var pair in pending)
                if (pending.TryRemove(pair.Key, out var request)) request.TrySetException(failure);
        }
    }

    public async Task DisconnectAsync(CancellationToken ct)
    {
        await connectionGate.WaitAsync(ct);
        try { await DisconnectCoreAsync(); }
        finally { connectionGate.Release(); }
    }

    private async Task DisconnectCoreAsync()
    {
        connected = false;
        connection?.Cancel();
        socket?.Dispose();
        await receiving;
        stream?.Dispose();
        connection?.Dispose();
        socket = null;
        stream = null;
        connection = null;
    }

    public async ValueTask DisposeAsync()
    {
        await connectionGate.WaitAsync();
        try
        {
            if (disposed) return;
            disposed = true;
            await DisconnectCoreAsync();
            if (ownsAccess) Access.Dispose();
        }
        finally { connectionGate.Release(); }
    }
}
