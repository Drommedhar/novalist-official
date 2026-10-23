using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Channels;
using NSec.Cryptography;

namespace Novalist.Sync.Client;

public sealed record PeerHostOptions(Guid ProjectId, uint Epoch, byte[] EpochKey, DeviceIdentity Owner);

public sealed partial class TcpPeerHost : IAsyncDisposable
{
    private readonly TcpListener listener;
    private readonly X509Certificate2 tlsCertificate;
    private readonly CancellationTokenSource stopping = new();
    private readonly ConcurrentDictionary<long, Task> connections = new();
    private readonly Dictionary<Guid, PeerClient> clients = [];
    private readonly object gate = new();
    private readonly PeerUpdateLog log;
    private readonly ProjectMembership membership;
    private readonly EnvelopeCipher cipher;
    private readonly DeviceIdentity owner;
    private readonly byte[] epochKey;
    private readonly Guid project;
    private readonly uint epoch;
    private readonly Task accepting;
    private long connectionNumber;
    private bool disposed;
    public Guid SessionId { get; } = Guid.NewGuid();
    public IPEndPoint EndPoint => (IPEndPoint)listener.LocalEndpoint;
    public Exception? ConnectionError { get; private set; }

    private TcpPeerHost(IPEndPoint endpoint, string storage, PeerHostOptions options)
    {
        project = options.ProjectId;
        epoch = options.Epoch;
        owner = options.Owner;
        epochKey = options.EpochKey.ToArray();
        membership = new(project, epoch, owner.SigningKey.PublicKey);
        cipher = new(project, epoch, epochKey);
        tlsCertificate = CreateTlsCertificate();
        listener = new(endpoint);
        log = new(storage, SessionId);
        members.Add(owner.Id, membership.CreateOwnerCertificate(owner));
        try { listener.Start(); accepting = AcceptAsync(); }
        catch { log.Dispose(); cipher.Dispose(); tlsCertificate.Dispose(); throw; }
    }

