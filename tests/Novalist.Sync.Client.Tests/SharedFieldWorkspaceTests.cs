using System.Security.Cryptography;
using NSubstitute;
using NSec.Cryptography;
using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class SharedFieldWorkspaceTests
{
    [Fact]
    public async Task ClosingProjectsConfirmedHistoryWithoutWaitingForTheWorkspaceDispatchQueue()
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(Path.GetTempPath(), "novalist-field-close", Guid.NewGuid().ToString("N"));
        var dispatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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
            wire.ConnectionIdentity.Returns("test:field-close");
            wire.IsConnected.Returns(true);
            wire.CatchUpAsync(project, Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(EmptyUpdates());
            wire.PushAsync(Arg.Any<UpdateEnvelope>(), Arg.Any<CancellationToken>()).Returns(1L);
            await using var session = new CollaborationSession(new(project, device, 0), journal, cipher, signing,
                new Dictionary<Guid, PublicKey> { [device] = signing.PublicKey }, wire);
            var localValue = "42";
            var diskFull = true;
            await using var workspace = new SharedFieldWorkspace(session, (_, value, _) =>
            {
                if (diskFull) throw new IOException("Disk full.");
                localValue = value;
                return Task.CompletedTask;
            });
            workspace.DispatchRemote = async action => { await dispatch.Task; await action(); };
            await workspace.StartAsync(new Dictionary<Guid, string> { [item] = localValue }, ct);
            session.Save(item, "{\"Version\":1,\"Value\":\"43\"}"u8.ToArray());
            await session.SynchronizeAsync(false, ct);
            await Assert.ThrowsAsync<IOException>(() => workspace.CloseAsync(ct));
            Assert.Equal("42", localValue);
            diskFull = false;
            await workspace.CloseAsync(ct).WaitAsync(TimeSpan.FromSeconds(3), ct);
            Assert.Equal("43", localValue);
            dispatch.SetResult();
        }
        finally
        {
            dispatch.TrySetResult();
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"Version\":7,\"Value\":\"invalid\"}")]
    public async Task MalformedConfirmedFieldsCannotBlockOtherFieldsOrReplaceTheLastValidValue(string malformed)
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(Path.GetTempPath(), "novalist-field-rejection", Guid.NewGuid().ToString("N"));
        try
        {
            var project = Guid.NewGuid();
            var device = Guid.NewGuid();
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            using var signing = Key.Create(SignatureAlgorithm.Ed25519);
            using var cipher = new EnvelopeCipher(project, 0, RandomNumberGenerator.GetBytes(32));
            using var sequence = new DeviceSequence(Path.Combine(folder, "device.sqlite"), device);
            using var journal = new SessionJournal(Path.Combine(folder, "journal.sqlite"), Guid.NewGuid(), project, sequence);
            var wire = Substitute.For<IUpdateTransport, IFieldLeaseTransport>();
            wire.ConnectionIdentity.Returns("test:field-rejection");
            wire.IsConnected.Returns(true);
            wire.CatchUpAsync(project, Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(EmptyUpdates());
            await using var session = new CollaborationSession(new(project, device, 0), journal, cipher, signing,
                new Dictionary<Guid, PublicKey> { [device] = signing.PublicKey }, wire);
            var baseline = new Dictionary<Guid, string> { [first] = "42", [second] = "brown" };
            await using (var workspace = new SharedFieldWorkspace(session, (_, _, _) => Task.CompletedTask))
            {
                await workspace.StartAsync(baseline, ct);
                var request = new FieldLeaseRequest(project, first, 0, Guid.NewGuid());
                ((IFieldLeaseTransport)wire).PushFieldEditAsync(request, Arg.Any<UpdateEnvelope>(), Arg.Any<CancellationToken>()).Returns(1L);
                await workspace.WriteAsync(request, "43", ct);
                wire.PushAsync(Arg.Any<UpdateEnvelope>(), Arg.Any<CancellationToken>()).Returns(2L);
                session.Save(first, System.Text.Encoding.UTF8.GetBytes(malformed));
                await session.SynchronizeAsync(false, ct);
                Assert.Equal("brown", await workspace.ReadAsync(second, ct));
                Assert.Equal("43", await workspace.ReadAsync(first, ct));
                Assert.True(workspace.RejectedUpdates);
            }
            await using var restored = new SharedFieldWorkspace(session, (_, _, _) => Task.CompletedTask);
            await restored.StartAsync(baseline, ct);
            Assert.Equal("43", await restored.ReadAsync(first, ct));
            Assert.False(restored.RejectedUpdates);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task FocusedSavesProjectOnlyConfirmedValuesAndRestoreThemAfterReopening()
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(Path.GetTempPath(), "novalist-shared-fields", Guid.NewGuid().ToString("N"));
        try
        {
            var project = Guid.NewGuid();
            var device = Guid.NewGuid();
            var item = Guid.NewGuid();
            var request = new FieldLeaseRequest(project, item, 0, Guid.NewGuid());
            using var signing = Key.Create(SignatureAlgorithm.Ed25519);
            using var cipher = new EnvelopeCipher(project, 0, RandomNumberGenerator.GetBytes(32));
            using var sequence = new DeviceSequence(Path.Combine(folder, "device.sqlite"), device);
            using var journal = new SessionJournal(Path.Combine(folder, "journal.sqlite"), Guid.NewGuid(), project, sequence);
            var wire = Substitute.For<IUpdateTransport, IFieldLeaseTransport>();
            wire.ConnectionIdentity.Returns("test:field-projection");
            wire.IsConnected.Returns(true);
            wire.CatchUpAsync(project, Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(EmptyUpdates());
            var fields = (IFieldLeaseTransport)wire;
            var now = DateTimeOffset.UtcNow;
            fields.AcquireFieldLeaseAsync(request, Arg.Any<CancellationToken>())
                .Returns(new FieldLeaseGrant(device, request.LeaseId, now.AddSeconds(30), now, 0));
            await using var session = new CollaborationSession(new(project, device, 0), journal, cipher, signing,
                new Dictionary<Guid, PublicKey> { [device] = signing.PublicKey }, wire);
            var localValue = "42";
            Task Project(Guid id, string value, CancellationToken token)
            {
                Assert.Equal(item, id);
                localValue = value;
                return Task.CompletedTask;
            }
            await using (var workspace = new SharedFieldWorkspace(session, Project))
            {
                await workspace.StartAsync(new Dictionary<Guid, string> { [item] = localValue }, ct);
                Assert.Equal("42", (await workspace.AcquireAsync(request, ct)).Value);
                fields.PushFieldEditAsync(request, Arg.Any<UpdateEnvelope>(), Arg.Any<CancellationToken>())
                    .Returns<long>(_ => throw new IOException("Lost connection."));
                await Assert.ThrowsAsync<IOException>(() => workspace.WriteAsync(request, "43", ct));
                Assert.Equal("42", localValue);
                Assert.Single(journal.PendingFieldEdits());
                fields.PushFieldEditAsync(request, Arg.Any<UpdateEnvelope>(), Arg.Any<CancellationToken>()).Returns(1L);
                await session.FlushFieldAsync(request, ct);
                await workspace.RefreshAsync(ct);
                Assert.Equal("43", localValue);
            }
            localValue = "42";
            await using var reopened = new SharedFieldWorkspace(session, Project);
            await reopened.StartAsync(new Dictionary<Guid, string>(), ct);
            await reopened.AddAsync(new Dictionary<Guid, string> { [item] = localValue }, ct);
            Assert.Equal("43", localValue);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    private static async IAsyncEnumerable<SequencedUpdate> EmptyUpdates()
    {
        await Task.CompletedTask;
        yield break;
    }
}
