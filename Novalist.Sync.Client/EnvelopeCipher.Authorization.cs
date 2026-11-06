using System.Buffers.Binary;
using System.Security.Cryptography;
using NSec.Cryptography;

namespace Novalist.Sync.Client;

internal sealed record AuthorizedPayload(byte[] Payload, byte[] Certificate);

public sealed partial class EnvelopeCipher
{
    internal const int AuthorizationOverhead = 3 + ProjectMembership.MaxCertificateBytes;

    internal UpdateEnvelope SealAuthorized(UpdateHeader header, ReadOnlySpan<byte> payload, Key signingKey, byte[] certificate)
        => SealAuthorized(header, payload, signingKey, certificate, EnvelopeKind.Update);

    internal UpdateEnvelope SealAuthorizedPresence(UpdateHeader header, ReadOnlySpan<byte> payload, Key signingKey, byte[] certificate)
        => SealAuthorized(header, payload, signingKey, certificate, EnvelopeKind.Presence);

    private UpdateEnvelope SealAuthorized(UpdateHeader header, ReadOnlySpan<byte> payload, Key signingKey, byte[] certificate, EnvelopeKind kind)
    {
        var framed = new byte[3 + certificate.Length + payload.Length];
        framed[0] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(framed.AsSpan(1, 2), checked((ushort)certificate.Length));
        certificate.CopyTo(framed, 3);
        payload.CopyTo(framed.AsSpan(3 + certificate.Length));
        try { return Seal(header, framed, signingKey, kind); }
        finally { CryptographicOperations.ZeroMemory(framed); }
    }

    internal AuthorizedPayload OpenAuthorized(UpdateEnvelope envelope, ProjectMembership membership)
        => OpenAuthorized(envelope, membership, EnvelopeKind.Update);

    internal AuthorizedPayload OpenAuthorizedPresence(UpdateEnvelope envelope, ProjectMembership membership)
        => OpenAuthorized(envelope, membership, EnvelopeKind.Presence);

    private AuthorizedPayload OpenAuthorized(UpdateEnvelope envelope, ProjectMembership membership, EnvelopeKind kind)
    {
        var header = UpdateHeader.Parse(envelope.Header, kind);
        CheckContext(header);
        ValidateSize(envelope, kind);
        var framed = Aead.Decrypt(kind == EnvelopeKind.Update ? updateKey : presenceKey, envelope.Nonce, envelope.Header, envelope.Ciphertext)
            ?? throw new CryptographicException("The collaboration update could not be decrypted.");
        try
        {
            if (framed.Length < 3 || framed[0] != 1) throw new CryptographicException("Missing project authorization.");
            var size = BinaryPrimitives.ReadUInt16BigEndian(framed.AsSpan(1, 2));
            if (size > ProjectMembership.MaxCertificateBytes || framed.Length <= 3 + size)
                throw new CryptographicException("Invalid project authorization size.");
            var certificate = framed.AsSpan(3, size);
            var device = membership.VerifyCertificate(header.SenderDeviceId, certificate);
            VerifySignature(envelope, device.SigningKey);
            return new(framed[(3 + size)..], certificate.ToArray());
        }
        finally { CryptographicOperations.ZeroMemory(framed); }
    }
}
