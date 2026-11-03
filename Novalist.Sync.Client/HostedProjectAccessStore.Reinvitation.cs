using System.Security.Cryptography;
using System.Text.Json;

namespace Novalist.Sync.Client;

public sealed partial class HostedProjectAccessStore
{
    private sealed record ProjectAnchor(uint Epoch, string Owner);

    private static void ValidateReplacement(StoredProjectAccess previous, HostedProjectAccess next)
    {
        if (previous.Server != next.State.Server || previous.ProjectId != next.ProjectId || previous.DeviceId != next.State.DeviceId ||
            !previous.OwnerKey.AsSpan().SequenceEqual(next.State.OwnerKey))
            throw new CryptographicException("The replacement invitation changes the pinned project authority.");
        if (previous.Epoch >= next.Epoch)
            throw new InvalidOperationException("A replacement invitation must use newer project keys. Reconnect to continue with the current access.");
    }

    private async Task ArchivePreviousAsync(string json, HostedProjectAccess next, CancellationToken ct)
    {
        var previous = JsonSerializer.Deserialize<StoredProjectAccess>(json) ?? throw new InvalidDataException("Missing previous project credentials.");
        try
        {
            ValidateReplacement(previous, next);
            var key = StorageKey(next.Server, next.ProjectId, next.State.DeviceId);
            await credentials.WriteAsync("project-history:" + previous.Epoch + ":" + key, json, ct);
        }
        finally { CryptographicOperations.ZeroMemory(previous.EpochKey); }
    }
}
