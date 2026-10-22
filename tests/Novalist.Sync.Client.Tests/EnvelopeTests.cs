using System.Security.Cryptography;
using Novalist.Sync.Client;
using NSec.Cryptography;
using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class EnvelopeTests
{
    [Fact]
    public void PeersDecryptAuthenticatedUpdatesWithoutSendingPlaintext()
    {
        var project = Guid.NewGuid();
        var device = Guid.NewGuid();
        using var signing = Key.Create(SignatureAlgorithm.Ed25519);
        using var cipher = new EnvelopeCipher(project, 0, RandomNumberGenerator.GetBytes(32));
        var header = new UpdateHeader(project, Guid.NewGuid(), 0, device, 1);
        var message = cipher.Seal(header, "A private scene."u8, signing);

        Assert.Equal("A private scene."u8.ToArray(), cipher.Open(message, signing.PublicKey));
        Assert.False(message.Ciphertext.AsSpan().IndexOf("A private scene."u8) >= 0);
        Assert.Equal(header, UpdateHeader.Parse(message.Header));
        Assert.Equal(24, message.Nonce.Length);
        Assert.Equal(64, message.Signature.Length);
    }

    [Theory]
    [InlineData("header")]
    [InlineData("nonce")]
    [InlineData("ciphertext")]
    [InlineData("signature")]
    public void AlteredEnvelopesAreRejected(string field)
    {
        var project = Guid.NewGuid();
        using var signing = Key.Create(SignatureAlgorithm.Ed25519);
        using var cipher = new EnvelopeCipher(project, 0, RandomNumberGenerator.GetBytes(32));
        var message = cipher.Seal(new(project, Guid.NewGuid(), 0, Guid.NewGuid(), 1), "Private"u8, signing);
        var bytes = field switch
        {
            "header" => message.Header,
            "nonce" => message.Nonce,
            "ciphertext" => message.Ciphertext,
            "signature" => message.Signature,
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        bytes[^1] ^= 2;
        Assert.Throws<CryptographicException>(() => cipher.Open(message, signing.PublicKey));
    }

    [Fact]
    public void OtherProjectsEpochsAndUntrustedSignersAreRejected()
    {
        var project = Guid.NewGuid();
        var epochKey = RandomNumberGenerator.GetBytes(32);
        using var signing = Key.Create(SignatureAlgorithm.Ed25519);
        using var stranger = Key.Create(SignatureAlgorithm.Ed25519);
        using var cipher = new EnvelopeCipher(project, 0, epochKey);
        using var otherProject = new EnvelopeCipher(Guid.NewGuid(), 0, epochKey);
        using var otherEpoch = new EnvelopeCipher(project, 1, epochKey);
        var message = cipher.Seal(new(project, Guid.NewGuid(), 0, Guid.NewGuid(), 1), "Private"u8, signing);
        Assert.Throws<CryptographicException>(() => cipher.Open(message, stranger.PublicKey));
        Assert.Throws<CryptographicException>(() => otherProject.Open(message, signing.PublicKey));
        Assert.Throws<CryptographicException>(() => otherEpoch.Open(message, signing.PublicKey));
    }
}
