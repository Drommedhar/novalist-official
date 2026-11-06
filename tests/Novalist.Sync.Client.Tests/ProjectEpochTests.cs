using System.Security.Cryptography;
using NSubstitute;
using NSec.Cryptography;
using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class ProjectEpochTests
{
    [Fact]
    public async Task RotatedCertificatesPinExactDevicesAndOldDelegationCannotMintCurrentWriters()
    {
        var ct = TestContext.Current.CancellationToken;
        var credentials = Substitute.For<ICollaborationCredentials>();
        credentials.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));
        var identities = new DeviceIdentityStore(credentials);
        using var owner = await identities.GetOrCreateAsync(new("https://identity.example"), "owner", ct);
        using var writer = await identities.GetOrCreateAsync(new("https://identity.example"), "writer", ct);
        using var delegation = Key.Create(SignatureAlgorithm.Ed25519);
        var project = Guid.NewGuid();
        var previous = new ProjectMembership(project, 0, owner.SigningKey.PublicKey);
        var grant = previous.CreateInvitation(new(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1)), delegation.PublicKey, owner.SigningKey);
        var old = previous.ClaimInvitation(grant, delegation, writer);
        var current = new ProjectMembership(project, 1, owner.SigningKey.PublicKey);
        var rotated = current.RenewCertificate(previous.VerifyCertificate(writer.Id, old), owner.SigningKey);
        Assert.Equal(writer.ReceivingKey.PublicKey, current.VerifyCertificate(writer.Id, rotated).ReceivingKey);
        Assert.Throws<CryptographicException>(() => current.VerifyCertificate(writer.Id, old));
        Assert.Throws<CryptographicException>(() => current.RenewCertificate(previous.VerifyCertificate(writer.Id, old), writer.SigningKey));
        rotated[^1] ^= 1;
        Assert.Throws<CryptographicException>(() => current.VerifyCertificate(writer.Id, rotated));
    }
}
