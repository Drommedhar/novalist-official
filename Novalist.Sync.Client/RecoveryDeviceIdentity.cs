using System.Security.Cryptography;
using NSec.Cryptography;

namespace Novalist.Sync.Client;

public static class RecoveryDeviceIdentity
{
    public static DeviceIdentity Derive(ReadOnlySpan<byte> recoveryKey)
    {
        if (recoveryKey.Length != 32) throw new ArgumentException("A recovery key must contain 32 random bytes.", nameof(recoveryKey));
        Span<byte> seed = stackalloc byte[32];
        Span<byte> signingSeed = stackalloc byte[32];
        Span<byte> receivingSeed = stackalloc byte[32];
        try
        {
            HKDF.DeriveKey(HashAlgorithmName.SHA256, recoveryKey, seed, salt: [], info: "novalist/v1/recovery"u8);
            HKDF.DeriveKey(HashAlgorithmName.SHA256, seed, signingSeed, salt: [], info: "novalist/v1/recovery/signing"u8);
            HKDF.DeriveKey(HashAlgorithmName.SHA256, seed, receivingSeed, salt: [], info: "novalist/v1/recovery/receiving"u8);
            var signing = Key.Import(SignatureAlgorithm.Ed25519, signingSeed, KeyBlobFormat.RawPrivateKey);
            try
            {
                var receiving = Key.Import(KeyAgreementAlgorithm.X25519, receivingSeed, KeyBlobFormat.RawPrivateKey);
                try { return new(DeviceIdentity.IdForKeys(signing.PublicKey, receiving.PublicKey), signing, receiving); }
                catch { receiving.Dispose(); throw; }
            }
            catch { signing.Dispose(); throw; }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(seed);
            CryptographicOperations.ZeroMemory(signingSeed);
            CryptographicOperations.ZeroMemory(receivingSeed);
        }
    }
}
