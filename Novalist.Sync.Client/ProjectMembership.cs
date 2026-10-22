using System.Buffers.Binary;
using System.Security.Cryptography;
using NSec.Cryptography;

namespace Novalist.Sync.Client;

public enum ProjectRole : byte { Owner = 1, Contributor = 2 }
public sealed record InvitationValidity(Guid MemberId, DateTimeOffset NotBefore, DateTimeOffset ExpiresAt);
public sealed record AuthorizedProjectDevice(Guid MemberId, Guid DeviceId, ProjectRole Role, PublicKey SigningKey, PublicKey ReceivingKey);

public sealed class ProjectMembership
{
    private const int GrantBodySize = 86;
    private const int GrantSize = GrantBodySize + 64;
    private const int CertificateBodySize = 118;
    public const int MaxCertificateBytes = CertificateBodySize + GrantSize + 64;
    private static readonly SignatureAlgorithm Signing = SignatureAlgorithm.Ed25519;
    private readonly Guid project;
    private readonly uint epoch;
    private readonly PublicKey owner;
    private readonly TimeProvider clock;

    public ProjectMembership(Guid project, uint epoch, PublicKey owner, TimeProvider? clock = null)
    {
        if (project == Guid.Empty) throw new ArgumentException("A project identity is required.", nameof(project));
        if (owner.Algorithm != Signing) throw new ArgumentException("An Ed25519 owner key is required.", nameof(owner));
        this.project = project;
        this.epoch = epoch;
        this.owner = owner;
        this.clock = clock ?? TimeProvider.System;
    }

    public byte[] CreateOwnerCertificate(DeviceIdentity device)
    {
        RequireKey(device.SigningKey, owner);
        return CreateCertificate(device.Id, device, ProjectRole.Owner, [], device.SigningKey);
    }

    internal void CheckSession(SessionContext context)
    {
        if (context.ProjectId != project || context.Epoch != epoch)
            throw new CryptographicException("The authorization belongs to another session context.");
    }

