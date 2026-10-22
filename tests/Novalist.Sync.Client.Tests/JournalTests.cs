using System.Security.Cryptography;
using NSec.Cryptography;
using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class JournalTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "novalist-sync-tests", Guid.NewGuid().ToString("N"));
    private readonly Guid session = Guid.NewGuid();
    private readonly Guid project = Guid.NewGuid();
    private readonly Guid device = Guid.NewGuid();
    private readonly byte[] epochKey = RandomNumberGenerator.GetBytes(32);
    private readonly DeviceSequence sequences;
    private string Database => Path.Combine(folder, "updates.sqlite");

    public JournalTests() => sequences = new DeviceSequence(Path.Combine(folder, "device.sqlite"), device);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnconfirmedFieldEditsSurviveRestartWithoutEnteringTheOrdinaryUploadQueue(bool guarded)
    {
        using var signing = Key.Create(SignatureAlgorithm.Ed25519);
        using var cipher = new EnvelopeCipher(project, 0, epochKey);
        var item = Guid.NewGuid();
        var lease = new FieldLeaseRequest(project, Guid.NewGuid(), 0, Guid.NewGuid())
            { RequiredLease = guarded ? new(Guid.NewGuid(), Guid.NewGuid()) : null };
        UpdateEnvelope envelope;
        using (var journal = new SessionJournal(Database, session, project, sequences))
        {
            envelope = Assert.Single(journal.AppendField(item, ["Unconfirmed age"u8.ToArray()], lease,
                (sequence, payload) => cipher.Seal(new(project, item, 0, device, sequence), payload, signing)));
            Assert.Empty(journal.Pending());
            Assert.Empty(journal.ReadItem(item));
            Assert.Empty(journal.ReadCommittedItemRecords(item));
        }
        using var restored = new SessionJournal(Database, session, project, sequences);
        var retained = Assert.Single(restored.PendingFieldEdits());
        Assert.Equal(lease, retained.Lease);
        Assert.Equal(envelope.Ciphertext, retained.Envelope.Ciphertext);
        Assert.Equal("Unconfirmed age"u8.ToArray(), retained.Payload);
        Assert.Empty(restored.Pending());
        restored.Acknowledge(envelope, 1);
        Assert.Empty(restored.PendingFieldEdits());
        Assert.Equal("Unconfirmed age"u8.ToArray(), Assert.Single(restored.ReadItem(item)));
        Assert.Equal(1, Assert.Single(restored.ReadCommittedItemRecords(item)).ServerSeq);
    }

    [Fact]
    public void FieldHistoryWaitsForGapsAndFailedFieldsRemainRecoverableAfterSessionEnd()
    {
        using var signing = Key.Create(SignatureAlgorithm.Ed25519);
        using var cipher = new EnvelopeCipher(project, 0, epochKey);
        var item = Guid.NewGuid();
        using var journal = new SessionJournal(Database, session, project, sequences);
        var lease = new FieldLeaseRequest(project, Guid.NewGuid(), 0, Guid.NewGuid());
        var pending = Assert.Single(journal.AppendField(item, [[1]], lease,
            (sequence, payload) => cipher.Seal(new(project, item, 0, device, sequence), payload, signing)));
        var peer = Guid.NewGuid();
        var latest = cipher.Seal(new(project, item, 0, peer, 2), [3], signing);
        journal.Receive(new(2, latest), [3]);
        Assert.Empty(journal.ReadCommittedItemRecords(item));
        var earlier = cipher.Seal(new(project, item, 0, peer, 1), [2], signing);
        journal.Receive(new(1, earlier), [2]);
        Assert.Equal(new long[] { 1, 2 }, journal.ReadCommittedItemRecords(item).Select(record => record.ServerSeq));
        journal.Close();
        Assert.Equal(pending.Ciphertext, Assert.Single(journal.PendingFieldEdits()).Envelope.Ciphertext);
    }

    [Fact]
    public void RestartRetainsLocalDocumentsAndExactEnvelopesForIdempotentRetry()
    {
        using var signing = Key.Create(SignatureAlgorithm.Ed25519);
        using var cipher = new EnvelopeCipher(project, 0, epochKey);
        var item = Guid.NewGuid();
        UpdateEnvelope sent;
        using (var journal = new SessionJournal(Database, session, project, sequences))
        {
            sent = journal.Append(item, "first change"u8.ToArray(), sequence =>
                cipher.Seal(new(project, item, 0, device, sequence), "first change"u8, signing));
        }
        using var restored = new SessionJournal(Database, session, project, sequences);
        Assert.Equal("first change"u8.ToArray(), Assert.Single(restored.ReadItem(item)));
        Assert.Equal(sent.Ciphertext, Assert.Single(restored.Pending()).Ciphertext);
        Assert.Equal(sent.Nonce, Assert.Single(restored.Pending()).Nonce);
        restored.Acknowledge(sent, 1);
        Assert.Empty(restored.Pending());
        Assert.Single(restored.ReadItem(item));
        Assert.Equal(1, restored.LastServerSequence);
    }

    [Fact]
    public void CatchUpDoesNotSkipAGapOrStoreDuplicateUpdates()
    {
        using var signing = Key.Create(SignatureAlgorithm.Ed25519);
        using var cipher = new EnvelopeCipher(project, 0, epochKey);
        using var journal = new SessionJournal(Database, session, project, sequences);
        var item = Guid.NewGuid();
        var peer = Guid.NewGuid();
        var first = cipher.Seal(new(project, item, 0, peer, 1), "first"u8, signing);
        var second = cipher.Seal(new(project, item, 0, peer, 2), "second"u8, signing);
        Assert.True(journal.Receive(new(2, second), cipher.Open(second, signing.PublicKey)));
        Assert.Equal(0, journal.LastServerSequence);
        Assert.True(journal.Receive(new(1, first), cipher.Open(first, signing.PublicKey)));
        Assert.False(journal.Receive(new(2, second), cipher.Open(second, signing.PublicKey)));
        Assert.Equal(2, journal.LastServerSequence);
        Assert.Equal(2, journal.ReadItem(item).Count);
    }

    [Fact]
    public void FailedEncryptionDoesNotPublishAnUpdateOrReuseAnAllocatedSequence()
    {
        using var signing = Key.Create(SignatureAlgorithm.Ed25519);
        using var cipher = new EnvelopeCipher(project, 0, epochKey);
        using var journal = new SessionJournal(Database, session, project, sequences);
        var item = Guid.NewGuid();
        Assert.Throws<CryptographicException>(() => journal.Append(item, [1], _ => throw new CryptographicException()));
        var envelope = journal.Append(item, [2], sequence => cipher.Seal(new(project, item, 0, device, sequence), [2], signing));
        Assert.Equal(2ul, UpdateHeader.Parse(envelope.Header).SenderSeq);
        Assert.Single(journal.ReadItem(item));
    }

    [Fact]
    public void AClosedSessionCanBeRecoveredAfterRestartWithoutAllowingMoreWrites()
    {
        using var signing = Key.Create(SignatureAlgorithm.Ed25519);
        using var cipher = new EnvelopeCipher(project, 0, epochKey);
        var item = Guid.NewGuid();
        using (var journal = new SessionJournal(Database, session, project, sequences))
        {
            journal.Append(item, [7], sequence => cipher.Seal(new(project, item, 0, device, sequence), [7], signing));
            journal.Close();
        }
        using var archive = new SessionJournal(Database, session, project, sequences);
        Assert.Equal(new byte[] { 7 }, Assert.Single(archive.ReadItem(item)));
        Assert.Throws<InvalidOperationException>(() => archive.Append(item, [8], sequence => cipher.Seal(new(project, item, 0, device, sequence), [8], signing)));
        Assert.Throws<InvalidOperationException>(() => archive.Pending());
        Assert.Throws<InvalidOperationException>(() => new SessionJournal(Database, Guid.NewGuid(), project, sequences));
        Assert.Throws<InvalidOperationException>(() => new SessionJournal(Database, session, Guid.NewGuid(), sequences));
    }

    [Fact]
    public void NewSessionsOnTheSameDeviceNeverReuseSenderSequences()
    {
        using var signing = Key.Create(SignatureAlgorithm.Ed25519);
        using var cipher = new EnvelopeCipher(project, 0, epochKey);
        var item = Guid.NewGuid();
        UpdateEnvelope first;
        using (var journal = new SessionJournal(Database, session, project, sequences))
        {
            first = journal.Append(item, [1], sequence => cipher.Seal(new(project, item, 0, device, sequence), [1], signing));
            journal.Close();
        }
        using var next = new SessionJournal(Path.Combine(folder, "next.sqlite"), Guid.NewGuid(), project, sequences);
        next.Receive(new(1, first), [1]);
        var second = next.Append(item, [2], sequence => cipher.Seal(new(project, item, 0, device, sequence), [2], signing));
        Assert.True(UpdateHeader.Parse(second.Header).SenderSeq > UpdateHeader.Parse(first.Header).SenderSeq);
        Assert.Single(next.Pending());
    }

    public void Dispose()
    {
        sequences.Dispose();
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void RestartCannotSendTheOldQueueToAnotherServer()
    {
        using (var journal = new SessionJournal(Database, session, project, sequences)) journal.BindTransport("hosted:https://first.example/");
        using var resumed = new SessionJournal(Database, session, project, sequences);
        resumed.BindTransport("hosted:https://first.example/");
        Assert.Throws<InvalidOperationException>(() => resumed.BindTransport("hosted:https://second.example/"));
        Assert.Throws<InvalidOperationException>(() => resumed.BindTransport("steam:another-session"));
    }
}
