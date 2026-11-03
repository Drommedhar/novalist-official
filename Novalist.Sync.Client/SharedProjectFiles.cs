using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Novalist.Sync.Client;

public sealed record SharedFileChange(string Path, byte[]? Before, byte[]? After);
public sealed record SharedFileRecovery(string Id, string Path, byte[] Content);
public sealed record SharedProjectProjection(IReadOnlyList<SharedFileChange> Changes,
    IReadOnlyList<SharedFileRecovery> Recoveries, long Sequence, SharedProjectState AddedState);

public sealed class SharedProjectFiles : IAsyncDisposable
{
    private readonly CollaborationSession session;
    private readonly Func<SharedProjectProjection, CancellationToken, Task> apply;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SemaphoreSlim editing = new(1, 1);
    private readonly Dictionary<string, byte[]> files = new(StringComparer.OrdinalIgnoreCase);
    private SharedProjectState state = new([], []);
    private readonly Dictionary<string, long> deletedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Guid> deletedItems = [];
    private readonly object queueGate = new();
    private Task incoming = Task.CompletedTask;
    private long projectedSequence;
    private bool started;
    private bool disposed;
    private sealed record FileBatch(int Version, IReadOnlyList<SharedFileChange> Changes, SharedProjectState AddedState, long ObservedSequence = 0);

    public Guid ItemId { get; }
    public Func<Func<Task>, Task> DispatchRemote { get; set; } = action => action();
    public Exception? ProjectionError { get; private set; }
    public bool RejectedUpdates { get; private set; }

