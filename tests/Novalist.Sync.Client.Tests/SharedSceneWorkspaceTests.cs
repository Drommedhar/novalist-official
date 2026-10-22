using System.Security.Cryptography;
using System.Text.Json;
using NSec.Cryptography;
using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class SharedSceneWorkspaceTests : IAsyncDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "novalist-scenes", Guid.NewGuid().ToString("N"));
    private readonly Guid item = Guid.NewGuid();
    private readonly DeviceSequence sequences;
    private readonly SessionJournal journal;
    private readonly Key signing = Key.Create(SignatureAlgorithm.Ed25519);
    private readonly EnvelopeCipher cipher;
    private readonly CollaborationSession session;
    private readonly DeviceSequence remoteSequences;
    private readonly SessionJournal remoteJournal;
    private readonly CollaborationSession remote;
    private readonly OfflineTransport transport = new();
    private readonly DocumentPeer peer = new();
    private readonly List<string?> projections = [];

    public SharedSceneWorkspaceTests()
    {
        var project = Guid.NewGuid();
        sequences = new(Path.Combine(folder, "device.sqlite"), Guid.NewGuid());
        journal = new(Path.Combine(folder, "session.sqlite"), Guid.NewGuid(), project, sequences);
        cipher = new(project, 0, RandomNumberGenerator.GetBytes(32));
        remoteSequences = new(Path.Combine(folder, "remote-device.sqlite"), Guid.NewGuid());
        remoteJournal = new(Path.Combine(folder, "remote-session.sqlite"), Guid.NewGuid(), project, remoteSequences);
        remote = new(new(project, remoteSequences.DeviceId, 0), remoteJournal, cipher, signing,
            new Dictionary<Guid, PublicKey>(), new OfflineTransport());
        session = new(new(project, sequences.DeviceId, 0), journal, cipher, signing,
            new Dictionary<Guid, PublicKey> { [sequences.DeviceId] = signing.PublicKey, [remoteSequences.DeviceId] = signing.PublicKey }, transport);
    }

    [Fact]
    public async Task LocalEditIsJournaledBeforeProjectionAndReplayedAfterRestart()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var shared = Create((id, snapshot, _, _) =>
        {
            if (snapshot.State[0] == 2) Assert.Single(session.ReadItem(id));
            projections.Add(snapshot.Html);
            return Task.CompletedTask;
        }))
        {
            await shared.StartAsync([new(item, "Books/scene.html", [1])], ct);
            await shared.WriteAsync(item, [2], ct);
            Assert.Equal("<p>2</p>", (await shared.ReadAsync(item, ct)).Html);
            Assert.False(session.IsConnected);
        }
        await using var reopened = Create();
        await reopened.StartAsync([new(item, "Books/scene.html", [1])], ct);
        Assert.Equal("<p>2</p>", (await reopened.ReadAsync(item, ct)).Html);
        Assert.Single(session.ReadItem(item));
    }

    [Fact]
    public async Task FailedProjectionStaysDurableAndReadRetriesIt()
    {
        var fail = true;
        await using var shared = Create((_, snapshot, _, _) =>
        {
            if (snapshot.State[0] == 2 && fail) throw new IOException("Disk full.");
            projections.Add(snapshot.Html);
            return Task.CompletedTask;
        });
        var ct = TestContext.Current.CancellationToken;
        await shared.StartAsync([new(item, "Books/scene.html", [1])], ct);
        await Assert.ThrowsAsync<IOException>(() => shared.WriteAsync(item, [2], ct));
        Assert.Single(session.ReadItem(item));
        fail = false;
        Assert.Equal("<p>2</p>", (await shared.ReadAsync(item, ct)).Html);
        Assert.Equal("<p>2</p>", projections[^1]);
    }

    [Fact]
    public async Task InvalidOrUnknownLocalUpdateCannotEnterJournal()
    {
        await using var shared = Create();
        var ct = TestContext.Current.CancellationToken;
        await shared.StartAsync([new(item, "Books/scene.html", [1])], ct);
        await Assert.ThrowsAsync<InvalidDataException>(() => shared.WriteAsync(item, [255], ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => shared.WriteAsync(Guid.NewGuid(), [2], ct));
        Assert.Empty(session.ReadItem(item));
    }

    [Fact]
    public async Task InvalidReceivedHistoryIsQuarantinedWithoutBlockingValidEditsOrRestart()
    {
        session.Save(item, [255]);
        var ct = TestContext.Current.CancellationToken;
        await using (var shared = Create())
        {
            await shared.StartAsync([new(item, "Books/scene.html", [1])], ct);
            Assert.True(shared.RejectedUpdates);
            Assert.Empty(session.ReadItem(item));
            await shared.WriteAsync(item, [3], ct);
            Assert.Equal("<p>3</p>", (await shared.ReadAsync(item, ct)).Html);
        }
        await using var reopened = Create();
        await reopened.StartAsync([new(item, "Books/scene.html", [1])], ct);
        Assert.False(reopened.RejectedUpdates);
        Assert.Equal("<p>3</p>", (await reopened.ReadAsync(item, ct)).Html);
    }

    private SharedSceneWorkspace Create(Func<Guid, SharedSceneSnapshot, bool, CancellationToken, Task>? project = null) =>
        new(session, new DesktopSceneDocuments(Guid.NewGuid(), peer.RequestAsync),
            project ?? ((_, snapshot, _, _) => { projections.Add(snapshot.Html); return Task.CompletedTask; }));

    [Fact]
    public async Task ClosingProjectsQueuedEditsAndKeepsSessionRetryableIfTheLocalCopyCannotBeSaved()
    {
        var fail = true;
        var dispatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var shared = Create((_, snapshot, _, _) =>
        {
            if (snapshot.State[0] == 2 && fail) throw new IOException("Disk full.");
            projections.Add(snapshot.Html);
            return Task.CompletedTask;
        });
        shared.DispatchRemote = async action => { await dispatch.Task; await action(); };
        var ct = TestContext.Current.CancellationToken;
        await shared.StartAsync([new(item, "Books/scene.html", [1])], ct);
        await DeliverAsync([2]);
        try
        {
            await Assert.ThrowsAsync<IOException>(() => shared.CloseAsync(ct));
            Assert.False(journal.IsClosed);
            fail = false;
            await shared.CloseAsync(ct);
            Assert.True(journal.IsClosed);
            Assert.Equal("<p>2</p>", projections[^1]);
            await DeliverAsync([3]);
        }
        finally { dispatch.TrySetResult(); }
        await shared.DrainAsync(ct);
        Assert.Equal("<p>2</p>", projections[^1]);
        Assert.Single(session.ReadItem(item));
    }

    private Task DeliverAsync(byte[] update)
    {
        remote.Save(item, update);
        var outgoing = remoteJournal.Pending();
        return transport.DeliverAsync(new(outgoing.Count, outgoing[^1]));
    }

    [Fact]
    public async Task RecoveredProjectionPublishesEveryWithheldEditToOpenEditors()
    {
        peer.Cumulative = true;
        var fail = true;
        var notices = new List<byte[]>();
        await using var shared = Create((_, snapshot, _, _) =>
        {
            if (snapshot.State.Contains((byte)2) && fail) throw new IOException("Disk full.");
            return Task.CompletedTask;
        });
        shared.Notify = (_, state) => { notices.Add(state); return Task.CompletedTask; };
        var ct = TestContext.Current.CancellationToken;
        await shared.StartAsync([new(item, "Books/scene.html", [1])], ct);
        await DeliverAsync([2]);
        await Assert.ThrowsAsync<IOException>(() => shared.DrainAsync(ct));
        Assert.Empty(notices);
        fail = false;
        await DeliverAsync([3]);
        await shared.DrainAsync(ct);
        Assert.Equal(new byte[] { 1, 2, 3 }, Assert.Single(notices));
    }

    public async ValueTask DisposeAsync()
    {
        await session.DisposeAsync();
        await remote.DisposeAsync();
        remoteJournal.Dispose();
        remoteSequences.Dispose();
        journal.Dispose();
        sequences.Dispose();
        cipher.Dispose();
        signing.Dispose();
        Directory.Delete(folder, recursive: true);
    }

    // The desktop protocol is external to this .NET module; Yjs merging is
    // exercised with the actual Electron worker in collaboration-host.spec.ts.
    private sealed class DocumentPeer
    {
        private readonly Dictionary<Guid, byte[]> states = [];
        public bool Cumulative { get; set; }

        public Task<JsonElement> RequestAsync(object request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var message = JsonSerializer.SerializeToElement(request);
            var action = message.GetProperty("action").GetString();
            if (action == "open") states.Clear();
            if (action == "execute")
            {
                var command = message.GetProperty("command");
                var state = command.GetProperty("state").GetBytesFromBase64();
                if (state[0] == 255)
                {
                    if (command.GetProperty("kind").GetString() == "receive")
                        return Task.FromResult(JsonSerializer.SerializeToElement(new SceneApplyResult(false, [Convert.ToHexStringLower(SHA256.HashData(state))])));
                    throw new InvalidDataException("Invalid shared state.");
                }
                var id = command.GetProperty("id").GetGuid();
                if (command.GetProperty("kind").GetString() != "validate")
                    states[id] = Cumulative && states.TryGetValue(id, out var previous) ? previous.Union(state).ToArray() : state;
            }
            object? result = action == "execute" && message.GetProperty("command").GetProperty("kind").GetString() == "receive" ? new SceneApplyResult(true, []) : null;
            if (action == "capture")
            {
                var state = states[message.GetProperty("id").GetGuid()];
                result = new SharedSceneSnapshot(state, [], $"<p>{state[0]}</p>", null);
            }
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }
    }

    private sealed class OfflineTransport : IUpdateTransport
    {
        public event Func<SequencedUpdate, Task>? Received;
        public Task DeliverAsync(SequencedUpdate update) => Received?.Invoke(update) ?? Task.CompletedTask;
        public bool IsConnected => false;
        public string ConnectionIdentity => "test:offline";
        public Task ConnectAsync(CancellationToken ct) => throw new IOException("Offline.");
        public Task CreateProjectAsync(Guid project, CancellationToken ct) => throw new NotSupportedException();
        public Task JoinAsync(Guid project, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> PushAsync(UpdateEnvelope envelope, CancellationToken ct) => throw new NotSupportedException();
        public IAsyncEnumerable<SequencedUpdate> CatchUpAsync(Guid project, long after, CancellationToken ct) => throw new NotSupportedException();
        public Task DisconnectAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
