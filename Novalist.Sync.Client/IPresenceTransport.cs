namespace Novalist.Sync.Client;

public interface IPresenceTransport
{
    event Func<UpdateEnvelope, Task>? PresenceReceived;
    bool IsConnected { get; }
    Task PublishPresenceAsync(UpdateEnvelope envelope, CancellationToken cancellationToken);
}
