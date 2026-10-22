using NSubstitute;
using NSec.Cryptography;
using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class DeviceIdentityTests
{
    [Fact]
    public async Task DeviceKeysSurviveRestartRemainAccountBoundAndRefuseCorruptStorage()
    {
        var ct = TestContext.Current.CancellationToken;
        var saved = new Dictionary<string, string>();
        var credentials = Substitute.For<ICollaborationCredentials>();
        credentials.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(saved.GetValueOrDefault(call.ArgAt<string>(0))));
        credentials.WriteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => { saved[call.ArgAt<string>(0)] = call.ArgAt<string>(1); return Task.CompletedTask; });
        var issuer = new Uri("https://identity.example");
        using var first = await new DeviceIdentityStore(credentials).GetOrCreateAsync(issuer, "writer", ct);
        using var reopened = await new DeviceIdentityStore(credentials).GetOrCreateAsync(issuer, "writer", ct);
        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.Equal(first.Id, reopened.Id);
        Assert.Equal(first.SigningKey.PublicKey.Export(KeyBlobFormat.RawPublicKey), reopened.SigningKey.PublicKey.Export(KeyBlobFormat.RawPublicKey));
        Assert.Equal(first.ReceivingKey.PublicKey.Export(KeyBlobFormat.RawPublicKey), reopened.ReceivingKey.PublicKey.Export(KeyBlobFormat.RawPublicKey));
        var proof = SignatureAlgorithm.Ed25519.Sign(reopened.SigningKey, "device challenge"u8);
        Assert.True(SignatureAlgorithm.Ed25519.Verify(first.SigningKey.PublicKey, "device challenge"u8, proof));
        using var other = await new DeviceIdentityStore(credentials).GetOrCreateAsync(issuer, "another-writer", ct);
        Assert.NotEqual(first.Id, other.Id);
        foreach (var key in saved.Keys.ToArray()) saved[key] = "{}";
        await Assert.ThrowsAsync<InvalidDataException>(() => new DeviceIdentityStore(credentials).GetOrCreateAsync(issuer, "writer", ct));
    }
}
