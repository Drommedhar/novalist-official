namespace Novalist.Sync.Client;

public sealed class SharedSceneWorkspace : IAsyncDisposable
{
    private readonly CollaborationSession session;
    private readonly DesktopSceneDocuments documents;
    private readonly Func<Guid, SharedSceneSnapshot, bool, CancellationToken, Task> project;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object queueGate = new();
    private readonly HashSet<Guid> items = [];
    private readonly HashSet<Guid> failures = [];
    private readonly Dictionary<Guid, byte[]?> unpublished = [];
    private Task incoming = Task.CompletedTask;
    private bool opened;
    private bool started;
    private bool disposed;

    public Func<Guid, byte[], Task>? Notify { get; set; }
    public Func<Func<Task>, Task> DispatchRemote { get; set; } = action => action();
    public Exception? ProjectionError { get; private set; }
    public bool RejectedUpdates { get; private set; }

    public SharedSceneWorkspace(CollaborationSession session, DesktopSceneDocuments documents,
        Func<Guid, SharedSceneSnapshot, bool, CancellationToken, Task> project)
    {
        this.session = session;
        this.documents = documents;
        this.project = project;
    }

    public async Task StartAsync(IReadOnlyList<SharedDocumentState> initial, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (opened) throw new InvalidOperationException("The shared scene workspace has already started.");
            if (initial.Any(scene => scene.ItemId == Guid.Empty) || initial.Select(scene => scene.ItemId).Distinct().Count() != initial.Count)
                throw new ArgumentException("Each shared scene needs a unique identity.", nameof(initial));
            await documents.OpenAsync(ct);
            opened = true;
            session.UpdateReceived += Enqueue;
            foreach (var scene in initial)
            {
                await documents.LoadAsync(scene.ItemId, scene.State, [], ct);
                items.Add(scene.ItemId);
                foreach (var update in session.ReadItem(scene.ItemId))
                    await ApplyAsync(scene.ItemId, update, ct);
                await ProjectAsync(scene.ItemId, ct);
            }
            started = true;
        }
        finally { gate.Release(); }
    }

    public async Task<SharedSceneSnapshot> ReadAsync(Guid item, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            EnsureItem(item);
            await ReplayFailureAsync(item, ct);
            return await ProjectAsync(item, ct);
        }
        finally { gate.Release(); }
    }

    public async Task AddAsync(IReadOnlyList<SharedDocumentState> added, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!started) throw new InvalidOperationException("The shared scenes have not opened.");
            foreach (var scene in added)
            {
                if (scene.ItemId == Guid.Empty) throw new InvalidDataException("A shared scene needs an identity.");
                if (items.Contains(scene.ItemId)) continue;
                await documents.LoadAsync(scene.ItemId, scene.State, [], ct);
                items.Add(scene.ItemId);
                failures.Add(scene.ItemId);
                await ReplayFailureAsync(scene.ItemId, ct);
                await ProjectAsync(scene.ItemId, ct);
            }
        }
        finally { gate.Release(); }
    }

    public async Task RemoveAsync(Guid item, byte[] deletedAt, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            EnsureItem(item);
            await documents.RemoveAsync(item, deletedAt, ct);
            failures.Add(item);
            await ReplayFailureAsync(item, ct);
            await ProjectAsync(item, ct);
        }
        finally { gate.Release(); }
    }

    public async Task WriteAsync(Guid item, byte[] update, CancellationToken ct)
    {
        var stable = update.ToArray();
        await gate.WaitAsync(ct);
        try
        {
            EnsureItem(item);
            await ReplayFailureAsync(item, ct);
            await documents.ValidateAsync(item, stable, ct);
            ct.ThrowIfCancellationRequested();
            session.Save(item, stable);
            failures.Add(item);
            // After the journal commits, finish projection even if the caller
            // leaves. A failed projection is replayed from that same journal.
            if (!await ApplyAsync(item, stable, CancellationToken.None)) throw new InvalidDataException("The shared edit was rejected after validation.");
            await ProjectAsync(item, CancellationToken.None, local: true);
        }
        finally { gate.Release(); }
    }

    public async Task DrainAsync(CancellationToken ct)
    {
        Task pending;
        lock (queueGate) pending = incoming;
        await pending.WaitAsync(ct);
        await gate.WaitAsync(ct);
        try
        {
            if (failures.Count > 0) throw new IOException("A shared scene has not been saved to the local project.");
        }
        finally { gate.Release(); }
    }

    private void Enqueue(Guid item, byte[] update)
    {
        lock (queueGate) incoming = ReceiveAfterAsync(incoming, item, update.ToArray());
    }

    private async Task ReceiveAfterAsync(Task previous, Guid item, byte[] update)
    {
        await previous;
        await DispatchRemote(() => ReceiveAsync(item, update));
    }

    private async Task ReceiveAsync(Guid item, byte[] update)
    {
        await gate.WaitAsync();
        try
        {
            if (disposed || !items.Contains(item)) return;
            await ReplayFailureAsync(item, CancellationToken.None);
            failures.Add(item);
            await ApplyAsync(item, update, CancellationToken.None);
            await ProjectAsync(item, CancellationToken.None);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            failures.Add(item);
            ProjectionError = error;
        }
        finally { gate.Release(); }
    }

    private async Task<SharedSceneSnapshot> ProjectAsync(Guid item, CancellationToken ct, bool local = false)
    {
        var snapshot = await documents.CaptureAsync(item, ct);
        await project(item, snapshot, local, ct);
        if (unpublished.TryGetValue(item, out var update) && Notify is { } notify)
            await notify(item, update ?? snapshot.State);
        unpublished.Remove(item);
        failures.Remove(item);
        if (failures.Count == 0) ProjectionError = null;
        return snapshot;
    }

    private async Task ReplayFailureAsync(Guid item, CancellationToken ct)
    {
        if (!failures.Contains(item)) return;
        foreach (var update in session.ReadItem(item)) await ApplyAsync(item, update, ct);
    }

    private async Task<bool> ApplyAsync(Guid item, byte[] update, CancellationToken ct)
    {
        var result = await documents.ReceiveAsync(item, update, ct);
        foreach (var rejected in result.Rejected) session.RejectUpdate(item, rejected);
        RejectedUpdates |= result.Rejected.Count > 0;
        if (result.Accepted)
            unpublished[item] = unpublished.ContainsKey(item) || result.Rejected.Count > 0 ? null : update;
        return result.Accepted;
    }

    private void EnsureItem(Guid item)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!started || !items.Contains(item)) throw new InvalidOperationException("The scene is not part of this collaboration session.");
    }

    public Task CloseAsync(CancellationToken ct) => StopAsync(closeSession: true, ct);

    public ValueTask DisposeAsync() => new(StopAsync(closeSession: false, CancellationToken.None));

    private async Task StopAsync(bool closeSession, CancellationToken ct)
    {
        if (disposed) return;
        await session.PauseAsync(ct);
        await gate.WaitAsync(ct);
        try
        {
            if (disposed) return;
            if (started)
            {
                // Replay the durable intake here: queued dispatches may need the
                // workspace lock held by the caller closing this project.
                foreach (var item in items)
                {
                    failures.Add(item);
                    await ReplayFailureAsync(item, ct);
                    await ProjectAsync(item, ct);
                }
            }
            if (closeSession) await session.CloseAsync(ct);
            disposed = true;
            session.UpdateReceived -= Enqueue;
            if (opened) await documents.DisposeAsync();
        }
        finally { gate.Release(); }
    }
}
