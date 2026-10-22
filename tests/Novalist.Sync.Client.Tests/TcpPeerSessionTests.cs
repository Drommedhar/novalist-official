using System.Net;
using System.Security.Cryptography;
using NSubstitute;
using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class TcpPeerSessionTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "novalist-peer-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InvitedWriterReceivesExistingContentAndSendsEditsToTheOwnerOverTcp()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await DeviceAsync("owner", ct);
        using var writer = await DeviceAsync("writer", ct);
        var project = Guid.NewGuid();
        await using var host = await TcpPeerHost.StartAsync(new(IPAddress.Loopback, 0), folder, new(project, 0,
            RandomNumberGenerator.GetBytes(32), owner), ct);
        using var ownerAccess = host.CreateOwnerAccess("127.0.0.1");
        await using var ownerWire = new TcpPeerTransport(ownerAccess, owner);
        await using var ownerSession = OpenSession("owner", owner, ownerWire);
        var connectionError = await Record.ExceptionAsync(() => ownerSession.Updates.SynchronizeAsync(true, ct));
        Assert.Null(host.ConnectionError);
        Assert.Null(connectionError);
        var item = Guid.NewGuid();
        ownerSession.Updates.Save(item, "Existing scene"u8.ToArray());
        await ownerSession.Updates.SynchronizeAsync(false, ct);

        await using var writerWire = await TcpPeerTransport.JoinInvitationAsync(host.CreateInvitation("127.0.0.1"), writer, ct);
        await using var writerSession = OpenSession("writer", writer, writerWire);
        await writerSession.Updates.SynchronizeAsync(false, ct);
        Assert.Equal("Existing scene"u8.ToArray(), Assert.Single(writerSession.Updates.ReadItem(item)));
        var arrived = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        ownerSession.Updates.UpdateReceived += (_, value) => arrived.TrySetResult(value);
        writerSession.Updates.Save(item, "A writer's edit"u8.ToArray());
        await writerSession.Updates.SynchronizeAsync(false, ct);
        Assert.Equal("A writer's edit"u8.ToArray(), await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5), ct));
        Assert.Equal(2, ownerSession.Updates.ReadItem(item).Count);
    }

    [Fact]
    public async Task OnlyTheAdmittedDeviceCanReconnectAfterItsInvitationExpires()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await DeviceAsync("owner", ct);
        using var writer = await DeviceAsync("writer", ct);
        using var stranger = await DeviceAsync("stranger", ct);
        await using var host = await TcpPeerHost.StartAsync(new(IPAddress.Loopback, 0), folder,
            new(Guid.NewGuid(), 0, RandomNumberGenerator.GetBytes(32), owner), ct);
        var expires = DateTimeOffset.UtcNow.AddSeconds(3);
        var invitation = host.CreateInvitation("127.0.0.1", expiresAt: expires);
        await using (var joined = await TcpPeerTransport.JoinInvitationAsync(invitation, writer, ct))
            Assert.True(joined.IsConnected);
        await Task.Delay(expires - DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(50), ct);
        await using var resumed = await TcpPeerTransport.JoinInvitationAsync(invitation, writer, ct);
        Assert.True(resumed.IsConnected);
        var extended = invitation with { ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
        await Assert.ThrowsAsync<CryptographicException>(() => TcpPeerTransport.JoinInvitationAsync(extended, stranger, ct));
    }

    [Fact]
    public async Task RemovedWriterNeedsANewInvitationToRestoreItsPinnedDeviceCertificate()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await DeviceAsync("owner", ct);
        using var writer = await DeviceAsync("writer", ct);
        using var stranger = await DeviceAsync("stranger", ct);
        await using var host = await TcpPeerHost.StartAsync(new(IPAddress.Loopback, 0), folder,
            new(Guid.NewGuid(), 0, RandomNumberGenerator.GetBytes(32), owner), ct);
        var original = host.CreateInvitation("127.0.0.1");
        await using var joined = await TcpPeerTransport.JoinInvitationAsync(original, writer, ct);
        var certificate = joined.Access.Certificate;
        await joined.DisconnectAsync(ct);
        host.RemoveMember(writer.Id);
        Assert.DoesNotContain(writer.Id, host.Members());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => joined.ConnectAsync(ct));
        await Assert.ThrowsAsync<CryptographicException>(() => TcpPeerTransport.JoinInvitationAsync(original, writer, ct));
        var replacement = host.CreateInvitation("127.0.0.1");
        await using var readmitted = await TcpPeerTransport.JoinInvitationAsync(replacement, writer, ct);
        Assert.Equal(certificate, readmitted.Access.Certificate);
        Assert.Contains(writer.Id, host.Members());
        await Assert.ThrowsAsync<CryptographicException>(() => TcpPeerTransport.JoinInvitationAsync(replacement, stranger, ct));
        await Assert.ThrowsAsync<CryptographicException>(() => TcpPeerTransport.JoinInvitationAsync(original, writer, ct));
        Assert.True(joined.AccessHasEnded);
    }

    private static Task<DeviceIdentity> DeviceAsync(string subject, CancellationToken ct)
    {
        var credentials = Substitute.For<ICollaborationCredentials>();
        credentials.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));
        return new DeviceIdentityStore(credentials).GetOrCreateAsync(new("https://peer.test"), subject, ct);
    }

    [Fact]
    public async Task FocusedMetadataHasOneWriterAndHandsOffAfterSaving()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await DeviceAsync("owner", ct);
        using var writer = await DeviceAsync("writer", ct);
        var project = Guid.NewGuid();
        await using var host = await TcpPeerHost.StartAsync(new(IPAddress.Loopback, 0), folder, new(project, 0,
            RandomNumberGenerator.GetBytes(32), owner), ct);
        using var ownerAccess = host.CreateOwnerAccess("127.0.0.1");
        await using var ownerWire = new TcpPeerTransport(ownerAccess, owner);
        await using var ownerSession = OpenSession("owner", owner, ownerWire);
        await ownerSession.Updates.SynchronizeAsync(true, ct);
        await using var writerWire = await TcpPeerTransport.JoinInvitationAsync(host.CreateInvitation("127.0.0.1"), writer, ct);
        await using var writerSession = OpenSession("writer", writer, writerWire);
        await writerSession.Updates.SynchronizeAsync(false, ct);
        var item = Guid.NewGuid();
        var ownerLease = new FieldLeaseRequest(project, item, 0, Guid.NewGuid());
        var writerLease = ownerLease with { LeaseId = Guid.NewGuid() };
        Assert.True((await ownerSession.Updates.AcquireFieldAsync(ownerLease, ct)).Acquired);
        Assert.False((await writerSession.Updates.AcquireFieldAsync(writerLease, ct)).Acquired);
        Assert.True((await ownerSession.Updates.RenewFieldAsync(ownerLease, ct))!.Acquired);
        await ownerSession.Updates.CommitFieldAsync(item, "42"u8.ToArray(), ownerLease, ct);
        Assert.True(await ownerSession.Updates.ReleaseFieldAsync(ownerLease, ct));
        writerLease = writerLease with { LeaseId = Guid.NewGuid() };
        Assert.True((await writerSession.Updates.AcquireFieldAsync(writerLease, ct)).Acquired);
        Assert.Equal("42"u8.ToArray(), Assert.Single(writerSession.Updates.ReadCommittedItem(item)).Payload);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ownerSession.Updates.CommitFieldAsync(item, "43"u8.ToArray(), ownerLease, ct));
        await writerSession.Updates.CommitFieldAsync(item, "44"u8.ToArray(), writerLease, ct);
        await ownerSession.Updates.SynchronizeAsync(false, ct);
        Assert.Equal("44"u8.ToArray(), ownerSession.Updates.ReadCommittedItem(item)[^1].Payload);
        var graph = new FieldLeaseRequest(project, Guid.NewGuid(), 0, Guid.NewGuid());
        Assert.True((await writerSession.Updates.AcquireFieldAsync(graph, ct)).Acquired);
        var guarded = writerLease with { RequiredLease = new(graph.ResourceId, graph.LeaseId) };
        await writerSession.Updates.CommitFieldAsync(item, "45"u8.ToArray(), guarded, ct);
        Assert.True(await writerSession.Updates.ReleaseFieldAsync(graph, ct));
        var replacement = graph with { LeaseId = Guid.NewGuid() };
        Assert.True((await ownerSession.Updates.AcquireFieldAsync(replacement, ct)).Acquired);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writerSession.Updates.CommitFieldAsync(item, "46"u8.ToArray(), guarded, ct));
        Assert.Equal("45"u8.ToArray(), writerSession.Updates.ReadCommittedItem(item)[^1].Payload);
    }

    private PeerSession OpenSession(string name, DeviceIdentity device, TcpPeerTransport transport) =>
        new(Path.Combine(folder, name), device, transport);

    [Fact]
    public async Task SnapshotWaitsForCompleteLiveUpdatesBeforeChoosingItsSequenceBoundary()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await DeviceAsync("owner", ct);
        using var writer = await DeviceAsync("writer", ct);
        await using var host = await TcpPeerHost.StartAsync(new(IPAddress.Loopback, 0), folder,
            new(Guid.NewGuid(), 0, RandomNumberGenerator.GetBytes(32), owner), ct);
        using var ownerAccess = host.CreateOwnerAccess("127.0.0.1");
        await using var ownerWire = new TcpPeerTransport(ownerAccess, owner);
        await using var ownerSession = OpenSession("owner", owner, ownerWire);
        await ownerSession.Updates.SynchronizeAsync(true, ct);
        await using var writerWire = await TcpPeerTransport.JoinInvitationAsync(host.CreateInvitation("127.0.0.1"), writer, ct);
        await using var writerSession = OpenSession("writer", writer, writerWire);
        await writerSession.Updates.SynchronizeAsync(false, ct);
        var item = Guid.NewGuid();
        var payload = RandomNumberGenerator.GetBytes(600_000);
        writerSession.Updates.Save(item, payload);
        await writerWire.PushAsync(writerSession.Pending[0], ct);
        await ownerSession.Updates.SynchronizeAsync(false, ct);
        Assert.Empty(ownerSession.Updates.ReadItem(item));
        var capture = ownerSession.Updates.CaptureSnapshotAsync((sequence, token) =>
        {
            Assert.Equal(payload, Assert.Single(ownerSession.Updates.ReadItem(item)));
            return Task.FromResult(sequence);
        }, ct);

        await writerSession.Updates.SynchronizeAsync(false, ct);

        Assert.True(await capture > 1);
    }

    [Fact]
    public async Task PeerPresenceReachesOtherWritersWithoutEnteringProjectHistory()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await DeviceAsync("owner", ct);
        using var writer = await DeviceAsync("writer", ct);
        var project = Guid.NewGuid();
        await using var host = await TcpPeerHost.StartAsync(new(IPAddress.Loopback, 0), folder, new(project, 0,
            RandomNumberGenerator.GetBytes(32), owner), ct);
        using var ownerAccess = host.CreateOwnerAccess("127.0.0.1");
        await using var ownerWire = new TcpPeerTransport(ownerAccess, owner);
        await using var ownerSession = OpenSession("owner", owner, ownerWire);
        await ownerSession.Updates.SynchronizeAsync(true, ct);
        await using var writerWire = await TcpPeerTransport.JoinInvitationAsync(host.CreateInvitation("127.0.0.1"), writer, ct);
        await using var writerSession = OpenSession("writer", writer, writerWire);
        await writerSession.Updates.SynchronizeAsync(false, ct);
        await using var ownerPresence = ownerSession.Presence(owner, ownerWire);
        await using var writerPresence = writerSession.Presence(writer, writerWire);
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.IsAssignableFrom<IPresenceTransport>(ownerWire).PresenceReceived += _ => { arrived.TrySetResult(); return Task.CompletedTask; };
        var item = Guid.NewGuid();
        var view = Guid.NewGuid();
        await writerPresence.PublishAsync(new(item, view, "Writer", new([1, 2], [3]), true), ct);
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        var participant = Assert.Single(ownerPresence.Snapshot());
        Assert.Equal(writer.Id, participant.DeviceId);
        Assert.Equal(item, participant.ItemId);
        Assert.Equal(new byte[] { 1, 2 }, participant.Selection!.Anchor);
        Assert.Equal(0, ownerSession.Updates.CommittedSequence);
        Assert.Empty(ownerSession.Updates.ReadItem(item));
    }

    private sealed class PeerSession : IAsyncDisposable
    {
        private readonly DeviceSequence sequence;
        private readonly SessionJournal journal;
        private readonly EnvelopeCipher cipher;
        public CollaborationSession Updates { get; }
        public IReadOnlyList<UpdateEnvelope> Pending => journal.Pending();

        public PeerSession(string storage, DeviceIdentity device, TcpPeerTransport transport)
        {
            sequence = new(Path.Combine(storage, "sequence.sqlite"), device.Id);
            journal = new(Path.Combine(storage, "journal.sqlite"), Guid.NewGuid(), transport.Access.ProjectId, sequence);
            cipher = transport.Access.CreateCipher();
            Updates = new(new(transport.Access.ProjectId, device.Id, transport.Access.Epoch), journal, cipher,
                transport.Access.CreateSessionIdentity(device), transport);
        }

        public async ValueTask DisposeAsync()
        {
            await Updates.DisposeAsync();
            cipher.Dispose();
            journal.Dispose();
            sequence.Dispose();
        }

        public SessionPresence Presence(DeviceIdentity device, TcpPeerTransport transport) =>
            new(Updates.Context, transport.Access.CreateSessionIdentity(device), sequence, cipher,
                Assert.IsAssignableFrom<IPresenceTransport>(transport));
    }

    [Fact]
    public async Task ReconnectingCatchesUpFragmentedHistoryAndRetriesKeepTheOriginalSequence()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await DeviceAsync("owner", ct);
        using var writer = await DeviceAsync("writer", ct);
        var project = Guid.NewGuid();
        await using var host = await TcpPeerHost.StartAsync(new(IPAddress.Loopback, 0), folder, new(project, 0,
            RandomNumberGenerator.GetBytes(32), owner), ct);
        using var ownerAccess = host.CreateOwnerAccess("127.0.0.1");
        await using var ownerWire = new TcpPeerTransport(ownerAccess, owner);
        await using var ownerSession = OpenSession("owner", owner, ownerWire);
        await ownerSession.Updates.SynchronizeAsync(true, ct);
        await using var writerWire = await TcpPeerTransport.JoinInvitationAsync(host.CreateInvitation("127.0.0.1"), writer, ct);
        await using var writerSession = OpenSession("writer", writer, writerWire);
        await writerSession.Updates.SynchronizeAsync(false, ct);
        await writerSession.Updates.PauseAsync(ct);
        var item = Guid.NewGuid();
        var content = RandomNumberGenerator.GetBytes(600_000);
        ownerSession.Updates.Save(item, content);
        var firstPacket = ownerSession.Pending[0];
        await ownerSession.Updates.SynchronizeAsync(false, ct);
        Assert.Equal(1, await ownerWire.PushAsync(firstPacket, ct));
        Assert.Equal(1, await ownerWire.PushAsync(firstPacket, ct));
        writerSession.Updates.Save(item, "Offline prose"u8.ToArray());
        await writerSession.Updates.SynchronizeAsync(false, ct);
        Assert.Equal(content, writerSession.Updates.ReadCommittedItem(item)[0].Payload);
        Assert.Equal("Offline prose"u8.ToArray(), writerSession.Updates.ReadCommittedItem(item)[1].Payload);
        Assert.Empty(writerSession.Pending);
        await ownerSession.Updates.SynchronizeAsync(false, ct);
        Assert.Equal(2, ownerSession.Updates.ReadCommittedItem(item).Count);
    }

    [Fact]
    public async Task AnInvitationIsSingleUseAndRejectsAnUnknownSecretWithoutGrantingAccess()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await DeviceAsync("owner", ct);
        using var writer = await DeviceAsync("writer", ct);
        using var stranger = await DeviceAsync("stranger", ct);
        await using var host = await TcpPeerHost.StartAsync(new(IPAddress.Loopback, 0), folder, new(Guid.NewGuid(), 0,
            RandomNumberGenerator.GetBytes(32), owner), ct);
        var invitation = host.CreateInvitation("127.0.0.1");
        await Assert.ThrowsAsync<CryptographicException>(() => TcpPeerTransport.JoinInvitationAsync(
            invitation with { Secret = RandomNumberGenerator.GetBytes(32) }, stranger, ct));
        await using var admitted = await TcpPeerTransport.JoinInvitationAsync(invitation, writer, ct);
        await Assert.ThrowsAsync<CryptographicException>(() => TcpPeerTransport.JoinInvitationAsync(invitation, stranger, ct));
        await admitted.DisconnectAsync(ct);
        await using var retry = await TcpPeerTransport.JoinInvitationAsync(invitation, writer, ct);
        Assert.True(retry.IsConnected);
        await using var ownerRetry = await TcpPeerTransport.JoinInvitationAsync(host.CreateInvitation("127.0.0.1"), owner, ct);
        Assert.True(ownerRetry.Access.IsOwner);
    }

    [Fact]
    public async Task PeerRejectsAChangedHostPinAndForgedOrForeignPackets()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await DeviceAsync("owner", ct);
        using var writer = await DeviceAsync("writer", ct);
        var project = Guid.NewGuid();
        await using var host = await TcpPeerHost.StartAsync(new(IPAddress.Loopback, 0), folder, new(project, 0,
            RandomNumberGenerator.GetBytes(32), owner), ct);
        var invitation = host.CreateInvitation("127.0.0.1");
        await Assert.ThrowsAsync<System.Security.Authentication.AuthenticationException>(() => TcpPeerTransport.JoinInvitationAsync(
            invitation with { Address = invitation.Address with { CertificateHash = new byte[32] } }, writer, ct));
        using var ownerAccess = host.CreateOwnerAccess("127.0.0.1");
        await using var ownerWire = new TcpPeerTransport(ownerAccess, owner);
        await using var ownerSession = OpenSession("owner", owner, ownerWire);
        await ownerSession.Updates.SynchronizeAsync(true, ct);
        await using var writerWire = await TcpPeerTransport.JoinInvitationAsync(invitation, writer, ct);
        await using var writerSession = OpenSession("writer", writer, writerWire);
        await writerSession.Updates.SynchronizeAsync(false, ct);
        var item = Guid.NewGuid();
        ownerSession.Updates.Save(item, "Owner draft"u8.ToArray());
        var ownerPacket = Assert.Single(ownerSession.Pending);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writerWire.PushAsync(ownerPacket, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ownerWire.PushAsync(ownerPacket with { Signature = new byte[64] }, ct));
        Assert.Equal(0, writerSession.Updates.CommittedSequence);
        await ownerSession.Updates.SynchronizeAsync(false, ct);
        await writerSession.Updates.SynchronizeAsync(false, ct);
        Assert.Equal("Owner draft"u8.ToArray(), Assert.Single(writerSession.Updates.ReadCommittedItem(item)).Payload);
    }

    [Fact]
    public async Task InvitationLinkJoinsAPopulatedProjectAndDownloadsItsCompleteCheckpoint()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await DeviceAsync("owner", ct);
        using var writer = await DeviceAsync("writer", ct);
        var project = Guid.NewGuid();
        await using var host = await TcpPeerHost.StartAsync(new(IPAddress.Loopback, 0), folder,
            new(project, 0, RandomNumberGenerator.GetBytes(32), owner), ct);
        using var ownerAccess = host.CreateOwnerAccess("127.0.0.1");
        await using var ownerWire = new TcpPeerTransport(ownerAccess, owner);
        await using var ownerSession = OpenSession("owner", owner, ownerWire);
        await ownerSession.Updates.SynchronizeAsync(true, ct);
        var archive = RandomNumberGenerator.GetBytes(1_100_000);
        var checkpoint = await CheckpointTransfer.PublishAsync(ownerSession.Updates, new MemoryStream(archive), ct);
        var link = host.CreateInvitation("127.0.0.1", checkpoint).ToLink();
        var invitation = PeerInvitation.Parse(link);
        Assert.Equal(checkpoint, invitation.Checkpoint);
        await using var writerWire = await TcpPeerTransport.JoinInvitationAsync(invitation, writer, ct);
        await using var writerSession = OpenSession("writer", writer, writerWire);
        await writerSession.Updates.SynchronizeAsync(false, ct);
        using var downloaded = new MemoryStream();
        await CheckpointTransfer.DownloadAsync(writerSession.Updates, Assert.IsType<CheckpointReference>(invitation.Checkpoint), downloaded, archive.Length, ct);
        Assert.Equal(archive, downloaded.ToArray());
    }

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }
}