    internal byte[] ExportAuthority()
    {
        var bytes = new byte[36];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, epoch);
        owner.Export(KeyBlobFormat.RawPublicKey).CopyTo(bytes, 4);
        return bytes;
    }

    public byte[] CreateInvitation(InvitationValidity validity, PublicKey delegation, Key ownerKey)
    {
        RequireKey(ownerKey, owner);
        if (validity.MemberId == Guid.Empty || validity.NotBefore.ToUnixTimeSeconds() >= validity.ExpiresAt.ToUnixTimeSeconds())
            throw new ArgumentException("A member and a valid invitation interval are required.", nameof(validity));
        if (delegation.Algorithm != Signing) throw new ArgumentException("An Ed25519 delegation key is required.", nameof(delegation));
        var body = new byte[GrantBodySize];
        WriteContext(body, validity.MemberId, ProjectRole.Contributor);
        BinaryPrimitives.WriteInt64BigEndian(body.AsSpan(38, 8), validity.NotBefore.ToUnixTimeSeconds());
        BinaryPrimitives.WriteInt64BigEndian(body.AsSpan(46, 8), validity.ExpiresAt.ToUnixTimeSeconds());
        delegation.Export(KeyBlobFormat.RawPublicKey).CopyTo(body, 54);
        return Sign("novalist/v1/project-grant"u8, body, ownerKey);
    }

    public byte[] ClaimInvitation(byte[] grant, Key delegation, DeviceIdentity device)
        => ClaimInvitation(grant, delegation, device.Id, device.SigningKey.PublicKey, device.ReceivingKey.PublicKey);

    public byte[] ClaimInvitation(byte[] grant, Key delegation, Guid device, PublicKey signing, PublicKey receiving)
    {
        var verified = VerifyGrant(grant);
        RequireKey(delegation, verified.Key);
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        if (now < verified.NotBefore || now >= verified.ExpiresAt)
            throw new InvalidOperationException("The invitation is not currently valid.");
        if (device != DeviceIdentity.IdForKeys(signing, receiving))
            throw new CryptographicException("The invited device identity does not match its keys.");
        return CreateCertificate(new(verified.Member, device, ProjectRole.Contributor, signing, receiving), grant, delegation);
    }

    public AuthorizedProjectDevice VerifyCertificate(Guid device, ReadOnlySpan<byte> certificate)
    {
        if (certificate.Length is not (CertificateBodySize + 64) and not MaxCertificateBytes)
            throw new CryptographicException("Invalid project certificate size.");
        var role = (ProjectRole)certificate[1];
        CheckContext(certificate, role);
        if (device == Guid.Empty || new Guid(certificate.Slice(38, 16), bigEndian: true) != device)
            throw new CryptographicException("The certificate belongs to another device.");
        var member = new Guid(certificate.Slice(18, 16), bigEndian: true);
        var signer = CertificateSigner(certificate, role, member);
        Verify("novalist/v1/project-device"u8, certificate, signer);
        var signing = PublicKey.Import(Signing, certificate.Slice(54, 32), KeyBlobFormat.RawPublicKey);
        if (role == ProjectRole.Owner && !signing.Equals(owner)) throw new CryptographicException("Invalid owner device key.");
        var receiving = PublicKey.Import(KeyAgreementAlgorithm.X25519, certificate.Slice(86, 32), KeyBlobFormat.RawPublicKey);
        if (device != DeviceIdentity.IdForKeys(signing, receiving))
            throw new CryptographicException("The certified device identity does not match its public keys.");
        return new(member, device, role, signing, receiving);
    }

    private PublicKey CertificateSigner(ReadOnlySpan<byte> certificate, ProjectRole role, Guid member)
    {
        if (role == ProjectRole.Owner && certificate.Length == CertificateBodySize + 64) return owner;
        if (role != ProjectRole.Contributor || certificate.Length != MaxCertificateBytes)
            throw new CryptographicException("Invalid project certificate role.");
        var grant = VerifyGrant(certificate.Slice(CertificateBodySize, GrantSize));
        if (grant.Member != member) throw new CryptographicException("The certificate belongs to another member.");
        if (clock.GetUtcNow().ToUnixTimeSeconds() < grant.NotBefore)
            throw new CryptographicException("The project authorization is not yet valid.");
        return grant.Key;
    }

    private (Guid Member, PublicKey Key, long NotBefore, long ExpiresAt) VerifyGrant(ReadOnlySpan<byte> grant)
    {
        if (grant.Length != GrantSize) throw new CryptographicException("Invalid invitation grant size.");
        CheckContext(grant, ProjectRole.Contributor);
        Verify("novalist/v1/project-grant"u8, grant, owner);
        var notBefore = BinaryPrimitives.ReadInt64BigEndian(grant.Slice(38, 8));
        var expiresAt = BinaryPrimitives.ReadInt64BigEndian(grant.Slice(46, 8));
        if (notBefore >= expiresAt) throw new CryptographicException("Invalid invitation interval.");
        return (new Guid(grant.Slice(18, 16), bigEndian: true),
            PublicKey.Import(Signing, grant.Slice(54, 32), KeyBlobFormat.RawPublicKey), notBefore, expiresAt);
    }

    private byte[] CreateCertificate(Guid member, DeviceIdentity device, ProjectRole role, byte[] grant, Key signer)
        => CreateCertificate(new(member, device.Id, role, device.SigningKey.PublicKey, device.ReceivingKey.PublicKey), grant, signer);

    private byte[] CreateCertificate(AuthorizedProjectDevice device, byte[] grant, Key signer)
    {
        var body = new byte[CertificateBodySize + grant.Length];
        WriteContext(body, device.MemberId, device.Role);
        device.DeviceId.TryWriteBytes(body.AsSpan(38, 16), bigEndian: true, out _);
        device.SigningKey.Export(KeyBlobFormat.RawPublicKey).CopyTo(body, 54);
        device.ReceivingKey.Export(KeyBlobFormat.RawPublicKey).CopyTo(body, 86);
        grant.CopyTo(body, CertificateBodySize);
        return Sign("novalist/v1/project-device"u8, body, signer);
    }

    private void WriteContext(Span<byte> body, Guid member, ProjectRole role)
    {
        body[0] = 1;
        body[1] = (byte)role;
        project.TryWriteBytes(body.Slice(2, 16), bigEndian: true, out _);
        member.TryWriteBytes(body.Slice(18, 16), bigEndian: true, out _);
        BinaryPrimitives.WriteUInt32BigEndian(body.Slice(34, 4), epoch);
    }

    private void CheckContext(ReadOnlySpan<byte> body, ProjectRole role)
    {
        if (body[0] != 1 || body[1] != (byte)role || new Guid(body.Slice(2, 16), bigEndian: true) != project ||
            new Guid(body.Slice(18, 16), bigEndian: true) == Guid.Empty || BinaryPrimitives.ReadUInt32BigEndian(body.Slice(34, 4)) != epoch)
            throw new CryptographicException("The authorization belongs to another project, member or epoch.");
    }

    private static void RequireKey(Key key, PublicKey expected)
    {
        if (!key.PublicKey.Equals(expected)) throw new CryptographicException("The signing key is not authorized.");
    }

    private static byte[] Sign(ReadOnlySpan<byte> domain, byte[] body, Key key) =>
        [.. body, .. Signing.Sign(key, [.. domain, .. body])];

    private static void Verify(ReadOnlySpan<byte> domain, ReadOnlySpan<byte> record, PublicKey key)
    {
        if (!Signing.Verify(key, [.. domain, .. record[..^64]], record[^64..]))
            throw new CryptographicException("Invalid project authorization signature.");
    }
}
