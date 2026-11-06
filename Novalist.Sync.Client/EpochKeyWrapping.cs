using System.Buffers.Binary;
using System.Security.Cryptography;
using NSec.Cryptography;

namespace Novalist.Sync.Client;

public sealed record KeyWrapContext(Guid ProjectId, uint Epoch, Guid RecipientDeviceId, Guid GrantorDeviceId);
public sealed record WrappedEpochKey(byte[] EphemeralPublicKey, byte[] Nonce, byte[] Ciphertext, byte[] Signature);

public static class EpochKeyWrapping
{
    private static readonly KeyAgreementAlgorithm Agreement = KeyAgreementAlgorithm.X25519;
    private static readonly AeadAlgorithm Aead = AeadAlgorithm.XChaCha20Poly1305;
    private static readonly SignatureAlgorithm Signing = SignatureAlgorithm.Ed25519;

    public static WrappedEpochKey Wrap(KeyWrapContext context, ReadOnlySpan<byte> epochKey, PublicKey recipient, Key grantor)
    {
        if (epochKey.Length != 32) throw new ArgumentException("An epoch key must contain 32 bytes.", nameof(epochKey));
        var associatedData = EncodeContext(context);
        using var ephemeral = Key.Create(Agreement);
        var publicKey = ephemeral.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        using var wrappingKey = Derive(ephemeral, recipient, publicKey, recipient.Export(KeyBlobFormat.RawPublicKey), associatedData);
        var nonce = RandomNumberGenerator.GetBytes(Aead.NonceSize);
        var ciphertext = Aead.Encrypt(wrappingKey, nonce, associatedData, epochKey);
        return new(publicKey, nonce, ciphertext, Signing.Sign(grantor, SignatureInput(associatedData, publicKey, nonce, ciphertext)));
    }

    public static byte[] Unwrap(KeyWrapContext context, WrappedEpochKey wrapped, Key recipient, PublicKey trustedGrantor)
    {
        var associatedData = EncodeContext(context);
        if (wrapped.EphemeralPublicKey is not { Length: 32 } || wrapped.Nonce is not { Length: 24 } ||
            wrapped.Ciphertext is not { Length: 48 } || wrapped.Signature is not { Length: 64 })
            throw new CryptographicException("Invalid wrapped project key size.");
        if (!Signing.Verify(trustedGrantor, SignatureInput(associatedData, wrapped.EphemeralPublicKey, wrapped.Nonce, wrapped.Ciphertext), wrapped.Signature))
            throw new CryptographicException("The project key grant is not trusted.");
        var ephemeral = PublicKey.Import(Agreement, wrapped.EphemeralPublicKey, KeyBlobFormat.RawPublicKey);
        using var wrappingKey = Derive(recipient, ephemeral, wrapped.EphemeralPublicKey, recipient.PublicKey.Export(KeyBlobFormat.RawPublicKey), associatedData);
        return Aead.Decrypt(wrappingKey, wrapped.Nonce, associatedData, wrapped.Ciphertext)
            ?? throw new CryptographicException("The project key could not be decrypted.");
    }

    private static Key Derive(Key privateKey, PublicKey publicKey, byte[] ephemeral, byte[] recipient, byte[] context)
    {
        using var shared = Agreement.Agree(privateKey, publicKey)
            ?? throw new CryptographicException("The receiving device key is invalid.");
        byte[] salt = [.. ephemeral, .. recipient];
        byte[] info = [.. "novalist/v1/wrap"u8, .. context.AsSpan(0, 20)];
        return KeyDerivationAlgorithm.HkdfSha256.DeriveKey(shared, salt, info, Aead);
    }

    private static byte[] EncodeContext(KeyWrapContext context)
    {
        if (context.ProjectId == Guid.Empty || context.RecipientDeviceId == Guid.Empty || context.GrantorDeviceId == Guid.Empty)
            throw new ArgumentException("Project, recipient and grantor identities are required.", nameof(context));
        var encoded = new byte[52];
        context.ProjectId.TryWriteBytes(encoded, bigEndian: true, out _);
        BinaryPrimitives.WriteUInt32BigEndian(encoded.AsSpan(16), context.Epoch);
        context.RecipientDeviceId.TryWriteBytes(encoded.AsSpan(20), bigEndian: true, out _);
        context.GrantorDeviceId.TryWriteBytes(encoded.AsSpan(36), bigEndian: true, out _);
        return encoded;
    }

    private static byte[] SignatureInput(byte[] context, byte[] ephemeral, byte[] nonce, byte[] ciphertext) =>
        [.. "novalist/v1/wrap-sig"u8, .. context, .. ephemeral, .. nonce, .. ciphertext];
}
