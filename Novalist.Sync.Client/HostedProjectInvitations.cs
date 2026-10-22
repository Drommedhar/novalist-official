using System.Security.Cryptography;
using NSec.Cryptography;

namespace Novalist.Sync.Client;

public sealed record HostedInvitationOptions(CheckpointReference Checkpoint, DateTimeOffset ExpiresAt);

public static class HostedProjectInvitations
{
    public static async Task<string> CreateAsync(HostedProjectAccess access, DeviceIdentity device, HostedTransport transport,
        HostedInvitationOptions options, CancellationToken ct)
    {
        access.CheckTransport(transport);
        if (!transport.IsConnected) throw new InvalidOperationException("Connect before inviting a collaborator.");
        var identity = access.CreateSessionIdentity(device);
        if (identity.Membership.VerifyCertificate(device.Id, identity.Certificate).Role != ProjectRole.Owner)
            throw new InvalidOperationException("Only the project owner can invite collaborators.");
        using var delegation = Key.Create(SignatureAlgorithm.Ed25519, new() { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var invitation = Guid.NewGuid();
        var grant = identity.Membership.CreateInvitation(new(Guid.NewGuid(), DateTimeOffset.UtcNow, options.ExpiresAt),
            delegation.PublicKey, device.SigningKey);
        var secret = delegation.Export(KeyBlobFormat.RawPrivateKey);
        try
        {
            var link = HostedInvitationBootstrap.Encode(access.State, invitation, grant, secret, options.Checkpoint);
            await transport.CreateInvitationAsync(access.ProjectId, invitation, options.ExpiresAt, ct);
            return link;
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }
}
