using System.Security.Cryptography;
using NSec.Cryptography;

namespace Novalist.Sync.Client;

public sealed partial class EnvelopeCipher : IDisposable
{
    public const int MaxPayloadBytes = 24 * 1024;
    public const int MaxPresenceBytes = 4096 - 16;
    private static readonly AeadAlgorithm Aead = AeadAlgorithm.XChaCha20Poly1305;
    private static readonly SignatureAlgorithm Signing = SignatureAlgorithm.Ed25519;
    private readonly Guid projectId;
    private readonly uint epoch;
    private readonly Key updateKey;
    private readonly Key presenceKey;

    public EnvelopeCipher(Guid projectId, uint epoch, ReadOnlySpan<byte> epochKey)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("A project identity is required.", nameof(projectId));
        if (epochKey.Length != 32) throw new ArgumentException("An epoch key must contain 32 bytes.", nameof(epochKey));
        this.projectId = projectId;
        this.epoch = epoch;
        Span<byte> salt = stackalloc byte[16];
        projectId.TryWriteBytes(salt, bigEndian: true, out _);
        updateKey = Derive(epochKey, salt, "novalist/v1/update"u8);
        try { presenceKey = Derive(epochKey, salt, "novalist/v1/presence"u8); }
        catch { updateKey.Dispose(); throw; }
    }

    private static Key Derive(ReadOnlySpan<byte> epochKey, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> info)
    {
        Span<byte> derived = stackalloc byte[32];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, epochKey, derived, salt, info);
        try { return Key.Import(Aead, derived, KeyBlobFormat.RawSymmetricKey); }
        finally { CryptographicOperations.ZeroMemory(derived); }
    }

    public UpdateEnvelope Seal(UpdateHeader header, ReadOnlySpan<byte> payload, Key signingKey) =>
        Seal(header, payload, signingKey, EnvelopeKind.Update);

    public UpdateEnvelope SealPresence(UpdateHeader header, ReadOnlySpan<byte> payload, Key signingKey) =>
        Seal(header, payload, signingKey, EnvelopeKind.Presence);

    private UpdateEnvelope Seal(UpdateHeader header, ReadOnlySpan<byte> payload, Key signingKey, EnvelopeKind kind)
    {
        CheckContext(header);
        var limit = kind == EnvelopeKind.Update ? MaxPayloadBytes : MaxPresenceBytes;
        if (payload.IsEmpty || payload.Length > limit) throw new ArgumentOutOfRangeException(nameof(payload));
        var encoded = header.Encode(kind);
        var nonce = RandomNumberGenerator.GetBytes(Aead.NonceSize);
        var ciphertext = Aead.Encrypt(kind == EnvelopeKind.Update ? updateKey : presenceKey, nonce, encoded, payload);
        return new(encoded, nonce, ciphertext, Signing.Sign(signingKey, SignatureInput(encoded, nonce, ciphertext)));
    }

    public byte[] Open(UpdateEnvelope envelope, PublicKey trustedSigner) => Open(envelope, trustedSigner, EnvelopeKind.Update);

    public byte[] OpenPresence(UpdateEnvelope envelope, PublicKey trustedSigner) => Open(envelope, trustedSigner, EnvelopeKind.Presence);

    private byte[] Open(UpdateEnvelope envelope, PublicKey trustedSigner, EnvelopeKind kind)
    {
        var header = UpdateHeader.Parse(envelope.Header, kind);
        CheckContext(header);
        ValidateSize(envelope, kind);
        VerifySignature(envelope, trustedSigner);
        return Aead.Decrypt(kind == EnvelopeKind.Update ? updateKey : presenceKey, envelope.Nonce, envelope.Header, envelope.Ciphertext)
            ?? throw new CryptographicException("The collaboration update could not be decrypted.");
    }

    private static void ValidateSize(UpdateEnvelope envelope, EnvelopeKind kind)
    {
        var limit = kind == EnvelopeKind.Update ? MaxPayloadBytes : MaxPresenceBytes;
        if (envelope.Nonce is not { } nonce || nonce.Length != Aead.NonceSize ||
            envelope.Signature is not { } signature || signature.Length != Signing.SignatureSize ||
            envelope.Ciphertext is not { } ciphertext || ciphertext.Length <= Aead.TagSize || ciphertext.Length > limit + Aead.TagSize)
            throw new CryptographicException("Invalid encrypted update size.");
    }

    private static void VerifySignature(UpdateEnvelope envelope, PublicKey trustedSigner)
    {
        if (!Signing.Verify(trustedSigner, SignatureInput(envelope.Header, envelope.Nonce, envelope.Ciphertext), envelope.Signature))
            throw new CryptographicException("Untrusted collaboration update.");
    }

    private void CheckContext(UpdateHeader header)
    {
        if (header.ProjectId != projectId || header.Epoch != epoch)
            throw new CryptographicException("The update belongs to another project or epoch.");
    }

    private static byte[] SignatureInput(ReadOnlySpan<byte> header, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext)
    {
        ReadOnlySpan<byte> domain = "novalist/v1/msg-sig"u8;
        var bytes = new byte[domain.Length + header.Length + nonce.Length + ciphertext.Length];
        domain.CopyTo(bytes);
        header.CopyTo(bytes.AsSpan(domain.Length));
        nonce.CopyTo(bytes.AsSpan(domain.Length + header.Length));
        ciphertext.CopyTo(bytes.AsSpan(domain.Length + header.Length + nonce.Length));
        return bytes;
    }

    public void Dispose()
    {
        updateKey.Dispose();
        presenceKey.Dispose();
    }
}
