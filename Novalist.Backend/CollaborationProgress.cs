#if NOVALIST_COLLABORATION
using Novalist.Sync.Client;

namespace Novalist.Backend;

internal sealed class CollaborationProgress(Workspace workspace) : IProgress<CollaborationTransferProgress>
{
    private readonly object gate = new();
    private Task notifications = Task.CompletedTask;
    public void Report(CollaborationTransferProgress value)
    {
        lock (gate) notifications = SendAfterAsync(notifications, value);
    }
    private async Task SendAfterAsync(Task previous, CollaborationTransferProgress value)
    {
        await previous;
        await workspace.ReportCollaborationProgressAsync(value);
    }
    internal Task FlushAsync() { lock (gate) return notifications; }
}

#endif
