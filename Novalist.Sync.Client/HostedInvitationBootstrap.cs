using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using NSec.Cryptography;

namespace Novalist.Sync.Client;

internal sealed class HostedInvitationBootstrap : IDisposable
{
    private sealed record Bootstrap(int Version, string Server, Guid ProjectId, uint Epoch, Guid InvitationId,
        byte[] OwnerKey, byte[] Grant, byte[] DelegationKey, byte[] EpochKey, CheckpointReference Checkpoint);
    private readonly Bootstrap state;
    internal Uri Server { get; }
    internal Guid ProjectId => state.ProjectId;
    internal Guid InvitationId => state.InvitationId;
    internal string Fingerprint { get; }

    private HostedInvitationBootstrap(Bootstrap state, byte[] encoded)
    {
        if (state.Version != 1 || state.ProjectId == Guid.Empty || state.InvitationId == Guid.Empty || state.OwnerKey is not { Length: 32 } ||
            state.DelegationKey is not { Length: 32 } || state.EpochKey is not { Length: 32 } || state.Grant is not { Length: 150 })
            throw new InvalidDataException("The invitation is incomplete or unsupported.");
        ValidateCheckpoint(state.Checkpoint);
        Server = HostedProjectAccess.CanonicalServer(new Uri(state.Server, UriKind.Absolute));
        this.state = state;
        Fingerprint = Convert.ToHexStringLower(SHA256.HashData(encoded));
    }

    internal static string Encode(StoredProjectAccess access, Guid invitation, byte[] grant, byte[] delegation, CheckpointReference checkpoint)
    {
        ValidateCheckpoint(checkpoint);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Bootstrap(1, access.Server, access.ProjectId, access.Epoch, invitation,
            access.OwnerKey, grant, delegation, access.EpochKey, checkpoint));
        try { return "novalist://collaboration/join#" + Base64UrlEncoder.Encode(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    internal static HostedInvitationBootstrap Parse(string link)
    {
        if (link.Length > 8192 || !Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != "novalist" ||
            uri.Host != "collaboration" || uri.AbsolutePath != "/join" || uri.UserInfo.Length != 0 || uri.Port != -1 ||
            uri.Query.Length != 0 || uri.Fragment.Length < 2)
            throw new InvalidDataException("Use a complete Novalist collaboration invitation link.");
        var bytes = Base64UrlEncoder.DecodeBytes(uri.Fragment[1..]);
        try
        {
            var state = JsonSerializer.Deserialize<Bootstrap>(bytes, new JsonSerializerOptions { MaxDepth = 16 })
                ?? throw new InvalidDataException("The invitation is empty.");
            try { return new(state, bytes); }
            catch { Clear(state); throw; }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    internal HostedProjectAccess Claim(DeviceIdentity device)
    {
        var owner = PublicKey.Import(SignatureAlgorithm.Ed25519, state.OwnerKey, KeyBlobFormat.RawPublicKey);
        var membership = new ProjectMembership(state.ProjectId, state.Epoch, owner);
        using var delegation = Key.Import(SignatureAlgorithm.Ed25519, state.DelegationKey, KeyBlobFormat.RawPrivateKey);
        var certificate = membership.ClaimInvitation(state.Grant, delegation, device);
        return new(new(1, Server.AbsoluteUri, state.ProjectId, state.Epoch, device.Id, state.OwnerKey.ToArray(),
            state.EpochKey.ToArray(), certificate, state.InvitationId, state.Checkpoint, Fingerprint), device);
    }

    private static void ValidateCheckpoint(CheckpointReference? checkpoint)
    {
        if (checkpoint is null || checkpoint.ManifestId == Guid.Empty || checkpoint.Sha256 is not { Length: 64 } || !checkpoint.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("The invitation has no valid project checkpoint.");
    }

    private static void Clear(Bootstrap state)
    {
        if (state.EpochKey is not null) CryptographicOperations.ZeroMemory(state.EpochKey);
        if (state.DelegationKey is not null) CryptographicOperations.ZeroMemory(state.DelegationKey);
    }

    public void Dispose() => Clear(state);
}
