namespace Novalist.Sync.Client;

public sealed partial class TcpPeerHost
{
    private readonly SemaphoreSlim preparingBootstrap = new(1, 1);
    private (string Path, long Sequence)? bootstrap;
    public Func<CancellationToken, Task>? PrepareBootstrap { get; set; }

    public void SetBootstrap(string archivePath, long sequence = 0)
    {
        var path = Path.GetFullPath(archivePath);
        if (!File.Exists(path)) throw new FileNotFoundException("The local project snapshot is missing.");
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (sequence < 0 || sequence > log.LastSequence) throw new ArgumentOutOfRangeException(nameof(sequence));
            bootstrap = (path, sequence);
        }
    }

    private async Task<PeerMessage> DispatchBootstrapAsync(PeerClient client, PeerRequest request, CancellationToken ct)
    {
        try
        {
            if (request.Id <= 0 || request.Project != project)
                throw new InvalidDataException("The bootstrap request belongs to another project.");
            if (request.Operation == "bootstrap-begin")
            {
                await preparingBootstrap.WaitAsync(ct);
                try
                {
                    if (PrepareBootstrap is { } prepare) await prepare(ct);
                    (string Path, long Sequence) snapshot;
                    lock (gate) snapshot = bootstrap ?? throw new InvalidOperationException("The host has not prepared its project.");
                    var archive = await PeerBootstrapArchive.OpenAsync(snapshot.Path, snapshot.Sequence, ct);
                    client.Bootstrap?.Dispose();
                    client.Bootstrap = archive;
                    lock (gate)
                    {
                        if (!members.ContainsKey(client.Device)) throw new InvalidOperationException("This device is no longer a member of the session.");
                        return new(request.Id, "reply", Bootstrap: archive.Description);
                    }
                }
                finally { preparingBootstrap.Release(); }
            }
            if (request.Operation == "bootstrap-read" && request.BootstrapEntry is { } entry && client.Bootstrap is { } source)
            {
                var bytes = await source.ReadAsync(entry, request.Offset, ct);
                lock (gate)
                {
                    if (!members.ContainsKey(client.Device)) throw new InvalidOperationException("This device is no longer a member of the session.");
                    return new(request.Id, "reply", Data: bytes);
                }
            }
            throw new InvalidOperationException("Begin project comparison before transferring files.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or
            ArgumentException or TimeoutException or System.Text.Json.JsonException)
        {
            return new(request.Id, "reply", Error: "The host could not prepare or read the shared project.");
        }
    }
}
