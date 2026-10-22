using System.Diagnostics;

namespace Novalist.Sync.Client;

internal sealed class BootstrapProgress(IProgress<CollaborationTransferProgress>? progress)
{
    private long reported;
    private CollaborationTransferProgress current = new("", 0, 0, 0, 0);

    internal void Report(string phase, int completed = 0, int total = 0, long bytes = 0, long totalBytes = 0, int reused = 0)
    {
        var changed = phase != current.Phase;
        current = new(phase, completed, total, bytes, totalBytes, reused);
        if (!changed && Stopwatch.GetElapsedTime(reported) < TimeSpan.FromMilliseconds(100)) return;
        reported = Stopwatch.GetTimestamp();
        progress?.Report(current);
    }

    internal void AddBytes(int bytes) => Report(current.Phase, current.CompletedFiles, current.TotalFiles,
        current.CompletedBytes + bytes, current.TotalBytes, current.ReusedFiles);

    internal void CompleteFile() => Report(current.Phase, current.CompletedFiles + 1, current.TotalFiles,
        current.CompletedBytes, current.TotalBytes, current.ReusedFiles);

    internal void Phase(string phase) => Report(phase, current.CompletedFiles, current.TotalFiles,
        current.CompletedBytes, current.TotalBytes, current.ReusedFiles);
}
