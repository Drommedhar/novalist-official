using System.Security.Cryptography;
using System.Text.Json;
using NSec.Cryptography;

namespace Novalist.Sync.Client;

public sealed partial class HostedProjectAccessStore
{
    private readonly ICollaborationCredentials credentials;
    private readonly SemaphoreSlim gate = new(1, 1);

    public HostedProjectAccessStore(ICollaborationCredentials credentials) => this.credentials = credentials;

    public async Task<HostedProjectAccess> CreateOwnerAsync(Uri server, DeviceIdentity device, CancellationToken ct)
    {
        server = HostedProjectAccess.CanonicalServer(server);
        var project = Guid.NewGuid();
        var membership = new ProjectMembership(project, 0, device.SigningKey.PublicKey);
        var state = new StoredProjectAccess(1, server.AbsoluteUri, project, 0, device.Id,
            device.SigningKey.PublicKey.Export(KeyBlobFormat.RawPublicKey), RandomNumberGenerator.GetBytes(32),
            membership.CreateOwnerCertificate(device), null, null, null);
        var access = new HostedProjectAccess(state, device);
        try { await SaveAsync(access, ct); return access; }
        catch { access.Dispose(); throw; }
    }

    public async Task<HostedProjectAccess?> LoadAsync(Uri server, Guid project, DeviceIdentity device, CancellationToken ct)
    {
        server = HostedProjectAccess.CanonicalServer(server);
        return await ReadAsync(server, project, device, StorageKey(server, project, device.Id), ct);
    }

    private async Task<HostedProjectAccess?> ReadAsync(Uri server, Guid project, DeviceIdentity device, string key, CancellationToken ct)
    {
        var json = await credentials.ReadAsync(key, ct);
        if (json is null) return null;
        var state = JsonSerializer.Deserialize<StoredProjectAccess>(json) ?? throw new InvalidDataException("Missing project credentials.");
        try
        {
            if (state.Server != server.AbsoluteUri || state.ProjectId != project)
                throw new CryptographicException("The saved project credentials belong to another project or server.");
            return new(state, device);
        }
        catch { if (state.EpochKey is not null) CryptographicOperations.ZeroMemory(state.EpochKey); throw; }
    }

    public async Task<HostedProjectAccess> PrepareJoinAsync(string link, DeviceIdentity device, CancellationToken ct)
    {
        using var bootstrap = HostedInvitationBootstrap.Parse(link);
        await gate.WaitAsync(ct);
        try
        {
            var saved = await LoadAsync(bootstrap.Server, bootstrap.ProjectId, device, ct);
            if (saved is not null) return MatchingInvitation(saved, bootstrap);
            var pendingKey = PendingKey(bootstrap.Server, bootstrap.ProjectId, device.Id, bootstrap.InvitationId);
            var pending = await ReadAsync(bootstrap.Server, bootstrap.ProjectId, device, pendingKey, ct);
            if (pending is not null) return MatchingInvitation(pending, bootstrap);
            var access = bootstrap.Claim(device);
            try
            {
                await PinOwnerAsync(access, ct);
                await credentials.WriteAsync(pendingKey, JsonSerializer.Serialize(access.State), ct);
                return access;
            }
            catch { access.Dispose(); throw; }
        }
        finally { gate.Release(); }
    }

    public async Task ActivateAsync(HostedProjectAccess access, HostedTransport transport, CancellationToken ct)
    {
        access.CheckTransport(transport);
        if (access.State.InvitationId is not { } invitation) throw new InvalidOperationException("These project credentials did not come from an invitation.");
        await gate.WaitAsync(ct);
        try
        {
            var expected = JsonSerializer.Serialize(access.State);
            var active = await credentials.ReadAsync(StorageKey(access.Server, access.ProjectId, access.State.DeviceId), ct);
            if (active is not null && active != expected)
                throw new InvalidOperationException("This project was already joined with different credentials.");
            var pending = active ?? await credentials.ReadAsync(PendingKey(access.Server, access.ProjectId, access.State.DeviceId, invitation), ct);
            if (pending != expected) throw new InvalidOperationException("Save the joining credentials before claiming the invitation.");
            await transport.ActivateInvitationAsync(access.ProjectId, invitation, SHA256.HashData(access.Certificate), ct);
            await SaveAsync(access, ct);
        }
        finally { gate.Release(); }
    }

    private static HostedProjectAccess MatchingInvitation(HostedProjectAccess access, HostedInvitationBootstrap bootstrap)
    {
        if (access.State.InvitationHash == bootstrap.Fingerprint && access.State.InvitationId == bootstrap.InvitationId) return access;
        access.Dispose();
        throw new InvalidOperationException("This device already has different active credentials for that shared project.");
    }

    private async Task PinOwnerAsync(HostedProjectAccess access, CancellationToken ct)
    {
        var key = "project-anchor:" + StorageKey(access.Server, access.ProjectId, access.State.DeviceId);
        var expected = JsonSerializer.Serialize(new { access.Epoch, Owner = Convert.ToHexStringLower(access.State.OwnerKey) });
        var previous = await credentials.ReadAsync(key, ct);
        if (previous is not null && previous != expected) throw new CryptographicException("The invitation changes the pinned project authority.");
        if (previous is null) await credentials.WriteAsync(key, expected, ct);
    }

    private Task SaveAsync(HostedProjectAccess access, CancellationToken ct) => credentials.WriteAsync(
        StorageKey(access.Server, access.ProjectId, access.State.DeviceId), JsonSerializer.Serialize(access.State), ct);

    private static string StorageKey(Uri server, Guid project, Guid device) =>
        "project:" + JsonSerializer.Serialize(new[] { server.AbsoluteUri, project.ToString("D"), device.ToString("D") });

    private static string PendingKey(Uri server, Guid project, Guid device, Guid invitation) =>
        "project-pending:" + invitation.ToString("D") + ":" + StorageKey(server, project, device);
}
