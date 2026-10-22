using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Novalist.Sync.Client;

public sealed record SharedFieldFocus(FieldFocusResult Lease, string Value);

public sealed class SharedFieldWorkspace : IAsyncDisposable
{
    private readonly CollaborationSession session;
    private readonly Func<Guid, string, CancellationToken, Task> project;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SemaphoreSlim editing = new(1, 1);
    private readonly CancellationTokenSource stopping = new();
    private readonly Dictionary<Guid, (string Value, long Sequence)> fields = [];
    private readonly object queueGate = new();
    private Task incoming = Task.CompletedTask;
    private long projectedSequence;
    private bool started;
    private bool disposed;
    private volatile bool closing;
    private sealed record FieldValue(int Version, string Value);
    public Func<Func<Task>, Task> DispatchRemote { get; set; } = action => action();
    public Exception? ProjectionError { get; private set; }
    public bool RejectedUpdates { get; private set; }

    public SharedFieldWorkspace(CollaborationSession session, Func<Guid, string, CancellationToken, Task> project)
    {
        this.session = session;
        this.project = project;
    }

    public async Task StartAsync(IReadOnlyDictionary<Guid, string> initial, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (started) throw new InvalidOperationException("The shared fields are already open.");
            if (initial.Any(field => field.Key == Guid.Empty || field.Value is null))
                throw new ArgumentException("Each shared field requires an identity and a value.", nameof(initial));
            foreach (var (item, value) in initial) fields.Add(item, (value, 0));
            started = true;
            session.CommittedHistoryChanged += Enqueue;
            await ProjectAsync(ct);
        }
        finally { gate.Release(); }
    }

    public Task<SharedFieldFocus> AcquireAsync(FieldLeaseRequest request, CancellationToken ct) => EditAsync(async token =>
    {
        await ReadAsync(request.ResourceId, token);
        var lease = await session.AcquireFieldAsync(request, token);
        var startedAt = Stopwatch.GetTimestamp();
        var value = await ReadAsync(request.ResourceId, token);
        var remaining = lease.RemainingMilliseconds - (int)Math.Ceiling(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        if (remaining <= 0) throw new TimeoutException("The field editing lease expired while saving the previous edits locally.");
        return new SharedFieldFocus(lease with { RemainingMilliseconds = remaining }, value);
    }, ct);

    public async Task AddAsync(IReadOnlyDictionary<Guid, string> added, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!started) throw new InvalidOperationException("The shared fields have not opened.");
            foreach (var (item, value) in added)
            {
                if (item == Guid.Empty || value is null) throw new InvalidDataException("A shared field needs an identity and a value.");
                fields.TryAdd(item, (value, 0));
            }
            projectedSequence = 0;
            await ProjectAsync(ct);
        }
        finally { gate.Release(); }
    }

    public Task WriteAsync(FieldLeaseRequest request, string value, CancellationToken ct) => EditAsync(async token =>
    {
        await ReadAsync(request.ResourceId, token);
        ArgumentNullException.ThrowIfNull(value);
        await session.CommitFieldAsync(request.ResourceId, JsonSerializer.SerializeToUtf8Bytes(new FieldValue(1, value)), request, token);
        await RefreshAsync(token);
        return true;
    }, ct);

    private async Task<T> EditAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        await editing.WaitAsync(ct);
        try
        {
            if (closing) throw new InvalidOperationException("The shared fields are closing.");
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct, stopping.Token);
            return await action(operation.Token);
        }
        finally { editing.Release(); }
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!started) throw new InvalidOperationException("The shared fields have not opened.");
            await ProjectAsync(ct);
        }
        finally { gate.Release(); }
    }

    private async Task ProjectAsync(CancellationToken ct)
    {
        var through = session.CommittedSequence;
        foreach (var item in session.CommittedItemsSince(projectedSequence, through))
        {
            if (!fields.ContainsKey(item)) continue;
            foreach (var update in session.ReadCommittedItem(item, through).Where(update => update.ServerSeq > fields[item].Sequence))
            {
                if (Decode(item, update.Payload) is not { } value) continue;
                if (fields[item].Value != value) await project(item, value, ct);
                fields[item] = (value, update.ServerSeq);
            }
        }
        projectedSequence = through;
        ProjectionError = null;
    }

    private string? Decode(Guid item, byte[] payload)
    {
        try
        {
            var value = JsonSerializer.Deserialize<FieldValue>(payload);
            if (value is not { Version: 1, Value: not null }) throw new InvalidDataException("Unsupported shared field value.");
            return value.Value;
        }
        catch (Exception error) when (error is JsonException or InvalidDataException)
        {
            session.RejectUpdate(item, Convert.ToHexStringLower(SHA256.HashData(payload)));
            RejectedUpdates = true;
            return null;
        }
    }

    public async Task<string> ReadAsync(Guid item, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            EnsureItem(item);
            await ProjectAsync(ct);
            return fields[item].Value;
        }
        finally { gate.Release(); }
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

    private void EnsureItem(Guid item)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!started || !fields.ContainsKey(item)) throw new InvalidOperationException("This field is not part of the shared project.");
    }

    public async Task CloseAsync(CancellationToken ct)
    {
        lock (queueGate)
        {
            if (disposed) return;
            closing = true;
            stopping.Cancel();
        }
        await session.PauseAsync(ct);
        await editing.WaitAsync(ct);
        try
        {
            await gate.WaitAsync(ct);
            try
            {
                if (disposed) return;
                if (started) await ProjectAsync(ct);
                session.CommittedHistoryChanged -= Enqueue;
                lock (queueGate) { disposed = true; stopping.Dispose(); }
            }
            finally { gate.Release(); }
        }
        finally { editing.Release(); }
    }

    public async ValueTask DisposeAsync() => await CloseAsync(CancellationToken.None);
}
