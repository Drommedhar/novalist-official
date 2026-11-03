using Microsoft.AspNetCore.SignalR;
using Novalist.Sync.Client;
using NSubstitute;
using Xunit;

namespace Novalist.Sync.IntegrationTests;

public sealed class HostedFieldLeaseDeadlineTests(CommunityServerFixture server) : IClassFixture<CommunityServerFixture>
{
    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public async Task SuppliedAcquisitionDeadlinesAreNeverReplacedByTheTransport(int minutesFromNow)
    {
        var ct = TestContext.Current.CancellationToken;
        var subject = Guid.NewGuid().ToString();
        await using var transport = new HostedTransport(server.Address, Guid.NewGuid(),
            () => Task.FromResult(server.Token(subject)));
        await transport.ConnectAsync(ct);
        var project = Guid.NewGuid();
        await transport.CreateProjectAsync(project, ct);
        var request = new FieldLeaseRequest(project, Guid.NewGuid(), 0, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(minutesFromNow));
        await Assert.ThrowsAsync<HubException>(() => transport.AcquireFieldLeaseAsync(request, ct));
    }

    [Fact]
    public async Task RetryingAndReleasingAFocusKeepsItsOriginalAcquisitionDeadline()
    {
        var ct = TestContext.Current.CancellationToken;
        var subject = Guid.NewGuid().ToString();
        var credentials = Substitute.For<ICollaborationCredentials>();
        credentials.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));
        using var identity = await new DeviceIdentityStore(credentials).GetOrCreateAsync(new("https://identity.example"), subject, ct);
        await server.EnrollAsync(identity, subject, ct);
        await using var transport = new HostedTransport(server.Address, identity, () => Task.FromResult(server.Token(subject)));
        await transport.ConnectAsync(ct);
        var project = Guid.NewGuid();
        await transport.CreateProjectAsync(project, ct);
        var request = await transport.CreateFieldLeaseRequestAsync(project, Guid.NewGuid(), 0, ct);
        var first = await transport.AcquireFieldLeaseAsync(request, ct);
        var retry = await transport.AcquireFieldLeaseAsync(request, ct);
        Assert.Equal(first.LeaseId, retry.LeaseId);
        Assert.Equal(first.ExpiresAt, retry.ExpiresAt);
        Assert.True(await transport.ReleaseFieldLeaseAsync(request, ct));
        await Assert.ThrowsAsync<HubException>(() => transport.AcquireFieldLeaseAsync(request, ct));
    }
}
