using System.Security.Cryptography;
using NSubstitute;
using NSec.Cryptography;
using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class CertifiedSessionTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "novalist-certified-session", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InvitedWriterEditsAreAcceptedWithoutAPreconfiguredPeerList()
    {
        var ct = TestContext.Current.CancellationToken;
        var credentials = Substitute.For<ICollaborationCredentials>();
        credentials.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));
        var devices = new DeviceIdentityStore(credentials);
        using var owner = await devices.GetOrCreateAsync(new("https://identity.example"), "owner", ct);
        using var writer = await devices.GetOrCreateAsync(new("https://identity.example"), "writer", ct);
        using var delegation = Key.Create(SignatureAlgorithm.Ed25519);
        var project = Guid.NewGuid();
        var membership = new ProjectMembership(project, 0, owner.SigningKey.PublicKey);
        var grant = membership.CreateInvitation(new(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1)),
            delegation.PublicKey, owner.SigningKey);
        var certificate = membership.ClaimInvitation(grant, delegation, writer);
        var key = RandomNumberGenerator.GetBytes(32);
        using var ownerCipher = new EnvelopeCipher(project, 0, key);
        using var writerCipher = new EnvelopeCipher(project, 0, key);
        using var ownerSequence = new DeviceSequence(Path.Combine(folder, "owner-sequence.sqlite"), owner.Id);
        using var writerSequence = new DeviceSequence(Path.Combine(folder, "writer-sequence.sqlite"), writer.Id);
        using var ownerJournal = new SessionJournal(Path.Combine(folder, "owner.sqlite"), Guid.NewGuid(), project, ownerSequence);
        using var writerJournal = new SessionJournal(Path.Combine(folder, "writer.sqlite"), Guid.NewGuid(), project, writerSequence);
        var ownerWire = Substitute.For<IUpdateTransport>();
        var writerWire = Substitute.For<IUpdateTransport>();
        ownerWire.ConnectionIdentity.Returns("test:shared-project");
        writerWire.ConnectionIdentity.Returns("test:shared-project");
        await using var ownerSession = new CollaborationSession(new(project, owner.Id, 0), ownerJournal, ownerCipher,
            new CertifiedSessionIdentity(membership, membership.CreateOwnerCertificate(owner), owner), ownerWire);
        await using var writerSession = new CollaborationSession(new(project, writer.Id, 0), writerJournal, writerCipher,
            new CertifiedSessionIdentity(membership, certificate, writer), writerWire);
        var item = Guid.NewGuid();
        var content = RandomNumberGenerator.GetBytes(100_000);
        writerSession.Save(item, content);
        var delivered = new List<byte[]>();
        ownerSession.UpdateReceived += (_, update) => delivered.Add(update);
        var first = writerJournal.Pending()[0];
        var badSignature = first.Signature.ToArray();
        badSignature[0] ^= 1;
        ownerWire.Received += Raise.Event<Func<SequencedUpdate, Task>>(new SequencedUpdate(1, first with { Signature = badSignature }));
        Assert.IsType<CryptographicException>(ownerSession.ReceiveError);
        Assert.Equal(0, ownerJournal.LastServerSequence);
        Assert.Empty(ownerSession.ReadItem(item));
        Assert.Empty(delivered);
        long sequence = 0;
        foreach (var envelope in writerJournal.Pending())
            ownerWire.Received += Raise.Event<Func<SequencedUpdate, Task>>(new SequencedUpdate(++sequence, envelope));
        Assert.Equal(content, Assert.Single(delivered));
        Assert.Equal(content, Assert.Single(ownerSession.ReadItem(item)));
        Assert.Equal(sequence, ownerJournal.LastServerSequence);
    }

    [Fact]
    public async Task RestartCannotReplaceThePinnedOwnerWithAnOtherwiseValidCertificate()
    {
        var ct = TestContext.Current.CancellationToken;
        var credentials = Substitute.For<ICollaborationCredentials>();
        credentials.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));
        var devices = new DeviceIdentityStore(credentials);
        using var device = await devices.GetOrCreateAsync(new("https://identity.example"), "writer", ct);
        using var otherOwner = Key.Create(SignatureAlgorithm.Ed25519);
        using var delegation = Key.Create(SignatureAlgorithm.Ed25519);
        var project = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var path = Path.Combine(folder, "updates.sqlite");
        using var sequence = new DeviceSequence(Path.Combine(folder, "sequence.sqlite"), device.Id);
        using var cipher = new EnvelopeCipher(project, 0, RandomNumberGenerator.GetBytes(32));
        var wire = Substitute.For<IUpdateTransport>();
        wire.ConnectionIdentity.Returns("test:shared-project");
        var membership = new ProjectMembership(project, 0, device.SigningKey.PublicKey);
        using (var journal = new SessionJournal(path, sessionId, project, sequence))
        {
            await using var initial = new CollaborationSession(new(project, device.Id, 0), journal, cipher,
                new CertifiedSessionIdentity(membership, membership.CreateOwnerCertificate(device), device), wire);
        }
        using var restored = new SessionJournal(path, sessionId, project, sequence);
        var substituted = new ProjectMembership(project, 0, otherOwner.PublicKey);
        var grant = substituted.CreateInvitation(new(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1)),
            delegation.PublicKey, otherOwner);
        var certificate = substituted.ClaimInvitation(grant, delegation, device);
        Assert.Throws<CryptographicException>(() => new CollaborationSession(new(project, device.Id, 0), restored, cipher,
            new CertifiedSessionIdentity(substituted, certificate, device), wire));
    }

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }
}