    public static Task<TcpPeerHost> StartAsync(IPEndPoint endpoint, string storage, PeerHostOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new TcpPeerHost(endpoint, storage, options));
    }

    private static X509Certificate2 CreateTlsCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Novalist peer session", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
        var pfx = certificate.Export(X509ContentType.Pfx);
        try
        {
            // Schannel requires a provider-backed private key rather than an ephemeral CNG key.
            return X509CertificateLoader.LoadPkcs12(pfx, null,
                OperatingSystem.IsWindows() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet);
        }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }

    private PeerAddress Address(string host)
    {
        var address = new PeerAddress(host, EndPoint.Port, SessionId, project, epoch, SHA256.HashData(tlsCertificate.RawData));
        PeerProjectAccess.ValidateAddress(address);
        return address;
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                var socket = await listener.AcceptTcpClientAsync(stopping.Token);
                var number = Interlocked.Increment(ref connectionNumber);
                var task = ServeAsync(socket);
                connections[number] = task;
                _ = task.ContinueWith(finished =>
                {
                    if (finished.Exception is { } error) ConnectionError = error;
                    connections.TryRemove(number, out _);
                }, CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (SocketException) when (stopping.IsCancellationRequested) { }
    }

    private async Task ServeAsync(TcpClient socket)
    {
        using (socket)
        using (var stream = new SslStream(socket.GetStream(), leaveInnerStreamOpen: false))
        using (var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token))
        {
            PeerClient? client = null;
            Task writer = Task.CompletedTask;
            try
            {
                socket.NoDelay = true;
                using var handshake = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                handshake.CancelAfter(TimeSpan.FromSeconds(10));
                await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = tlsCertificate,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                }, handshake.Token);
                var device = await AuthenticateAsync(stream, handshake.Token);
                client = new(device, lifetime);
                lock (gate)
                {
                    if (clients.Remove(device, out var previous)) previous.Stop();
                    clients.Add(device, client);
                }
                writer = WriteMessagesAsync(stream, client, lifetime.Token);
                while (!lifetime.IsCancellationRequested)
                {
                    var request = await PeerProtocol.ReadAsync<PeerRequest>(stream, lifetime.Token);
                    if (request.Operation.StartsWith("bootstrap-", StringComparison.Ordinal))
                        client.Send(await DispatchBootstrapAsync(client, request, lifetime.Token));
                    else lock (gate) client.Send(Dispatch(client, request));
                }
            }
            catch (Exception error) when (error is IOException or SocketException or AuthenticationException or
                CryptographicException or JsonException or InvalidDataException or OperationCanceledException)
            {
                if (error is not EndOfStreamException && !lifetime.IsCancellationRequested) ConnectionError = error;
            }
            finally
            {
                lifetime.Cancel();
                if (client is not null)
                {
                    lock (gate)
                    {
                        if (clients.GetValueOrDefault(client.Device) == client) clients.Remove(client.Device);
                    }
                    client.Outgoing.Writer.TryComplete();
                    await FinishBootstrapAsync(client);
                }
                await writer;
            }
        }
    }

    private static async Task WriteMessagesAsync(Stream stream, PeerClient client, CancellationToken ct)
    {
        try
        {
            await foreach (var message in client.Outgoing.Reader.ReadAllAsync(ct))
                await PeerProtocol.WriteAsync(stream, message, ct);
        }
        catch (Exception error) when (error is IOException or SocketException or OperationCanceledException)
        { client.Stop(); }
    }

    private sealed class PeerClient(Guid device, CancellationTokenSource lifetime)
    {
        public Guid Device { get; } = device;
        public bool Joined { get; set; }
        public PeerBootstrapArchive? Bootstrap { get; set; }
        public PeerBootstrapSession? BootstrapSession { get; set; }
        public bool BootstrapPending { get; set; }
        public bool BootstrapLockHeld { get; set; }
        public Channel<PeerMessage> Outgoing { get; } = Channel.CreateBounded<PeerMessage>(256);
        public void Stop() => lifetime.Cancel();
        public void Send(PeerMessage value) { if (!Outgoing.Writer.TryWrite(value)) Stop(); }
    }

    private PeerMessage Dispatch(PeerClient client, PeerRequest request)
    {
        try
        {
            if (!members.ContainsKey(client.Device)) throw new CryptographicException("This device is no longer a member of the session.");
            if (request.Id <= 0 || request.Project != project) throw new InvalidDataException("The peer request belongs to another project.");
            if (request.Operation == "create" && client.Device == owner.Id) return new(request.Id, "reply");
            if (request.Operation == "join") { client.Joined = true; return new(request.Id, "reply"); }
            if (!client.Joined) throw new InvalidOperationException("Join the peer project before editing.");
            return request.Operation switch
            {
                "catch-up" when request.After >= 0 && request.After <= log.LastSequence => new(request.Id, "reply", Updates: log.Read(request.After)),
                "push" => new(request.Id, "reply", Sequence: Push(client, request)),
                "field-push" => new(request.Id, "reply", Sequence: Push(client, request with { Lease = RequireLease(request) })),
                "guarded-field-push" when request.Lease?.RequiredLease is not null => new(request.Id, "reply", Sequence: Push(client, request with { Lease = RequireLease(request) })),
                "field-acquire" => new(request.Id, "reply", Lease: Acquire(client, RequireLease(request))),
                "field-renew" => new(request.Id, "reply", Lease: Renew(client, RequireLease(request))),
                "field-release" => new(request.Id, "reply", Released: Release(client, RequireLease(request))),
                "presence" => PublishPresence(client, request),
                _ => throw new InvalidOperationException("The peer request is not supported.")
            };
        }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or CryptographicException or FormatException)
        { return new(request.Id, "reply", Error: error.Message); }
    }

    private long Push(PeerClient client, PeerRequest request)
    {
        var envelope = request.Envelope ?? throw new InvalidDataException("Missing peer update.");
        var header = UpdateHeader.Parse(envelope.Header);
        if (header.SenderDeviceId != client.Device) throw new CryptographicException("The peer update belongs to another device.");
        var authorized = cipher.OpenAuthorized(envelope, membership);
        CryptographicOperations.ZeroMemory(authorized.Payload);
        if (log.Find(header, envelope) is { } existing) return existing;
        if (request.Lease is { } lease && !Owns(client, lease, DateTimeOffset.UtcNow))
            throw new InvalidOperationException("This field's editing lease has expired or changed.");
        if (request.Lease?.RequiredLease is { } required && !Owns(client, new(project, required.ResourceId, epoch, required.LeaseId), DateTimeOffset.UtcNow))
            throw new InvalidOperationException("The required editing lease has expired or changed.");
        var sequence = log.Append(header, envelope);
        var update = new PeerMessage(0, "update", Update: new(sequence, envelope));
        foreach (var peer in clients.Values.Where(peer => peer.Joined)) peer.Send(update);
        return sequence;
    }

    private PeerMessage PublishPresence(PeerClient client, PeerRequest request)
    {
        var envelope = request.Envelope ?? throw new InvalidDataException("Missing peer presence.");
        var header = UpdateHeader.Parse(envelope.Header, EnvelopeKind.Presence);
        if (header.SenderDeviceId != client.Device) throw new CryptographicException("The peer presence belongs to another device.");
        var authorized = cipher.OpenAuthorizedPresence(envelope, membership);
        CryptographicOperations.ZeroMemory(authorized.Payload);
        foreach (var peer in clients.Values.Where(peer => peer.Joined && peer.Device != client.Device))
            peer.Send(new(0, "presence", Presence: envelope));
        return new(request.Id, "reply");
    }

    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            stopping.Cancel();
            listener.Stop();
        }
        await accepting;
        await Task.WhenAll(connections.Values);
        log.Dispose();
        cipher.Dispose();
        tlsCertificate.Dispose();
        CryptographicOperations.ZeroMemory(epochKey);
        stopping.Dispose();
    }
}