    public SharedProjectFiles(CollaborationSession session, Func<SharedProjectProjection, CancellationToken, Task> apply)
    {
        this.session = session;
        this.apply = apply;
        ItemId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"novalist:project-files:v1:{session.Context.ProjectId:N}"))[..16]);
    }

    public Task StartAsync(IReadOnlyDictionary<string, byte[]> initial, CancellationToken ct) => StartAsync(initial, new([], []), ct);

    public async Task StartAsync(IReadOnlyDictionary<string, byte[]> initial, SharedProjectState initialState, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (started) throw new InvalidOperationException("The shared project files are already open.");
            foreach (var (path, content) in initial)
                files.Add(path, await FilterContentAsync(path, content, ct));
            SharedProjectFileBindings.Validate(initialState, files);
            if (initialState.FileBaselines is { } baselines)
            {
                foreach (var (path, content) in baselines)
                {
                    if (!files.ContainsKey(path) || content is null) throw new InvalidDataException("A canonical shared project file is missing.");
                    files[path] = await FilterContentAsync(path, content, ct);
                }
                await CheckpointStorageReferences.ValidateAsync(files, ct);
            }
            state = SharedProjectFileBindings.Copy(initialState);
            started = true;
            session.CommittedHistoryChanged += Enqueue;
            await ProjectAsync(ct);
        }
        finally { gate.Release(); }
    }

    public Task CommitAsync(IReadOnlyList<SharedFileChange> changes, CancellationToken ct) => CommitAsync(changes, new([], []), ct);

    public async Task CommitAsync(IReadOnlyList<SharedFileChange> changes, SharedProjectState addedState, CancellationToken ct)
    {
        await editing.WaitAsync(ct);
        try
        {
            EnsureOpen();
            if (changes.Count == 0) return;
            var observed = Interlocked.Read(ref projectedSequence);
            var filtered = new List<SharedFileChange>();
            foreach (var change in changes)
                filtered.Add(new(change.Path,
                    change.Before is { } before ? await FilterContentAsync(change.Path, before, ct) : null,
                    change.After is { } after ? await FilterContentAsync(change.Path, after, ct) : null));
            var batch = new FileBatch(1, filtered, SharedProjectFileBindings.Copy(addedState), observed);
            Validate(batch);
            var request = await session.CreateFieldRequestAsync(ItemId, ct);
            var lease = await session.AcquireFieldAsync(request, ct);
            if (!lease.Acquired) throw new InvalidOperationException("Another collaborator is saving project changes. Retry shortly.");
            try
            {
                await RefreshAsync(ct);
                await session.CommitFieldAsync(ItemId, JsonSerializer.SerializeToUtf8Bytes(batch), request, ct);
                await RefreshAsync(ct);
            }
            finally { await session.ReleaseFieldAsync(request, ct); }
        }
        finally { editing.Release(); }
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { EnsureOpen(); await ProjectAsync(ct); }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyDictionary<string, byte[]>> SnapshotAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            EnsureOpen();
            await ProjectAsync(ct);
            return files.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
        }
        finally { gate.Release(); }
    }

    public async Task<SharedProjectState> SnapshotStateAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { EnsureOpen(); await ProjectAsync(ct); return SharedProjectFileBindings.Copy(state); }
        finally { gate.Release(); }
    }

    public static async Task<byte[]> FilterContentAsync(string path, byte[] content, CancellationToken ct)
    {
        SharedProjectPath.Validate(path);
        using var source = new MemoryStream(content, writable: false);
        using var projected = await SharedProjectMetadata.ProjectAsync(path, source, ct);
        return projected?.ToArray() ?? content.ToArray();
    }

    private async Task ProjectAsync(CancellationToken ct)
    {
        var through = session.CommittedSequence;
        foreach (var update in session.ReadCommittedItem(ItemId, through).Where(update => update.ServerSeq > projectedSequence))
        {
            var batch = await DecodeAsync(update, ct);
            if (batch is null) continue;
            var changes = new List<SharedFileChange>();
            var recoveries = new List<SharedFileRecovery>();
            foreach (var change in batch.Changes)
            {
                var current = files.GetValueOrDefault(change.Path);
                var merged = DeletedSince(batch, change) ? new SharedFileMergeResult(current, true) : SharedFileMerge.Apply(change, current);
                if (merged.Conflict && (change.After ?? current) is { } recovered)
                    recoveries.Add(new($"{update.ServerSeq}-{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(change.Path)))}", change.Path, recovered));
                if (!SharedFileMerge.Equal(current, merged.Content)) changes.Add(new(change.Path, current, merged.Content));
            }
            var nextFiles = new Dictionary<string, byte[]>(files, StringComparer.OrdinalIgnoreCase);
            foreach (var change in changes)
                if (change.After is { } content) nextFiles[change.Path] = content;
                else nextFiles.Remove(change.Path);
            var added = SharedProjectFileBindings.Accept(batch.AddedState, batch.Changes, nextFiles);
            SharedProjectState nextState;
            try
            {
                if (files.ContainsKey(".novalist/project.json") || nextFiles.ContainsKey(".novalist/project.json"))
                    await CheckpointStorageReferences.ValidateAsync(nextFiles, ct);
                nextState = SharedProjectFileBindings.Update(state, added, nextFiles);
            }
            catch (InvalidDataException) { Reject(update.Payload); continue; }
            await apply(new(changes, recoveries, update.ServerSeq, added), ct);
            foreach (var change in changes.Where(change => change.After is null)) deletedPaths[change.Path] = update.ServerSeq;
            foreach (var item in state.Documents.Select(document => document.ItemId).Concat(state.Fields.Select(field => field.ItemId))
                .Except(nextState.Documents.Select(document => document.ItemId).Concat(nextState.Fields.Select(field => field.ItemId))))
                deletedItems.Add(item);
            files.Clear();
            foreach (var (path, content) in nextFiles) files.Add(path, content);
            state = nextState;
            projectedSequence = update.ServerSeq;
        }
        projectedSequence = through;
        ProjectionError = null;
    }

    private bool DeletedSince(FileBatch batch, SharedFileChange change)
    {
        if (change.After is null) return false;
        if (deletedPaths.GetValueOrDefault(change.Path) > batch.ObservedSequence) return true;
        return batch.AddedState.Documents.Where(document => document.Path.Equals(change.Path, StringComparison.OrdinalIgnoreCase)).Select(document => document.ItemId)
            .Concat(batch.AddedState.Fields.Where(field => field.Path.Equals(change.Path, StringComparison.OrdinalIgnoreCase)).Select(field => field.ItemId))
            .Any(deletedItems.Contains);
    }

    private async Task<FileBatch?> DecodeAsync(CommittedUpdate update, CancellationToken ct)
    {
        try
        {
            var batch = JsonSerializer.Deserialize<FileBatch>(update.Payload) ?? throw new InvalidDataException("Missing project file batch.");
            Validate(batch);
            if (batch.ObservedSequence >= update.ServerSeq) throw new InvalidDataException("Invalid project change sequence.");
            var changes = new List<SharedFileChange>();
            foreach (var change in batch.Changes)
                changes.Add(new(change.Path,
                    change.Before is { } before ? await FilterContentAsync(change.Path, before, ct) : null,
                    change.After is { } after ? await FilterContentAsync(change.Path, after, ct) : null));
            return batch with { Changes = changes };
        }
        catch (Exception error) when (error is JsonException or InvalidDataException)
        {
            Reject(update.Payload);
            return null;
        }
    }

    private void Reject(byte[] payload)
    {
        session.RejectUpdate(ItemId, Convert.ToHexStringLower(SHA256.HashData(payload)));
        RejectedUpdates = true;
    }

    private static void Validate(FileBatch batch)
    {
        if (batch.Version != 1 || batch.ObservedSequence < 0 || batch.Changes is not { Count: > 0 }) throw new InvalidDataException("Unsupported project file batch.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var change in batch.Changes)
        {
            if (change is null || (change.Before is null && change.After is null)) throw new InvalidDataException("Missing project file change.");
            SharedProjectPath.Validate(change.Path);
            if (!paths.Add(change.Path.Normalize())) throw new InvalidDataException("Duplicate project file change.");
        }
        SharedProjectFileBindings.Validate(batch.AddedState,
            batch.Changes.Where(change => change.After is not null).ToDictionary(change => change.Path,
                change => change.After ?? throw new InvalidDataException("Missing project file content."), StringComparer.OrdinalIgnoreCase));
    }

    private void Enqueue()
    {
        lock (queueGate) incoming = ReceiveAfterAsync(incoming);
    }

    private async Task ReceiveAfterAsync(Task previous)
    {
        await previous;
        try
        {
            await DispatchRemote(async () =>
            {
                await gate.WaitAsync();
                try { if (!disposed) await ProjectAsync(CancellationToken.None); }
                finally { gate.Release(); }
            });
        }
        catch (Exception error) when (error is not OutOfMemoryException) { ProjectionError = error; }
    }

    private void EnsureOpen()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!started) throw new InvalidOperationException("The shared project files have not opened.");
    }

    public async ValueTask DisposeAsync()
    {
        await editing.WaitAsync();
        try
        {
            await gate.WaitAsync();
            try { session.CommittedHistoryChanged -= Enqueue; disposed = true; }
            finally { gate.Release(); }
        }
        finally { editing.Release(); }
    }
}
