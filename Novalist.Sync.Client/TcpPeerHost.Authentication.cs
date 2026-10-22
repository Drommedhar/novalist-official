using System.Security.Cryptography;
using NSec.Cryptography;

namespace Novalist.Sync.Client;

public sealed partial class TcpPeerHost
{
    private readonly Dictionary<string, Invitation> invitations = [];
    private readonly Dictionary<Guid, byte[]> members = [];
    private sealed record Invitation(DateTimeOffset ExpiresAt, Guid? ClaimedBy = null);

    public PeerProjectAccess CreateOwnerAccess(string advertisedHost)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return new(Address(advertisedHost), Welcome(members[owner.Id]), owner);
        }
    }

    public PeerInvitation CreateInvitation(string advertisedHost, CheckpointReference? checkpoint = null, DateTimeOffset? expiresAt = null)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var address = Address(advertisedHost);
            var secret = RandomNumberGenerator.GetBytes(32);
            var expires = expiresAt ?? DateTimeOffset.UtcNow.AddHours(1);
            if (expires <= DateTimeOffset.UtcNow) throw new ArgumentException("Choose an invitation expiry in the future.", nameof(expiresAt));
            invitations.Add(Convert.ToHexString(SHA256.HashData(secret)), new(expires));
            return new(address, secret, expires, checkpoint);
        }
    }

    private PeerWelcome Welcome(byte[] certificate) => new(owner.SigningKey.PublicKey.Export(KeyBlobFormat.RawPublicKey), epochKey, certificate);

    private async Task<Guid> AuthenticateAsync(Stream stream, CancellationToken ct)
    {
        var challenge = new PeerChallenge(SessionId, RandomNumberGenerator.GetBytes(32));
        await PeerProtocol.WriteAsync(stream, challenge, ct);
        var hello = await PeerProtocol.ReadAsync<PeerHello>(stream, ct);
        byte[] certificate;
        try { lock (gate) certificate = Authenticate(challenge, hello); }
        catch (CryptographicException)
        {
            await PeerProtocol.WriteAsync(stream, new PeerWelcome([], [], [], "The peer invitation or device credentials were rejected."), ct);
            throw;
        }
        await PeerProtocol.WriteAsync(stream, Welcome(certificate), ct);
        return hello.DeviceId;
    }

    private byte[] Authenticate(PeerChallenge challenge, PeerHello hello)
    {
        if (hello.SigningKey is not { Length: 32 } || hello.ReceivingKey is not { Length: 32 } || hello.Signature is not { Length: 64 })
            throw new CryptographicException("Invalid peer device proof.");
        var signing = PublicKey.Import(SignatureAlgorithm.Ed25519, hello.SigningKey, KeyBlobFormat.RawPublicKey);
        var receiving = PublicKey.Import(KeyAgreementAlgorithm.X25519, hello.ReceivingKey, KeyBlobFormat.RawPublicKey);
        if (hello.DeviceId != DeviceIdentity.IdForKeys(signing, receiving) ||
            !SignatureAlgorithm.Ed25519.Verify(signing, PeerProtocol.Proof(challenge, hello), hello.Signature))
            throw new CryptographicException("Invalid peer device proof.");
        if (hello.Certificate is { } certificate)
        {
            var verified = membership.VerifyCertificate(hello.DeviceId, certificate);
            if (!verified.SigningKey.Equals(signing) || !verified.ReceivingKey.Equals(receiving) ||
                !members.TryGetValue(hello.DeviceId, out var admitted) || !admitted.AsSpan().SequenceEqual(certificate))
                throw new CryptographicException("This device is not a member of the peer session.");
            return certificate;
        }
        return ClaimInvitation(hello, signing, receiving);
    }

    private byte[] ClaimInvitation(PeerHello hello, PublicKey signing, PublicKey receiving)
    {
        if (hello.Invitation is not { Length: 32 }) throw new CryptographicException("A peer invitation is required.");
        var hash = Convert.ToHexString(SHA256.HashData(hello.Invitation));
        if (!invitations.TryGetValue(hash, out var invitation) ||
            (invitation.ExpiresAt <= DateTimeOffset.UtcNow && invitation.ClaimedBy != hello.DeviceId) ||
            (invitation.ClaimedBy is { } claimed && claimed != hello.DeviceId))
            throw new CryptographicException("The peer invitation is expired or already used.");
        if (members.TryGetValue(hello.DeviceId, out var existing))
        {
            invitations[hash] = invitation with { ClaimedBy = hello.DeviceId };
            return existing;
        }
        using var delegation = Key.Create(SignatureAlgorithm.Ed25519);
        var grant = membership.CreateInvitation(new(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1), invitation.ExpiresAt),
            delegation.PublicKey, owner.SigningKey);
        var certificate = membership.ClaimInvitation(grant, delegation, hello.DeviceId, signing, receiving);
        invitations[hash] = invitation with { ClaimedBy = hello.DeviceId };
        members[hello.DeviceId] = certificate;
        return certificate;
    }
}
