using System.Text.Json;

namespace Novalist.Sync.Client;

public sealed partial class HostedProjectAccessStore
{
    public async Task<HostedProjectAccess?> RefreshEpochAsync(HostedProjectAccess access, DeviceIdentity device, HostedTransport transport, CancellationToken ct)
    {
        access.CheckTransport(transport);
        var grant = await transport.GetProjectEpochGrantAsync(access.ProjectId, access.Epoch, ct);
        if (grant is null) return null;
        var next = ProjectEpochGrant.Open(access, device, grant);
        try
        {
            await gate.WaitAsync(ct);
            try
            {
                var key = StorageKey(access.Server, access.ProjectId, device.Id);
                using var current = await ReadAsync(access.Server, access.ProjectId, device, key, ct);
                if (current is not null && current.Epoch > access.Epoch)
                    throw new InvalidOperationException("The saved project keys changed during refresh. Reopen the shared project.");
                await credentials.WriteAsync("project-history:" + access.Epoch + ":" + key, JsonSerializer.Serialize(access.State), ct);
                await SaveAsync(next, ct);
            }
            finally { gate.Release(); }
            return next;
        }
        catch { next.Dispose(); throw; }
    }
}
