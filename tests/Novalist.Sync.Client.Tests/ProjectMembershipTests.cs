using System.Security.Cryptography;
using NSubstitute;
using NSec.Cryptography;
using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class ProjectMembershipTests
{
    [Fact]
    public async Task OwnerAuthorizesAnOfflineJoinWithoutGivingTheWriterOwnerAuthority()
    {
        var ct = TestContext.Current.CancellationToken;
        var credentials = Substitute.For<ICollaborationCredentials>();
        credentials.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));
        var identities = new DeviceIdentityStore(credentials);
        using var owner = await identities.GetOrCreateAsync(new("https://identity.example"), "owner", ct);
        using var writer = await identities.GetOrCreateAsync(new("https://identity.example"), "writer", ct);
        using var delegation = Key.Create(SignatureAlgorithm.Ed25519);
        var project = Guid.NewGuid();
        var member = Guid.NewGuid();
        var membership = new ProjectMembership(project, 0, owner.SigningKey.PublicKey);
        var ownerCertificate = membership.CreateOwnerCertificate(owner);
        var grant = membership.CreateInvitation(new(member, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1)),
            delegation.PublicKey, owner.SigningKey);
        var writerCertificate = membership.ClaimInvitation(grant, delegation, writer);
        var verified = membership.VerifyCertificate(writer.Id, writerCertificate);
        Assert.Equal(member, verified.MemberId);
        Assert.Equal(ProjectRole.Contributor, verified.Role);
        Assert.Equal(writer.SigningKey.PublicKey, verified.SigningKey);
        Assert.Equal(writer.ReceivingKey.PublicKey, verified.ReceivingKey);
        Assert.Equal(ProjectRole.Owner, membership.VerifyCertificate(owner.Id, ownerCertificate).Role);
        Assert.Throws<CryptographicException>(() => membership.CreateOwnerCertificate(writer));
        Assert.Throws<CryptographicException>(() => membership.CreateInvitation(
            new(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1)), delegation.PublicKey, writer.SigningKey));
        Assert.Throws<CryptographicException>(() => membership.VerifyCertificate(owner.Id, writerCertificate));
        var substitutedDevice = writerCertificate.ToArray();
        owner.Id.TryWriteBytes(substitutedDevice.AsSpan(38, 16), bigEndian: true, out _);
        SignatureAlgorithm.Ed25519.Sign(delegation, [.. "novalist/v1/project-device"u8, .. substitutedDevice[..^64]])
            .CopyTo(substitutedDevice, substitutedDevice.Length - 64);
        Assert.Throws<CryptographicException>(() => membership.VerifyCertificate(owner.Id, substitutedDevice));
        Assert.Throws<CryptographicException>(() => new ProjectMembership(Guid.NewGuid(), 0, owner.SigningKey.PublicKey)
            .VerifyCertificate(writer.Id, writerCertificate));
        Assert.Throws<CryptographicException>(() => new ProjectMembership(project, 1, owner.SigningKey.PublicKey)
            .VerifyCertificate(writer.Id, writerCertificate));
        Assert.Throws<CryptographicException>(() => new ProjectMembership(project, 0, writer.SigningKey.PublicKey)
            .VerifyCertificate(writer.Id, writerCertificate));
        writerCertificate[^1] ^= 1;
        Assert.Throws<CryptographicException>(() => membership.VerifyCertificate(writer.Id, writerCertificate));
    }

    [Fact]
    public async Task DelegationCannotAuthorizeEditsBeforeItsStartButExistingCredentialsOutliveInvitationExpiry()
    {
        var ct = TestContext.Current.CancellationToken;
        var credentials = Substitute.For<ICollaborationCredentials>();
        credentials.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));
        var devices = new DeviceIdentityStore(credentials);
        using var owner = await devices.GetOrCreateAsync(new("https://identity.example"), "owner", ct);
        using var writer = await devices.GetOrCreateAsync(new("https://identity.example"), "writer", ct);
        using var delegation = Key.Create(SignatureAlgorithm.Ed25519);
        var project = Guid.NewGuid();
        var start = DateTimeOffset.UtcNow.AddHours(1);
        var before = new ProjectMembership(project, 0, owner.SigningKey.PublicKey, new FixedClock(start.AddMinutes(-1)));
        var during = new ProjectMembership(project, 0, owner.SigningKey.PublicKey, new FixedClock(start));
        var after = new ProjectMembership(project, 0, owner.SigningKey.PublicKey, new FixedClock(start.AddHours(2)));
        var grant = before.CreateInvitation(new(Guid.NewGuid(), start, start.AddHours(1)), delegation.PublicKey, owner.SigningKey);
        var certificate = during.ClaimInvitation(grant, delegation, writer);
        Assert.Throws<CryptographicException>(() => before.VerifyCertificate(writer.Id, certificate));
        Assert.Equal(ProjectRole.Contributor, after.VerifyCertificate(writer.Id, certificate).Role);
        Assert.Throws<InvalidOperationException>(() => after.ClaimInvitation(grant, delegation, writer));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
