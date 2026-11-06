using System.Security.Cryptography;
using System.Text.Json;
using NSubstitute;
using NSec.Cryptography;
using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class SessionPresenceTests
{
    [Fact]
    public async Task ClosingCancelsAPresenceSendThatTheServerHasNotAcknowledged()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "novalist-presence", Guid.NewGuid().ToString("N"));
        var credentials = Substitute.For<ICollaborationCredentials>();
        credentials.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));
        using var device = await new DeviceIdentityStore(credentials).GetOrCreateAsync(new("https://identity.example"), "owner", ct);
        var project = Guid.NewGuid();
        var membership = new ProjectMembership(project, 0, device.SigningKey.PublicKey);
        using var cipher = new EnvelopeCipher(project, 0, RandomNumberGenerator.GetBytes(32));
        var wire = new Wire { Hold = true };
        try
        {
            using var sequence = new DeviceSequence(Path.Combine(root, "device.sqlite"), device.Id);
            await using var presence = new SessionPresence(new(project, device.Id, 0), new(membership, membership.CreateOwnerCertificate(device), device), sequence, cipher, wire);
            var sending = presence.PublishAsync(new(Guid.NewGuid(), Guid.NewGuid(), "Owner", null, true), ct);
            await wire.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2), ct);
            var first = presence.DisposeAsync().AsTask();
            var second = presence.DisposeAsync().AsTask();
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2), ct);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);
            Assert.Empty(presence.Snapshot());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task PresenceAuthenticatesWritersExpiresAndRejectsReplaysWithoutPersistingProse()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "novalist-presence", Guid.NewGuid().ToString("N"));
        var credentials = Substitute.For<ICollaborationCredentials>();
        credentials.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));
        var devices = new DeviceIdentityStore(credentials);
        using var owner = await devices.GetOrCreateAsync(new("https://identity.example"), "owner", ct);
        using var guest = await devices.GetOrCreateAsync(new("https://identity.example"), "guest", ct);
        using var delegation = Key.Create(SignatureAlgorithm.Ed25519);
        var project = Guid.NewGuid();
        var clock = new Clock();
        var membership = new ProjectMembership(project, 0, owner.SigningKey.PublicKey, clock);
        var grant = membership.CreateInvitation(new(Guid.NewGuid(), clock.Now.AddMinutes(-1), clock.Now.AddHours(1)), delegation.PublicKey, owner.SigningKey);
        var guestIdentity = new CertifiedSessionIdentity(membership, membership.ClaimInvitation(grant, delegation, guest), guest);
        var ownerIdentity = new CertifiedSessionIdentity(membership, membership.CreateOwnerCertificate(owner), owner);
        using var cipher = new EnvelopeCipher(project, 0, RandomNumberGenerator.GetBytes(32));
        var wire = new Wire();
        try
        {
            using var ownerSequence = new DeviceSequence(Path.Combine(root, "owner.sqlite"), owner.Id);
            using var guestSequence = new DeviceSequence(Path.Combine(root, "guest.sqlite"), guest.Id);
            await using var receiving = new SessionPresence(new(project, owner.Id, 0), ownerIdentity, ownerSequence, cipher, wire, clock);
            await using var sending = new SessionPresence(new(project, guest.Id, 0), guestIdentity, guestSequence, cipher, wire, clock);
            var item = Guid.NewGuid();
            var view = Guid.NewGuid();
            await sending.PublishAsync(new(item, view, "Guest", new([1, 2], [3, 4]), true), ct);
            var envelope = Assert.IsType<UpdateEnvelope>(wire.Last);
            var participant = Assert.Single(receiving.Snapshot());
            Assert.Equal(guest.Id, participant.DeviceId);
            Assert.Equal(item, participant.ItemId);
            Assert.Equal(new byte[] { 1, 2 }, participant.Selection!.Anchor);
            Assert.Empty(sending.Snapshot());
            Assert.Throws<FormatException>(() => cipher.Open(envelope, guest.SigningKey.PublicKey));
            clock.Now += TimeSpan.FromSeconds(16);
            Assert.Empty(receiving.Snapshot());
            await wire.DeliverAsync(envelope);
            Assert.Empty(receiving.Snapshot());
            await sending.PublishAsync(new(item, view, "Guest", null, true), ct);
            Assert.Single(receiving.Snapshot());
            var tampered = Assert.IsType<UpdateEnvelope>(wire.Last) with { Signature = new byte[64] };
            await wire.DeliverAsync(tampered);
            Assert.True(receiving.RejectedMessages);
            Assert.Single(receiving.Snapshot());
            var incomplete = JsonSerializer.Serialize(new { envelope.Header, Nonce = (byte[]?)null, envelope.Ciphertext, envelope.Signature });
            await wire.DeliverAsync(Assert.IsType<UpdateEnvelope>(JsonSerializer.Deserialize<UpdateEnvelope>(incomplete)));
            Assert.Single(receiving.Snapshot());
            await sending.PublishAsync(new(item, view, "Guest", null, false), ct);
            Assert.Empty(receiving.Snapshot());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Wire : IPresenceTransport
    {
        public event Func<UpdateEnvelope, Task>? PresenceReceived;
        public bool IsConnected => true;
        public UpdateEnvelope? Last { get; private set; }
        public bool Hold { get; init; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task PublishPresenceAsync(UpdateEnvelope envelope, CancellationToken ct)
        {
            Last = envelope;
            Entered.TrySetResult();
            return Hold ? Task.Delay(Timeout.Infinite, ct) : DeliverAsync(envelope);
        }
        public async Task DeliverAsync(UpdateEnvelope envelope)
        {
            if (PresenceReceived is { } handlers)
                foreach (var handler in handlers.GetInvocationList().Cast<Func<UpdateEnvelope, Task>>()) await handler(envelope);
        }
    }
}
