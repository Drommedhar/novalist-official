namespace Novalist.Sync.Client;

public interface IUpdateTransport : IAsyncDisposable
{
    event Func<SequencedUpdate, Task>? Received;
    bool IsConnected { get; }
    string ConnectionIdentity { get; }
    Task ConnectAsync(CancellationToken cancellationToken);
    Task CreateProjectAsync(Guid project, CancellationToken cancellationToken);
    Task JoinAsync(Guid project, CancellationToken cancellationToken);
    Task<long> PushAsync(UpdateEnvelope envelope, CancellationToken cancellationToken);
    IAsyncEnumerable<SequencedUpdate> CatchUpAsync(Guid project, long after, CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
}
