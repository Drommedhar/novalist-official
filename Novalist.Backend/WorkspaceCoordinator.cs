namespace Novalist.Backend;

/// <summary>
/// Owns the shared workspace transaction. Windows freeze and save before a
/// mutation, then acknowledge the same snapshot before editing resumes.
/// </summary>
internal sealed class WorkspaceCoordinator(Workspace workspace, SemaphoreSlim gate)
{
    private readonly object _sync = new();
    private readonly HashSet<string> _clients = new(StringComparer.Ordinal);
    private readonly AsyncLocal<Transition?> _ownedTransition = new();
    private Transition? _transition;
    private long _epoch;

    internal Func<string, object, Task>? Notify { get; set; }
    internal TimeSpan AcknowledgementTimeout { get; set; } = TimeSpan.FromSeconds(20);
    internal long Epoch => Interlocked.Read(ref _epoch);

    internal static bool IsControl(string? method) => method is
        "workspace/prepared" or "workspace/applied" or "workspace/clientClosed" or "workspace/snapshot";

    internal static bool ChangesWorkspace(string? method) => method is
        "project/open" or "project/create" or "project/close" or "project/switchBook"
        or "project/switchDraft" or "project/deleteDraft" or "project/createDraft"
        or "backup/restore" or "backup/restoreAsNewProject" or "manuscriptImport/run"
        or "drafts/transfer" or "drafts/duplicate" or "sceneBulk/archive" or "sceneBulk/delete"
        or "sceneBulk/moveToChapter" or "project/deleteScene" or "project/deleteChapter"
        or "project/moveScenes" or "sceneSplit/split" or "sceneSplit/merge"
        or "snapshots/restore" or "search/replaceAll";

    internal void Validate(WorkspaceRequestIdentity? identity, string? method)
    {
        if (IsControl(method))
        {
            if (identity?.Owner != "0") throw new InvalidOperationException("Only the window coordinator can control a workspace transaction.");
            return;
        }
        if (identity == null) return; // Existing in-process/mobile clients keep their single-workspace protocol.
        lock (_sync)
        {
            if (identity.Epoch != _epoch) throw new InvalidOperationException("The workspace changed. Reload before retrying.");
            if (identity.Owner != "0") _clients.Add(identity.Owner);
            if (_transition != null && ChangesWorkspace(method))
                throw new InvalidOperationException("A workspace change is already in progress.");
            if (identity.FlushToken != null && identity.FlushToken != _transition?.Token)
                throw new InvalidOperationException("The workspace save transaction has expired.");
        }
    }

    internal object Snapshot() => new { epoch = Epoch, state = workspace.BuildState(), draftId = workspace.Projects.ActiveBook?.ActiveDraftId };

    internal void Prepared(string token, bool saved, string? error)
    {
        lock (_sync)
        {
            if (_transition?.Token != token) return;
            if (saved) _transition.Prepared.TrySetResult();
            else _transition.Prepared.TrySetException(new InvalidOperationException(error ?? "A window could not save its pending edits."));
        }
    }

    internal void Applied(string token)
    {
        lock (_sync)
            if (_transition?.Token == token) _transition.Applied.TrySetResult();
    }

    internal void ClientClosed(string owner)
    {
        lock (_sync) _clients.Remove(owner);
        workspace.Editing.RemoveOwner(owner);
    }

    internal async Task RunAsync(string reason, Func<Task> action)
        => await RunAsync(reason, async () => { await action().ConfigureAwait(false); return true; }).ConfigureAwait(false);

    internal async Task<T> RunAsync<T>(string reason, Func<Task<T>> action)
    {
        // An SDK call inside an already coordinated import/switch shares that
        // transaction; intermediate book visits must not reach the windows.
        if (_ownedTransition.Value is { Active: true } owned)
        {
            if (!owned.Mutating || !ReferenceEquals(WorkspaceRequestContext.Lease, owned.Lease) || !owned.Lease.Held)
                throw new InvalidOperationException("The workspace transaction is waiting for its windows.");
            return await action().ConfigureAwait(false);
        }
        var expectedEpoch = WorkspaceRequestContext.Identity?.Epoch ?? Epoch;
        lock (_sync)
        {
            if (_transition != null) throw new InvalidOperationException("A workspace change is already in progress.");
        }
        if (Notify == null || !HasClients()) return await action().ConfigureAwait(false);

        var lease = WorkspaceRequestContext.Lease;
        var ownsLease = lease == null;
        if (ownsLease)
        {
            await gate.WaitAsync().ConfigureAwait(false);
            lease = new WorkspaceGateLease(gate);
        }

        var transition = new Transition(Guid.NewGuid().ToString("N"), lease!);
        var previous = _ownedTransition.Value;
        using var context = WorkspaceRequestContext.Enter(WorkspaceRequestContext.Identity, lease);
        var installed = false;
        var mutationStarted = false;
        T result;
        try
        {
            lock (_sync)
            {
                if (expectedEpoch != _epoch)
                    throw new InvalidOperationException("The workspace changed. Reload before retrying.");
                if (_transition != null) throw new InvalidOperationException("A workspace change is already in progress.");
                _transition = transition;
                installed = true;
            }
            _ownedTransition.Value = transition;
            lease!.Yield();
            try
            {
                await Notify("workspace/prepare", new { token = transition.Token, epoch = Epoch, reason }).ConfigureAwait(false);
                await transition.Prepared.Task.WaitAsync(AcknowledgementTimeout).ConfigureAwait(false);
            }
            finally { await lease.ReacquireAsync().ConfigureAwait(false); }

            mutationStarted = true;
            transition.Mutating = true;
            try { result = await action().ConfigureAwait(false); }
            finally
            {
                transition.Mutating = false;
                // Even a failing mutation can have changed part of the tree.
                // Publish the actual resulting state before accepting new edits.
                Interlocked.Increment(ref _epoch);
                if (WorkspaceRequestContext.Identity is { } identity) identity.Epoch = Epoch;
                workspace.Editing.ClearOwners();
                await Notify("workspace/changed", new
                {
                    token = transition.Token,
                    epoch = Epoch,
                    state = workspace.BuildState(),
                    draftId = workspace.Projects.ActiveBook?.ActiveDraftId
                }).ConfigureAwait(false);
                await transition.Applied.Task.WaitAsync(AcknowledgementTimeout).ConfigureAwait(false);
            }
        }
        catch
        {
            // Once the mutation began an unacknowledged window must remain
            // frozen until it obtains the new snapshot. Never unlock stale UI.
            if (installed && !mutationStarted)
                await Notify("workspace/aborted", new { token = transition.Token, epoch = Epoch }).ConfigureAwait(false);
            throw;
        }
        finally
        {
            transition.Active = false;
            _ownedTransition.Value = previous;
            if (installed) lock (_sync) _transition = null;
            if (ownsLease)
            {
                lease!.Active = false;
                lease.Yield();
            }
        }
        return result;
    }

    private bool HasClients()
    {
        lock (_sync) return _clients.Count > 0;
    }

    private sealed class Transition(string token, WorkspaceGateLease lease)
    {
        internal string Token { get; } = token;
        internal WorkspaceGateLease Lease { get; } = lease;
        internal bool Active { get; set; } = true;
        internal bool Mutating { get; set; }
        internal TaskCompletionSource Prepared { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Applied { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
