using System.Security.Cryptography;
using Microsoft.AspNetCore.SignalR;
using Novalist.Sync.Client;
using Xunit;

namespace Novalist.Sync.IntegrationTests;

public sealed class PrivateInvitationTests(CommunityServerFixture server) : IClassFixture<CommunityServerFixture>
{
    [Fact]
    public async Task FocusHandoffPublishesConfirmedFieldsIntoBothLocalCopies()
    {
        var ct = TestContext.Current.CancellationToken;
        var credentials = new MemoryCredentials();
        var store = new HostedProjectAccessStore(credentials);
        var devices = new DeviceIdentityStore(credentials);
        var subject = Guid.NewGuid().ToString();
        using var ownerDevice = await devices.GetOrCreateAsync(new("https://identity.example"), "owner-" + subject, ct);
        using var guestDevice = await devices.GetOrCreateAsync(new("https://identity.example"), subject, ct);
        await server.EnrollAsync(ownerDevice, "owner-" + subject, ct);
        await server.EnrollAsync(guestDevice, subject, ct);
        using var access = await store.CreateOwnerAsync(server.Address, ownerDevice, ct);
        await using var ownerTransport = new HostedTransport(server.Address, ownerDevice, () => Task.FromResult(server.Token("owner-" + subject)));
        await using var guestTransport = new HostedTransport(server.Address, guestDevice, () => Task.FromResult(server.Token(subject)));
        await using var owner = new Participant(access, ownerDevice, ownerTransport);
        await owner.Session.SynchronizeAsync(true, ct);
        var link = await HostedProjectInvitations.CreateAsync(access, ownerDevice, ownerTransport,
            new(new(Guid.NewGuid(), new string('0', 64)), DateTimeOffset.UtcNow.AddHours(1)), ct);
        using var guestAccess = await store.PrepareJoinAsync(link, guestDevice, ct);
        await guestTransport.ConnectAsync(ct);
        await store.ActivateAsync(guestAccess, guestTransport, ct);
        await using var guest = new Participant(guestAccess, guestDevice, guestTransport);
        await guest.Session.SynchronizeAsync(false, ct);
        var item = Guid.NewGuid();
        var guestProjection = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ownerProjection = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var ownerFields = new SharedFieldWorkspace(owner.Session, (_, value, _) =>
        {
            if (value == "44") ownerProjection.TrySetResult(value);
            return Task.CompletedTask;
        });
        await using var guestFields = new SharedFieldWorkspace(guest.Session, (_, value, _) =>
        {
            guestProjection.TrySetResult(value);
            return Task.CompletedTask;
        });
        var initial = new Dictionary<Guid, string> { [item] = "42" };
        await ownerFields.StartAsync(initial, ct);
        await guestFields.StartAsync(initial, ct);
        var ownerFocus = FieldLeaseRequest.Create(access.ProjectId, item, access.Epoch);
        var guestFocus = ownerFocus with { LeaseId = Guid.NewGuid() };
        Assert.True((await ownerFields.AcquireAsync(ownerFocus, ct)).Lease.Acquired);
        Assert.False((await guestFields.AcquireAsync(guestFocus, ct)).Lease.Acquired);
        await ownerFields.WriteAsync(ownerFocus, "43", ct);
        Assert.Equal("43", await guestProjection.Task.WaitAsync(TimeSpan.FromSeconds(5), ct));
        await owner.Session.ReleaseFieldAsync(ownerFocus, ct);
        guestFocus = guestFocus with { LeaseId = Guid.NewGuid() };
        var handedOff = await guestFields.AcquireAsync(guestFocus, ct);
        Assert.True(handedOff.Lease.Acquired);
        Assert.Equal("43", handedOff.Value);
        await guestFields.WriteAsync(guestFocus, "44", ct);
        Assert.Equal("44", await ownerProjection.Task.WaitAsync(TimeSpan.FromSeconds(5), ct));
    }

