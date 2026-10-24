using Microsoft.AspNetCore.SignalR;
using Novalist.Sync.Client;
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
        await using var transport = new HostedTransport(server.Address, Guid.NewGuid(), () => Task.FromResult(server.Token(subject)));
        await transport.ConnectAsync(ct);
        var project = Guid.NewGuid();
        await transport.CreateProjectAsync(project, ct);
        var request = FieldLeaseRequest.Create(project, Guid.NewGuid(), 0);
        var first = await transport.AcquireFieldLeaseAsync(request, ct);
        var retry = await transport.AcquireFieldLeaseAsync(request, ct);
        Assert.Equal(first.LeaseId, retry.LeaseId);
        Assert.Equal(first.ExpiresAt, retry.ExpiresAt);
        Assert.True(await transport.ReleaseFieldLeaseAsync(request, ct));
        await Assert.ThrowsAsync<HubException>(() => transport.AcquireFieldLeaseAsync(request, ct));
    }
}
