using System.Security.Cryptography;
using Microsoft.AspNetCore.SignalR;
using Novalist.Sync.Client;
using NSec.Cryptography;
using Xunit;

namespace Novalist.Sync.IntegrationTests;

public sealed class HostedTransportTests(CommunityServerFixture server) : IClassFixture<CommunityServerFixture>
{
    [Fact]
    public async Task LargeDocumentUpdatesCrossTheUnchangedServersMessageLimitAndReassemble()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = Guid.NewGuid();
        var ownerDevice = Guid.NewGuid();
        var guestDevice = Guid.NewGuid();
        var ownerToken = server.Token(Guid.NewGuid().ToString());
        var guestSubject = Guid.NewGuid().ToString();
        var guestToken = server.Token(guestSubject);
        var folder = Path.Combine(Path.GetTempPath(), "novalist-large-update-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var ownerSigning = Key.Create(SignatureAlgorithm.Ed25519);
            using var guestSigning = Key.Create(SignatureAlgorithm.Ed25519);
            using var cipher = new EnvelopeCipher(project, 0, RandomNumberGenerator.GetBytes(32));
            using var ownerSequences = new DeviceSequence(Path.Combine(folder, "owner-device.sqlite"), ownerDevice);
            using var guestSequences = new DeviceSequence(Path.Combine(folder, "guest-device.sqlite"), guestDevice);
            using var ownerJournal = new SessionJournal(Path.Combine(folder, "owner.sqlite"), Guid.NewGuid(), project, ownerSequences);
            using var guestJournal = new SessionJournal(Path.Combine(folder, "guest.sqlite"), Guid.NewGuid(), project, guestSequences);
            await using var ownerTransport = new HostedTransport(server.Address, ownerDevice, () => Task.FromResult(ownerToken));
            await using var guestTransport = new HostedTransport(server.Address, guestDevice, () => Task.FromResult(guestToken));
            var writers = new Dictionary<Guid, PublicKey> { [ownerDevice] = ownerSigning.PublicKey, [guestDevice] = guestSigning.PublicKey };
            await using var owner = new CollaborationSession(new(project, ownerDevice, 0), ownerJournal, cipher, ownerSigning, writers, ownerTransport);
            await using var guest = new CollaborationSession(new(project, guestDevice, 0), guestJournal, cipher, guestSigning, writers, guestTransport);
            await owner.SynchronizeAsync(createProject: true, ct);
            await guestTransport.ConnectAsync(ct);
            await server.AddContributorAsync(project, guestSubject, ct);
            await guest.SynchronizeAsync(createProject: false, ct);
            var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            guest.UpdateReceived += (_, payload) => received.TrySetResult(payload);
            var item = Guid.NewGuid();
            var update = RandomNumberGenerator.GetBytes(100_000);
            owner.Save(item, update);
            await owner.SynchronizeAsync(createProject: false, ct);
            Assert.Equal(update, await received.Task.WaitAsync(TimeSpan.FromSeconds(10), ct));
            Assert.Equal(update, Assert.Single(guest.ReadItem(item)));
            Assert.Empty(ownerJournal.Pending());
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task SessionRejoinsAndUploadsChangesSavedWhileDisconnected()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = Guid.NewGuid();
        var device = Guid.NewGuid();
        var token = server.Token(Guid.NewGuid().ToString());
        var item = Guid.NewGuid();
        var directory = Path.Combine(Path.GetTempPath(), "novalist-session-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var signing = Key.Create(SignatureAlgorithm.Ed25519);
            using var cipher = new EnvelopeCipher(project, 0, RandomNumberGenerator.GetBytes(32));
            using var sequences = new DeviceSequence(Path.Combine(directory, "device.sqlite"), device);
            using var journal = new SessionJournal(Path.Combine(directory, "updates.sqlite"), Guid.NewGuid(), project, sequences);
            await using var transport = new HostedTransport(server.Address, device, () => Task.FromResult(token));
            await using var session = new CollaborationSession(new(project, device, 0), journal, cipher, signing,
                new Dictionary<Guid, PublicKey> { [device] = signing.PublicKey }, transport);
            await session.SynchronizeAsync(createProject: true, ct);
            await session.PauseAsync(ct);
            session.Save(item, "An offline edit"u8.ToArray());
            Assert.Single(journal.Pending());
            await session.SynchronizeAsync(createProject: false, ct);
            Assert.Empty(journal.Pending());
            Assert.Equal(1, journal.LastServerSequence);
            Assert.Equal("An offline edit"u8.ToArray(), Assert.Single(session.ReadItem(item)));
            await session.CloseAsync(ct);
            Assert.Throws<InvalidOperationException>(() => session.Save(item, [1]));
            Assert.Single(session.ReadItem(item));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task StoredEncryptedUpdatesRemainAvailableWithTheOwnerOffline()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = Guid.NewGuid();
        var device = Guid.NewGuid();
        var ownerToken = server.Token(Guid.NewGuid().ToString());
        var guestSubject = Guid.NewGuid().ToString();
        var guestToken = server.Token(guestSubject);
        using var key = Key.Create(SignatureAlgorithm.Ed25519);
        using var cipher = new EnvelopeCipher(project, 0, RandomNumberGenerator.GetBytes(32));
        var envelope = cipher.Seal(new(project, Guid.NewGuid(), 0, device, 1), "Private story update"u8, key);
        await using var owner = new HostedTransport(server.Address, device, () => Task.FromResult(ownerToken));
        await using var guest = new HostedTransport(server.Address, Guid.NewGuid(), () => Task.FromResult(guestToken));
        await owner.ConnectAsync(ct);
        await owner.CreateProjectAsync(project, ct);
        await guest.ConnectAsync(ct);
        await Assert.ThrowsAsync<HubException>(() => guest.JoinAsync(project, ct));
        await server.AddContributorAsync(project, guestSubject, ct);
        await guest.JoinAsync(project, ct);
        var received = new TaskCompletionSource<SequencedUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        guest.Received += update => { received.TrySetResult(update); return Task.CompletedTask; };

        var first = await owner.PushAsync(envelope, ct);
        Assert.Equal(1, first);
        Assert.Equal(first, await owner.PushAsync(envelope, ct));
        var live = await received.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.Equal("Private story update"u8.ToArray(), cipher.Open(live.Envelope, key.PublicKey));
        await owner.DisconnectAsync(ct);
        await guest.DisconnectAsync(ct);
        await guest.ConnectAsync(ct);
        await guest.JoinAsync(project, ct);
        var history = new List<SequencedUpdate>();
        await foreach (var update in guest.CatchUpAsync(project, 0, ct)) history.Add(update);
        var stored = Assert.Single(history);
        Assert.Equal(first, stored.ServerSeq);
        Assert.Equal(envelope.Ciphertext, stored.Envelope.Ciphertext);
        Assert.Equal("Private story update"u8.ToArray(), cipher.Open(stored.Envelope, key.PublicKey));
    }
}
