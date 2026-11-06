using System.Net;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Novalist.Sync.Client;
using Novalist.Sync.Server.Data;
using Novalist.Sync.Server.Hubs;
using NSubstitute;
using Xunit;

namespace Novalist.Sync.IntegrationTests;

public sealed class HostedDeviceRecoveryTests(CommunityServerFixture server) : IClassFixture<CommunityServerFixture>
{
    [Fact]
    public async Task RecoveryKeyLinksAReplacementAfterTheOnlyOrdinaryDeviceIsRevoked()
    {
        var ct = TestContext.Current.CancellationToken;
        var subject = Guid.NewGuid().ToString();
        using var original = await NewIdentityAsync(ct);
        var recoveryKey = RandomNumberGenerator.GetBytes(32);
        try
        {
            Guid recoveryId;
            using (var recovery = RecoveryDeviceIdentity.Derive(recoveryKey))
            {
                recoveryId = recovery.Id;
                await HostedDeviceEnrollment.EnrollAsync(server.Address, original, recovery, server.Token(subject, original), ct);
            }
            await using (var ordinary = new HostedTransport(server.Address, original, () => Task.FromResult(server.Token(subject))))
                await ordinary.ConnectAsync(ct);
            await using (var scope = server.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
                var user = await db.Devices.Where(device => device.Id == original.Id).Select(device => device.UserId).SingleAsync(ct);
                Assert.Equal(original.Id, (await db.Devices.SingleAsync(device => device.Id == recoveryId, ct)).LinkedByDeviceId);
                await scope.ServiceProvider.GetRequiredService<AccessRevocation>().RevokeDeviceAsync(user, original.Id, ct);
            }
            original.Dispose();
            using var restored = RecoveryDeviceIdentity.Derive(recoveryKey);
            using var replacement = await NewIdentityAsync(ct);
            await using (var recoveryTransport = new HostedTransport(server.Address, restored, () => Task.FromResult(server.Token(subject))))
                await recoveryTransport.ConnectAsync(ct);
            await HostedDeviceEnrollment.LinkAsync(server.Address, restored, replacement, server.Token(subject), ct);
            await using var resumed = new HostedTransport(server.Address, replacement, () => Task.FromResult(server.Token(subject)));
            await resumed.ConnectAsync(ct);
            await resumed.CreateProjectAsync(Guid.NewGuid(), ct);
            Assert.True(resumed.IsConnected);
        }
        finally { CryptographicOperations.ZeroMemory(recoveryKey); }
    }

    [Fact]
    public async Task BorrowedEnrollmentTokenCannotInstallAnotherRootIdentity()
    {
        var ct = TestContext.Current.CancellationToken;
        var subject = Guid.NewGuid().ToString();
        using var intended = await NewIdentityAsync(ct);
        using var attacker = await NewIdentityAsync(ct);
        using var recovery = RecoveryDeviceIdentity.Derive(RandomNumberGenerator.GetBytes(32));
        var borrowed = server.Token(subject, intended);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            HostedDeviceEnrollment.EnrollAsync(server.Address, attacker, recovery, borrowed, ct));
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        await HostedDeviceEnrollment.EnrollAsync(server.Address, intended, recovery, borrowed, ct);
        await using var scope = server.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
        Assert.False(await db.Devices.AnyAsync(device => device.Id == attacker.Id, ct));
    }

    [Fact]
    public async Task AnExistingDeviceCanLinkAnAdditionalRecoveryIdentity()
    {
        var ct = TestContext.Current.CancellationToken;
        var subject = Guid.NewGuid().ToString();
        using var original = await NewIdentityAsync(ct);
        await server.EnrollAsync(original, subject, ct);
        using var recovery = RecoveryDeviceIdentity.Derive(RandomNumberGenerator.GetBytes(32));
        await HostedDeviceEnrollment.LinkAsync(server.Address, original, recovery, server.Token(subject), ct);
        await using var transport = new HostedTransport(server.Address, recovery, () => Task.FromResult(server.Token(subject)));
        await transport.ConnectAsync(ct);
        Assert.True(transport.IsConnected);
    }

    private static Task<DeviceIdentity> NewIdentityAsync(CancellationToken ct)
    {
        var credentials = Substitute.For<ICollaborationCredentials>();
        credentials.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));
        return new DeviceIdentityStore(credentials).GetOrCreateAsync(new("https://identity.example"), Guid.NewGuid().ToString(), ct);
    }
}
