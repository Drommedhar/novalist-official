namespace Novalist.Sync.Client;

public sealed class SteamInvitationInbox
{
    private readonly Queue<SteamJoinRequest> pending = new();
    public SteamJoinRequest? Pending => pending.TryPeek(out var request) ? request : null;

    public void Receive(string connectString, ulong sender = 0, string? friendName = null)
    {
        SteamJoinRequest request;
        try
        {
            var invitation = SteamInviteConnectString.Decode(connectString);
            if (sender != 0 && sender != invitation.HostSteamId)
                throw new InvalidDataException("The Steam invitation does not belong to its sender.");
            if (invitation.Invitation.ExpiresAt <= DateTimeOffset.UtcNow)
                throw new InvalidDataException("This invitation has expired. Ask the host for a new invitation.");
            request = new(Guid.NewGuid(), invitation.ToLink(), friendName, null);
            if (pending.Any(existing => existing.Link == request.Link)) return;
        }
        catch (Exception error) when (error is InvalidDataException or FormatException or ArgumentException)
        {
            request = new(Guid.NewGuid(), null, friendName, "This Steam invitation is invalid or expired. Ask the host for a new invitation.");
        }
        if (pending.Count >= 16) pending.Dequeue();
        pending.Enqueue(request);
    }

    public void Dismiss(Guid id)
    {
        if (Pending?.Id == id) pending.Dequeue();
    }
}
