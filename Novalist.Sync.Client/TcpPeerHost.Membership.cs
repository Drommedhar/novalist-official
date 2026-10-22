namespace Novalist.Sync.Client;

public sealed partial class TcpPeerHost
{
    public IReadOnlyList<Guid> Members()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return members.Keys.ToArray();
        }
    }

    public void RemoveMember(Guid device)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (device == owner.Id) throw new InvalidOperationException("The host ends the session to leave it.");
            if (!members.Remove(device)) throw new InvalidOperationException("This writer is not a member of the session.");
            foreach (var key in invitations.Where(pair => pair.Value.ClaimedBy == device).Select(pair => pair.Key).ToArray())
                invitations.Remove(key);
            foreach (var resource in leases.Where(pair => pair.Value.DeviceId == device).Select(pair => pair.Key).ToArray())
                leases.Remove(resource);
            if (clients.Remove(device, out var client)) client.Stop();
        }
    }
}
