using System.Security.Cryptography;
using NSec.Cryptography;
using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class EpochKeyWrappingTests
{
    [Fact]
    public void GrantsSignThePublishedBigEndianContextAndRejectEveryAlteredField()
    {
        using var grantor = Key.Create(SignatureAlgorithm.Ed25519);
        using var recipient = Key.Create(KeyAgreementAlgorithm.X25519);
        var context = new KeyWrapContext(
            Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"), 0x01020304,
            Guid.Parse("10213243-5465-7687-98a9-bacbdcedfe0f"), Guid.Parse("ffeeddcc-bbaa-9988-7766-554433221100"));
        var wrapped = EpochKeyWrapping.Wrap(context, RandomNumberGenerator.GetBytes(32), recipient.PublicKey, grantor);
        var encodedContext = Convert.FromHexString(
            "00112233445566778899AABBCCDDEEFF01020304102132435465768798A9BACBDCEDFE0FFFEEDDCCBBAA99887766554433221100");
        byte[] signed = [.. "novalist/v1/wrap-sig"u8, .. encodedContext, .. wrapped.EphemeralPublicKey, .. wrapped.Nonce, .. wrapped.Ciphertext];
        Assert.True(SignatureAlgorithm.Ed25519.Verify(grantor.PublicKey, signed, wrapped.Signature));

        foreach (var field in new[] { wrapped.EphemeralPublicKey, wrapped.Nonce, wrapped.Ciphertext, wrapped.Signature })
        {
            field[0] ^= 1;
            Assert.Throws<CryptographicException>(() => EpochKeyWrapping.Unwrap(context, wrapped, recipient, grantor.PublicKey));
            field[0] ^= 1;
        }
        Assert.Throws<CryptographicException>(() => EpochKeyWrapping.Unwrap(context, wrapped with { Nonce = [] }, recipient, grantor.PublicKey));
    }

    [Fact]
    public void OnlyTheIntendedDeviceCanReadAKeyFromATrustedGrantor()
    {
        using var grantor = Key.Create(SignatureAlgorithm.Ed25519);
        using var recipient = Key.Create(KeyAgreementAlgorithm.X25519);
        using var stranger = Key.Create(KeyAgreementAlgorithm.X25519);
        using var impostor = Key.Create(SignatureAlgorithm.Ed25519);
        var context = new KeyWrapContext(Guid.NewGuid(), 7, Guid.NewGuid(), Guid.NewGuid());
        var epochKey = RandomNumberGenerator.GetBytes(32);
        var wrapped = EpochKeyWrapping.Wrap(context, epochKey, recipient.PublicKey, grantor);

        Assert.Equal(epochKey, EpochKeyWrapping.Unwrap(context, wrapped, recipient, grantor.PublicKey));
        Assert.Throws<CryptographicException>(() => EpochKeyWrapping.Unwrap(context, wrapped, stranger, grantor.PublicKey));
        Assert.Throws<CryptographicException>(() => EpochKeyWrapping.Unwrap(context, wrapped, recipient, impostor.PublicKey));
        Assert.Throws<CryptographicException>(() => EpochKeyWrapping.Unwrap(context with { Epoch = 8 }, wrapped, recipient, grantor.PublicKey));
        Assert.Throws<CryptographicException>(() => EpochKeyWrapping.Unwrap(context with { ProjectId = Guid.NewGuid() }, wrapped, recipient, grantor.PublicKey));
        Assert.Throws<CryptographicException>(() => EpochKeyWrapping.Unwrap(context with { RecipientDeviceId = Guid.NewGuid() }, wrapped, recipient, grantor.PublicKey));
        Assert.Throws<CryptographicException>(() => EpochKeyWrapping.Unwrap(context with { GrantorDeviceId = Guid.NewGuid() }, wrapped, recipient, grantor.PublicKey));
    }
}
