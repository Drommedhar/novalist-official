using System.Security.Cryptography;
using System.Net;
using NSubstitute;
using NSec.Cryptography;
using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class SessionSynchronizerTests
{
    [Theory]
    [InlineData("complete")]
    [InlineData("pause")]
    [InlineData("close")]
    [InlineData("dispose")]
    public async Task SavesDuringAnActivePassAreSentTogetherOrRetainedWhenTheSessionStops(string operation)
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(Path.GetTempPath(), "novalist-live-queue", Guid.NewGuid().ToString("N"));
        try
        {
            var project = Guid.NewGuid();
            var device = Guid.NewGuid();
            using var signing = Key.Create(SignatureAlgorithm.Ed25519);
            using var cipher = new EnvelopeCipher(project, 0, RandomNumberGenerator.GetBytes(32));
            using var sequence = new DeviceSequence(Path.Combine(folder, "sequence.sqlite"), device);
            using var journal = new SessionJournal(Path.Combine(folder, "journal.sqlite"), Guid.NewGuid(), project, sequence);
            var wire = Substitute.For<IUpdateTransport>();
            wire.ConnectionIdentity.Returns("test:live-queue");
            wire.IsConnected.Returns(true);
            wire.CatchUpAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(_ => EmptyUpdates());
            var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
            var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var count = 0;
            wire.PushAsync(Arg.Any<UpdateEnvelope>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var number = Interlocked.Increment(ref count);
                if (number == 1) { sending.TrySetResult(); return release.Task.WaitAsync(call.ArgAt<CancellationToken>(1)); }
                if (number == 3) delivered.TrySetResult();
                return Task.FromResult((long)number);
            });
            await using var session = new CollaborationSession(new(project, device, 0), journal, cipher, signing,
                new Dictionary<Guid, PublicKey> { [device] = signing.PublicKey }, wire);
            await using var live = new SessionSynchronizer(session);
            await live.StartAsync(createProject: false, ct);
            var item = Guid.NewGuid();
            session.Save(item, [1]);
            await sending.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            session.Save(item, [2]);
            session.Save(item, [3]);
            if (operation == "complete")
            {
                release.TrySetResult(1);
                await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
                await live.DisposeAsync();
                Assert.Equal(3, count);
                Assert.Empty(journal.Pending());
            }
            else
            {
                var stop = operation switch
                {
                    "pause" => session.PauseAsync(ct),
                    "close" => session.CloseAsync(ct),
                    _ => session.DisposeAsync().AsTask()
                };
                await stop.WaitAsync(TimeSpan.FromSeconds(5), ct);
                await live.DisposeAsync();
                Assert.Equal(1, count);
                Assert.False(delivered.Task.IsCompleted);
                Assert.Equal(SessionConnectionState.Stopped, live.Status.State);
            }
            Assert.Equal(3, session.ReadItem(item).Count);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(408, true)]
    [InlineData(429, true)]
    [InlineData(503, true)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    public async Task ReconnectRetriesNetworkTimeoutsButStopsForRejectedCredentials(int status, bool retries)
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(Path.GetTempPath(), "novalist-live-retry", Guid.NewGuid().ToString("N"));
        try
        {
            var project = Guid.NewGuid();
            var device = Guid.NewGuid();
            using var signing = Key.Create(SignatureAlgorithm.Ed25519);
            using var cipher = new EnvelopeCipher(project, 0, RandomNumberGenerator.GetBytes(32));
            using var sequence = new DeviceSequence(Path.Combine(folder, "sequence.sqlite"), device);
            using var journal = new SessionJournal(Path.Combine(folder, "journal.sqlite"), Guid.NewGuid(), project, sequence);
            var wire = Substitute.For<IUpdateTransport>();
            var connected = true;
            var attempts = 0;
            wire.ConnectionIdentity.Returns("test:live-retry");
            wire.IsConnected.Returns(_ => connected);
            wire.ConnectAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            {
                if (++attempts == 1)
                {
                    if (status == 0) throw new TaskCanceledException("HTTP timeout.", new TimeoutException());
                    throw new HttpRequestException("Registration failed.", null, (HttpStatusCode)status);
                }
                connected = true;
                return Task.CompletedTask;
            });
            wire.CatchUpAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(_ => EmptyUpdates());
            var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            wire.PushAsync(Arg.Any<UpdateEnvelope>(), Arg.Any<CancellationToken>()).Returns(_ => { delivered.TrySetResult(); return Task.FromResult(1L); });
            await using var session = new CollaborationSession(new(project, device, 0), journal, cipher, signing,
                new Dictionary<Guid, PublicKey> { [device] = signing.PublicKey }, wire);
            await using var live = new SessionSynchronizer(session);
            await live.StartAsync(createProject: false, ct);
            connected = false;
            session.Save(Guid.NewGuid(), "offline edit"u8.ToArray());
            if (retries) await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            else
            {
                var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
                while (live.Status.State != SessionConnectionState.Failed && DateTimeOffset.UtcNow < deadline)
                    await Task.Delay(10, ct);
                Assert.Equal(SessionConnectionState.Failed, live.Status.State);
                Assert.Equal(1, attempts);
                Assert.Single(journal.Pending());
                Assert.False(delivered.Task.IsCompleted);
            }
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    private static async IAsyncEnumerable<SequencedUpdate> EmptyUpdates()
    {
        await Task.CompletedTask;
        yield break;
    }

    [Fact]
    public async Task AnInvalidLiveUpdateDuringInitialCatchUpCannotBeReportedAsConnected()
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(Path.GetTempPath(), "novalist-live-session", Guid.NewGuid().ToString("N"));
        try
        {
            var project = Guid.NewGuid();
            var device = Guid.NewGuid();
            using var signing = Key.Create(SignatureAlgorithm.Ed25519);
            using var cipher = new EnvelopeCipher(project, 0, RandomNumberGenerator.GetBytes(32));
            using var sequence = new DeviceSequence(Path.Combine(folder, "sequence.sqlite"), device);
            using var journal = new SessionJournal(Path.Combine(folder, "journal.sqlite"), Guid.NewGuid(), project, sequence);
            var wire = Substitute.For<IUpdateTransport>();
            wire.ConnectionIdentity.Returns("test:live-session");
            wire.IsConnected.Returns(true);
            var invalid = cipher.Seal(new(project, Guid.NewGuid(), 0, device, 1), [1], signing);
            invalid.Signature[0] ^= 1;
            wire.CatchUpAsync(project, 0, Arg.Any<CancellationToken>()).Returns(_ => ConcurrentInvalidUpdate());
            await using var session = new CollaborationSession(new(project, device, 0), journal, cipher, signing,
                new Dictionary<Guid, PublicKey> { [device] = signing.PublicKey }, wire);
            await using var live = new SessionSynchronizer(session);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => live.StartAsync(createProject: false, ct));
            Assert.Equal(SessionConnectionState.Failed, live.Status.State);
            Assert.IsType<CryptographicException>(live.Status.Error);
            Assert.Equal(0, journal.LastServerSequence);

            async IAsyncEnumerable<SequencedUpdate> ConcurrentInvalidUpdate()
            {
                wire.Received += Raise.Event<Func<SequencedUpdate, Task>>(new SequencedUpdate(1, invalid));
                await Task.CompletedTask;
                yield break;
            }
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }
}
