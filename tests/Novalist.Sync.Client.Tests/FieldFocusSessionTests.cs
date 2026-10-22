using System.Security.Cryptography;
using NSubstitute;
using NSec.Cryptography;
using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class FieldFocusSessionTests
{
    [Fact]
    public async Task FocusWaitsForThePreviousWritersConfirmedValue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = new Fixture();
        var request = fixture.Request();
        fixture.Session.Save(request.ResourceId, "previous writer's value"u8.ToArray());
        var saved = Assert.Single(fixture.Journal.Pending());
        fixture.Grant(request, sequence: 1);
        var caughtUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Wire.CatchUpAsync(fixture.Project, 0, Arg.Any<CancellationToken>())
            .Returns(call => UpdatesAfter(caughtUp.Task, new(1, saved), call.Arg<CancellationToken>()));
        var focusing = fixture.Session.AcquireFieldAsync(request, ct);
        Assert.False(focusing.IsCompleted);
        caughtUp.SetResult();
        var focus = await focusing;
        Assert.True(focus.Acquired);
        Assert.InRange(focus.RemainingMilliseconds, 1, 30_000);
        Assert.Equal("previous writer's value"u8.ToArray(), Assert.Single(fixture.Session.ReadCommittedItem(request.ResourceId)).Payload);
    }

    private static async IAsyncEnumerable<SequencedUpdate> UpdatesAfter(Task ready, SequencedUpdate update,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await ready.WaitAsync(ct);
        yield return update;
    }

    [Fact]
    public async Task AFieldRemainsUnavailableWhenHeldElsewhereOrItsHistoryHasAGap()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = new Fixture();
        var request = fixture.Request();
        var other = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        fixture.Fields.AcquireFieldLeaseAsync(request, Arg.Any<CancellationToken>())
            .Returns(new FieldLeaseGrant(other, Guid.NewGuid(), now.AddSeconds(30), now, 4));
        var busy = await fixture.Session.AcquireFieldAsync(request, ct);
        Assert.False(busy.Acquired);
        Assert.Equal(other, busy.DeviceId);
        fixture.Grant(request, sequence: 4);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Session.AcquireFieldAsync(request, ct));
    }

    [Fact]
    public async Task RenewAndBlurUseTheSameFocusIdentityAndDoNotReviveAnExpiredLease()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = new Fixture();
        var request = fixture.Request();
        fixture.Grant(request, sequence: 0);
        Assert.True((await fixture.Session.AcquireFieldAsync(request, ct)).Acquired);
        var now = DateTimeOffset.UtcNow;
        fixture.Fields.RenewFieldLeaseAsync(request, Arg.Any<CancellationToken>())
            .Returns(new FieldLeaseGrant(fixture.Device, request.LeaseId, now.AddSeconds(30), now, 0));
        Assert.True((await fixture.Session.RenewFieldAsync(request, ct))?.Acquired);
        fixture.Fields.RenewFieldLeaseAsync(request, Arg.Any<CancellationToken>()).Returns((FieldLeaseGrant?)null);
        Assert.Null(await fixture.Session.RenewFieldAsync(request, ct));
        fixture.Fields.ReleaseFieldLeaseAsync(request, Arg.Any<CancellationToken>()).Returns(true);
        Assert.True(await fixture.Session.ReleaseFieldAsync(request, ct));
        fixture.Fields.AcquireFieldLeaseAsync(request, Arg.Any<CancellationToken>())
            .Returns(new FieldLeaseGrant(fixture.Device, request.LeaseId, now, now, 0));
        await Assert.ThrowsAsync<TimeoutException>(() => fixture.Session.AcquireFieldAsync(request, ct));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "novalist-focus", Guid.NewGuid().ToString("N"));
        private readonly Key signing = Key.Create(SignatureAlgorithm.Ed25519);
        private readonly EnvelopeCipher cipher;
        private readonly DeviceSequence sequence;
        internal Guid Project { get; } = Guid.NewGuid();
        internal Guid Device { get; } = Guid.NewGuid();
        internal IUpdateTransport Wire { get; } = Substitute.For<IUpdateTransport, IFieldLeaseTransport>();
        internal IFieldLeaseTransport Fields => (IFieldLeaseTransport)Wire;
        internal SessionJournal Journal { get; }
        internal CollaborationSession Session { get; }

        internal Fixture()
        {
            cipher = new(Project, 0, RandomNumberGenerator.GetBytes(32));
            sequence = new(Path.Combine(folder, "device.sqlite"), Device);
            Journal = new(Path.Combine(folder, "session.sqlite"), Guid.NewGuid(), Project, sequence);
            Wire.ConnectionIdentity.Returns("test:field-focus");
            Wire.IsConnected.Returns(true);
            Wire.CatchUpAsync(Project, Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(EmptyUpdates());
            Session = new(new(Project, Device, 0), Journal, cipher, signing,
                new Dictionary<Guid, PublicKey> { [Device] = signing.PublicKey }, Wire);
        }

        private static async IAsyncEnumerable<SequencedUpdate> EmptyUpdates()
        {
            await Task.CompletedTask;
            yield break;
        }

        internal FieldLeaseRequest Request() => new(Project, Guid.NewGuid(), 0, Guid.NewGuid());
        internal void Grant(FieldLeaseRequest request, long sequence)
        {
            var now = DateTimeOffset.UtcNow;
            Fields.AcquireFieldLeaseAsync(request, Arg.Any<CancellationToken>())
                .Returns(new FieldLeaseGrant(Device, request.LeaseId, now.AddSeconds(30), now, sequence));
        }

        public async ValueTask DisposeAsync()
        {
            await Session.DisposeAsync();
            Journal.Dispose();
            sequence.Dispose();
            cipher.Dispose();
            signing.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }
}