    [Fact]
    public async Task ARevokedPreparedInvitationCanBeReplacedWithoutLosingAnAmbiguousSuccessfulClaim()
    {
        var ct = TestContext.Current.CancellationToken;
        var credentials = new MemoryCredentials();
        var store = new HostedProjectAccessStore(credentials);
        var devices = new DeviceIdentityStore(credentials);
        var subject = Guid.NewGuid().ToString();
        using var owner = await devices.GetOrCreateAsync(new("https://identity.example"), "owner-" + subject, ct);
        using var guest = await devices.GetOrCreateAsync(new("https://identity.example"), subject, ct);
        await server.EnrollAsync(owner, "owner-" + subject, ct);
        await server.EnrollAsync(guest, subject, ct);
        using var access = await store.CreateOwnerAsync(server.Address, owner, ct);
        await using var ownerTransport = new HostedTransport(server.Address, owner, () => Task.FromResult(server.Token("owner-" + subject)));
        await using var guestTransport = new HostedTransport(server.Address, guest, () => Task.FromResult(server.Token(subject)));
        await ownerTransport.ConnectAsync(ct);
        await ownerTransport.CreateProjectAsync(access.ProjectId, ct);
        var options = new HostedInvitationOptions(new(Guid.NewGuid(), new string('0', 64)), DateTimeOffset.UtcNow.AddHours(1));
        var firstLink = await HostedProjectInvitations.CreateAsync(access, owner, ownerTransport, options, ct);
        using var first = await store.PrepareJoinAsync(firstLink, guest, ct);
        await ownerTransport.RevokeInvitationAsync(access.ProjectId, Assert.IsType<Guid>(first.InvitationId), ct);
        await guestTransport.ConnectAsync(ct);
        await Assert.ThrowsAsync<HubException>(() => store.ActivateAsync(first, guestTransport, ct));
        var replacementLink = await HostedProjectInvitations.CreateAsync(access, owner, ownerTransport, options, ct);
        using var replacement = await new HostedProjectAccessStore(credentials).PrepareJoinAsync(replacementLink, guest, ct);
        credentials.FailNextProjectWrite = true;
        await Assert.ThrowsAsync<IOException>(() => store.ActivateAsync(replacement, guestTransport, ct));
        var restarted = new HostedProjectAccessStore(credentials);
        using var restored = await restarted.PrepareJoinAsync(replacementLink, guest, ct);
        Assert.Equal(replacement.Certificate, restored.Certificate);
        await restarted.ActivateAsync(restored, guestTransport, ct);
        using var active = await restarted.LoadAsync(server.Address, access.ProjectId, guest, ct);
        Assert.Equal(restored.Certificate, Assert.IsType<HostedProjectAccess>(active).Certificate);
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.ActivateAsync(first, guestTransport, ct));
    }

    [Fact]
    public async Task PrivateInvitationRestoresProjectKeysAndCheckpointWhileOwnerIsOffline()
    {
        var ct = TestContext.Current.CancellationToken;
        var credentials = new MemoryCredentials();
        var store = new HostedProjectAccessStore(credentials);
        var devices = new DeviceIdentityStore(credentials);
        var subject = Guid.NewGuid().ToString();
        using var ownerDevice = await devices.GetOrCreateAsync(new("https://identity.example"), "owner-" + subject, ct);
        using var guestDevice = await devices.GetOrCreateAsync(new("https://identity.example"), subject, ct);
        await server.EnrollAsync(ownerDevice, "owner-" + subject, ct);
        await server.EnrollAsync(guestDevice, subject, ct);
        using var ownerAccess = await store.CreateOwnerAsync(server.Address, ownerDevice, ct);
        await using var ownerTransport = new HostedTransport(server.Address, ownerDevice, () => Task.FromResult(server.Token("owner-" + subject)));
        await using var guestTransport = new HostedTransport(server.Address, guestDevice, () => Task.FromResult(server.Token(subject)));
        await using var owner = new Participant(ownerAccess, ownerDevice, ownerTransport);
        await owner.Session.SynchronizeAsync(createProject: true, ct);
        var archive = RandomNumberGenerator.GetBytes(100_000);
        using var source = new MemoryStream(archive);
        var reference = await CheckpointTransfer.PublishAsync(owner.Session, source, ct);
        var link = await HostedProjectInvitations.CreateAsync(ownerAccess, ownerDevice, ownerTransport,
            new(reference, DateTimeOffset.UtcNow.AddHours(1)), ct);
        Assert.StartsWith("novalist://collaboration/join#", link, StringComparison.Ordinal);
        using var prepared = await store.PrepareJoinAsync(link, guestDevice, ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ActivateAsync(prepared, ownerTransport, ct));
        await owner.Session.PauseAsync(ct);
        await guestTransport.ConnectAsync(ct);
        await store.ActivateAsync(prepared, guestTransport, ct);
        using var restored = await new HostedProjectAccessStore(credentials).PrepareJoinAsync(link, guestDevice, ct);
        Assert.Equal(prepared.Certificate, restored.Certificate);
        await store.ActivateAsync(restored, guestTransport, ct);
        await using var guest = new Participant(restored, guestDevice, guestTransport);
        await guest.Session.SynchronizeAsync(createProject: false, ct);
        using var destination = new MemoryStream();
        await CheckpointTransfer.DownloadAsync(guest.Session, Assert.IsType<CheckpointReference>(restored.Checkpoint), destination, 200_000, ct);
        Assert.Equal(archive, destination.ToArray());
        var scene = Guid.NewGuid();
        await using var ownerLive = new SessionSynchronizer(owner.Session);
        await using var guestLive = new SessionSynchronizer(guest.Session);
        await ownerLive.StartAsync(createProject: false, ct);
        await guestLive.StartAsync(createProject: false, ct);
        var arrived = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.Session.UpdateReceived += (item, bytes) => { if (item == scene) arrived.TrySetResult(bytes); };
        await guestTransport.DisconnectAsync(ct);
        guest.Session.Save(scene, "writer's new scene"u8.ToArray());
        Assert.Equal("writer's new scene"u8.ToArray(), await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10), ct));
        Assert.Equal("writer's new scene"u8.ToArray(), Assert.Single(owner.Session.ReadItem(scene)));
        await owner.Session.PauseAsync(ct);
        await Task.Delay(TimeSpan.FromMilliseconds(1100), ct);
        Assert.False(owner.Session.IsConnected);
    }

    private sealed class Participant : IAsyncDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "novalist-private-invite", Guid.NewGuid().ToString("N"));
        private readonly DeviceSequence sequence;
        private readonly SessionJournal journal;
        private readonly EnvelopeCipher cipher;
        public CollaborationSession Session { get; }

        public Participant(HostedProjectAccess access, DeviceIdentity device, HostedTransport transport)
        {
            sequence = new DeviceSequence(Path.Combine(folder, "sequence.sqlite"), device.Id);
            journal = new SessionJournal(Path.Combine(folder, "session.sqlite"), Guid.NewGuid(), access.ProjectId, sequence);
            cipher = access.CreateCipher();
            Session = new(new(access.ProjectId, device.Id, access.Epoch), journal, cipher, access.CreateSessionIdentity(device), transport);
        }

        public async ValueTask DisposeAsync()
        {
            await Session.DisposeAsync();
            cipher.Dispose();
            journal.Dispose();
            sequence.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    private sealed class MemoryCredentials : ICollaborationCredentials
    {
        private readonly Dictionary<string, string> values = [];
        public bool FailNextProjectWrite { get; set; }
        public Task<string?> ReadAsync(string key, CancellationToken ct) => Task.FromResult(values.GetValueOrDefault(key));
        public Task WriteAsync(string key, string value, CancellationToken ct)
        {
            if (FailNextProjectWrite && key.StartsWith("project:", StringComparison.Ordinal))
            {
                FailNextProjectWrite = false;
                throw new IOException("The credential store is temporarily unavailable.");
            }
            values[key] = value;
            return Task.CompletedTask;
        }
        public Task RemoveAsync(string key, CancellationToken ct) { values.Remove(key); return Task.CompletedTask; }
    }
}
