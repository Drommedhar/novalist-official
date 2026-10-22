using Novalist.Sync.Client;
using Xunit;

namespace Novalist.Sync.IntegrationTests;

public sealed class HostedProjectSessionTests(CommunityServerFixture server) : IClassFixture<CommunityServerFixture>
{
    [Fact]
    public async Task CancellingTheInvitationConnectionStopsWaitingForCredentials()
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(Path.GetTempPath(), "novalist-hosted-session", Guid.NewGuid().ToString("N"));
        var credentials = new MemoryCredentials();
        var subject = Guid.NewGuid().ToString();
        using var device = await new DeviceIdentityStore(credentials).GetOrCreateAsync(new("https://identity.example"), subject, ct);
        using var access = await new HostedProjectAccessStore(credentials).CreateOwnerAsync(server.Address, device, ct);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await using var session = await HostedProjectSession.OpenAsync(folder, Guid.NewGuid(), access, device,
                () => { requested.TrySetResult(); return token.Task; });
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var connecting = session.ConnectAsync(cancellation.Token);
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            try
            {
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting.WaitAsync(TimeSpan.FromSeconds(2), ct));
            }
            finally { token.TrySetResult(server.Token(subject)); }
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShutdownCancelsAStartupWaitingForCredentials(bool end)
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(Path.GetTempPath(), "novalist-hosted-session", Guid.NewGuid().ToString("N"));
        var credentials = new MemoryCredentials();
        var subject = Guid.NewGuid().ToString();
        using var device = await new DeviceIdentityStore(credentials).GetOrCreateAsync(new("https://identity.example"), subject, ct);
        using var access = await new HostedProjectAccessStore(credentials).CreateOwnerAsync(server.Address, device, ct);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await using var session = await HostedProjectSession.OpenAsync(folder, Guid.NewGuid(), access, device,
                () => { requested.TrySetResult(); return token.Task; });
            var starting = session.StartAsync(createProject: true, ct);
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            try
            {
                var stopping = end ? session.EndAsync(ct) : session.DisposeAsync().AsTask();
                await stopping.WaitAsync(TimeSpan.FromSeconds(2), ct);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);
            }
            finally { token.TrySetResult(server.Token(subject)); }
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task ReopeningAnInterruptedSessionRetainsPendingWritingButAnEndedSessionCannotReconnect()
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(Path.GetTempPath(), "novalist-hosted-session", Guid.NewGuid().ToString("N"));
        var credentials = new MemoryCredentials();
        var subject = Guid.NewGuid().ToString();
        using var device = await new DeviceIdentityStore(credentials).GetOrCreateAsync(new("https://identity.example"), subject, ct);
        using var access = await new HostedProjectAccessStore(credentials).CreateOwnerAsync(server.Address, device, ct);
        var sessionId = Guid.NewGuid();
        var item = Guid.NewGuid();
        try
        {
            await using (var first = await HostedProjectSession.OpenAsync(folder, sessionId, access, device, () => Task.FromResult(server.Token(subject))))
            {
                await first.StartAsync(createProject: true, ct);
                await first.Updates.PauseAsync(ct);
                first.Updates.Save(item, "Retained during interruption."u8.ToArray());
            }
            await using (var resumed = await HostedProjectSession.OpenAsync(folder, sessionId, access, device, () => Task.FromResult(server.Token(subject))))
            {
                Assert.Equal("Retained during interruption."u8.ToArray(), Assert.Single(resumed.Updates.ReadItem(item)));
                await resumed.StartAsync(createProject: false, ct);
                await using var observer = new HostedTransport(server.Address, device, () => Task.FromResult(server.Token(subject)));
                await observer.ConnectAsync(ct);
                await observer.JoinAsync(access.ProjectId, ct);
                var received = new List<SequencedUpdate>();
                await foreach (var update in observer.CatchUpAsync(access.ProjectId, 0, ct)) received.Add(update);
                Assert.Single(received);
                await resumed.EndAsync(ct);
            }
            await using var ended = await HostedProjectSession.OpenAsync(folder, sessionId, access, device, () => Task.FromResult(server.Token(subject)));
            Assert.Equal("Retained during interruption."u8.ToArray(), Assert.Single(ended.Updates.ReadItem(item)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => ended.StartAsync(createProject: false, ct));
            await using var next = await HostedProjectSession.OpenAsync(folder, Guid.NewGuid(), access, device, () => Task.FromResult(server.Token(subject)));
            Assert.Empty(next.Updates.ReadItem(item));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    private sealed class MemoryCredentials : ICollaborationCredentials
    {
        private readonly Dictionary<string, string> values = [];
        public Task<string?> ReadAsync(string key, CancellationToken ct) => Task.FromResult(values.GetValueOrDefault(key));
        public Task WriteAsync(string key, string value, CancellationToken ct) { values[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken ct) { values.Remove(key); return Task.CompletedTask; }
    }
}
