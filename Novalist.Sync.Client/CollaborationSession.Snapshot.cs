using System.Diagnostics;

namespace Novalist.Sync.Client;

public sealed partial class CollaborationSession
{
    public async Task<T> CaptureSnapshotAsync<T>(Func<long, CancellationToken, Task<T>> capture, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            await SynchronizeAsync(createProject: false, ct);
            await connectionGate.WaitAsync(ct);
            try
            {
                EnsureOpen();
                lock (receiveGate) paused = true;
                var sequence = journal.LastServerSequence;
                if (journal.Pending().Count != 0 || journal.PendingFieldEdits().Count != 0)
                    throw new InvalidOperationException("Save pending edits before capturing the shared project.");
                if (HasCompleteUpdates(sequence)) return await capture(sequence, ct);
            }
            finally
            {
                lock (receiveGate) paused = false;
                connectionGate.Release();
                Saved?.Invoke();
            }
            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(30))
                throw new TimeoutException("A collaborator's update is still incomplete. Retry joining after it finishes.");
            await Task.Delay(50, ct);
        }
    }

    private bool HasCompleteUpdates(long through)
    {
        foreach (var item in journal.CommittedItemsSince(0, through))
        {
            var messages = journal.ReadCommittedItemRecords(item, through)
                .Select(record => (record.SenderDeviceId, Packet: UpdatePacket.Parse(record.Payload)))
                .GroupBy(record => (record.SenderDeviceId, record.Packet.MessageId));
            if (messages.Any(message => UpdatePacket.Assemble(message.Select(record => record.Packet)) is null)) return false;
        }
        return true;
    }
}
