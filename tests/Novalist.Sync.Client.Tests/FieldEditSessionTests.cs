using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using NSubstitute;
using NSec.Cryptography;
using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class FieldEditSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedFieldSavesRequireTheirOriginalLeaseEvenAfterOrdinaryReconnect(bool connected)
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(Path.GetTempPath(), "novalist-field-edits", Guid.NewGuid().ToString("N"));
        try
        {
            var project = Guid.NewGuid();
            var device = Guid.NewGuid();
            var item = Guid.NewGuid();
            using var signing = Key.Create(SignatureAlgorithm.Ed25519);
            using var cipher = new EnvelopeCipher(project, 0, RandomNumberGenerator.GetBytes(32));
            using var sequences = new DeviceSequence(Path.Combine(folder, "device.sqlite"), device);
            using var journal = new SessionJournal(Path.Combine(folder, "journal.sqlite"), Guid.NewGuid(), project, sequences);
            var transport = Substitute.For<IUpdateTransport, IFieldLeaseTransport>();
            var fields = (IFieldLeaseTransport)transport;
            transport.ConnectionIdentity.Returns("test:field-edits");
            transport.IsConnected.Returns(connected);
            transport.CatchUpAsync(project, Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(_ => EmptyUpdates());
            var request = FieldLeaseRequest.Create(project, Guid.NewGuid(), 0);
            fields.PushFieldEditAsync(Arg.Any<FieldLeaseRequest>(), Arg.Any<UpdateEnvelope>(), Arg.Any<CancellationToken>())
                .Returns<long>(_ => throw new IOException("Disconnected during field save."));
            await using var session = new CollaborationSession(new(project, device, 0), journal, cipher, signing,
                new Dictionary<Guid, PublicKey> { [device] = signing.PublicKey }, transport);
            if (connected) await Assert.ThrowsAsync<IOException>(() => session.CommitFieldAsync(item, "Age: 42"u8.ToArray(), request, ct));
            else await Assert.ThrowsAsync<InvalidOperationException>(() => session.CommitFieldAsync(item, "Age: 42"u8.ToArray(), request, ct));
            if (connected) await Assert.ThrowsAsync<IOException>(() => session.CommitFieldAsync(item, "Age: 42"u8.ToArray(), request, ct));
            else await Assert.ThrowsAsync<InvalidOperationException>(() => session.CommitFieldAsync(item, "Age: 42"u8.ToArray(), request, ct));
            Assert.Single(journal.PendingFieldEdits());
            Assert.Empty(session.ReadItem(item));
            transport.IsConnected.Returns(true);
            await session.SynchronizeAsync(createProject: false, ct);
            await transport.DidNotReceive().PushAsync(Arg.Any<UpdateEnvelope>(), Arg.Any<CancellationToken>());
            Assert.Empty(session.ReadCommittedItem(item));
            var exact = Assert.Single(journal.PendingFieldEdits()).Envelope;
            fields.PushFieldEditAsync(request, Arg.Any<UpdateEnvelope>(), Arg.Any<CancellationToken>()).Returns(1L);
            await session.FlushFieldAsync(request, ct);
            await fields.Received().PushFieldEditAsync(request, Arg.Is<UpdateEnvelope>(value => value.Ciphertext.SequenceEqual(exact.Ciphertext)), Arg.Any<CancellationToken>());
            Assert.Empty(journal.PendingFieldEdits());
            var confirmed = Assert.Single(session.ReadCommittedItem(item));
            Assert.Equal(1, confirmed.ServerSeq);
            Assert.Equal("Age: 42"u8.ToArray(), confirmed.Payload);
            await session.CommitFieldAsync(item, "Age: 42"u8.ToArray(), request, ct);
            Assert.Single(session.ReadCommittedItem(item));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    [Theory]
    [InlineData("pause", false)]
    [InlineData("close", false)]
    [InlineData("dispose", false)]
    [InlineData("pause", true)]
    [InlineData("close", true)]
    [InlineData("dispose", true)]
    public async Task StoppingCancelsAStalledFieldSaveAndKeepsItsDraft(string stop, bool stallCatchUp)
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(Path.GetTempPath(), "novalist-field-stop", Guid.NewGuid().ToString("N"));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            var project = Guid.NewGuid();
            var device = Guid.NewGuid();
            var item = Guid.NewGuid();
            using var signing = Key.Create(SignatureAlgorithm.Ed25519);
            using var cipher = new EnvelopeCipher(project, 0, RandomNumberGenerator.GetBytes(32));
            using var sequence = new DeviceSequence(Path.Combine(folder, "device.sqlite"), device);
            using var journal = new SessionJournal(Path.Combine(folder, "journal.sqlite"), Guid.NewGuid(), project, sequence);
            var wire = Substitute.For<IUpdateTransport, IFieldLeaseTransport>();
            wire.ConnectionIdentity.Returns("test:field-stop");
            wire.IsConnected.Returns(true);
            wire.CatchUpAsync(project, Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(_ => EmptyUpdates());
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ((IFieldLeaseTransport)wire).PushFieldEditAsync(Arg.Any<FieldLeaseRequest>(), Arg.Any<UpdateEnvelope>(), Arg.Any<CancellationToken>())
                .Returns(async call =>
                {
                    if (!stallCatchUp) { started.TrySetResult(); await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>()); }
                    return 1L;
                });
            if (stallCatchUp) wire.CatchUpAsync(project, Arg.Any<long>(), Arg.Any<CancellationToken>())
                .Returns(call => StalledUpdates(started, call.Arg<CancellationToken>()));
            await using var session = new CollaborationSession(new(project, device, 0), journal, cipher, signing,
                new Dictionary<Guid, PublicKey> { [device] = signing.PublicKey }, wire);
            var lease = new FieldLeaseRequest(project, Guid.NewGuid(), 0, Guid.NewGuid());
            var writing = session.CommitFieldAsync(item, "A retained field draft"u8.ToArray(), lease, cancellation.Token);
            try
            {
                await started.Task.WaitAsync(TimeSpan.FromSeconds(3), ct);
                var stopping = stop switch { "pause" => session.PauseAsync(ct), "close" => session.CloseAsync(ct), _ => session.DisposeAsync().AsTask() };
                await stopping.WaitAsync(TimeSpan.FromSeconds(3), ct);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writing);
                if (stallCatchUp) Assert.Equal("A retained field draft"u8.ToArray(), Assert.Single(session.ReadCommittedItem(item)).Payload);
                else Assert.Single(journal.PendingFieldEdits());
                if (stop == "pause")
                {
                    wire.CatchUpAsync(project, Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(_ => EmptyUpdates());
                    await session.SynchronizeAsync(false, ct);
                    ((IFieldLeaseTransport)wire).PushFieldEditAsync(lease, Arg.Any<UpdateEnvelope>(), Arg.Any<CancellationToken>()).Returns(1L);
                    await session.FlushFieldAsync(lease, ct);
                    Assert.Empty(journal.PendingFieldEdits());
                }
            }
            finally { cancellation.Cancel(); try { await writing; } catch (OperationCanceledException) { Assert.True(cancellation.IsCancellationRequested); } }
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    private static async IAsyncEnumerable<SequencedUpdate> EmptyUpdates()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<SequencedUpdate> StalledUpdates(TaskCompletionSource started, [EnumeratorCancellation] CancellationToken ct)
    {
        started.TrySetResult();
        await Task.Delay(Timeout.Infinite, ct);
        yield break;
    }
}
