using NSec.Cryptography;
using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class RecoveryDeviceIdentityTests
{
    [Fact]
    public void RecoveryDerivationMatchesIndependentHkdfAndCurveVectors()
    {
        var recoveryKey = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        using var first = RecoveryDeviceIdentity.Derive(recoveryKey);
        using var restored = RecoveryDeviceIdentity.Derive(recoveryKey);
        Assert.Equal("bbc5a4cf0084597c752db332cf9404bff7696225c59242be59e931cb96889377",
            Convert.ToHexStringLower(first.SigningKey.PublicKey.Export(KeyBlobFormat.RawPublicKey)));
        Assert.Equal("845c8e48768c75c7df38bb4a26e69ad1c6f1abfa00950aa2bfb66d55f83def26",
            Convert.ToHexStringLower(first.ReceivingKey.PublicKey.Export(KeyBlobFormat.RawPublicKey)));
        Assert.Equal(first.Id, restored.Id);
        var proof = SignatureAlgorithm.Ed25519.Sign(restored.SigningKey, "Recovered identity"u8);
        Assert.True(SignatureAlgorithm.Ed25519.Verify(first.SigningKey.PublicKey, "Recovered identity"u8, proof));
        recoveryKey[0] ^= 1;
        using var other = RecoveryDeviceIdentity.Derive(recoveryKey);
        Assert.NotEqual(first.Id, other.Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public void RecoveryRejectsKeysWithTheWrongSize(int length) =>
        Assert.Throws<ArgumentException>(() => RecoveryDeviceIdentity.Derive(new byte[length]));

    [Fact]
    public void SigningThumbprintMatchesRfc8037AppendixA3()
    {
        var key = Convert.FromHexString("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a");
        Assert.Equal("kPrK_qmxVWaYVA9wwBF6Iuo3vVzz7TxHCTwXBygrS4k", HostedDeviceEnrollment.SigningKeyThumbprint(key));
    }
}
