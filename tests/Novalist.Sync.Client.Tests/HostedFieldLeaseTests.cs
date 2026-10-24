using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class HostedFieldLeaseTests
{
    [Fact]
    public async Task HostedAcquisitionRequiresTheCallerToKeepAFixedDeadline()
    {
        await using var transport = new HostedTransport(new("https://relay.example"), Guid.NewGuid(), () => Task.FromResult("token"));
        var request = new FieldLeaseRequest(Guid.NewGuid(), Guid.NewGuid(), 0, Guid.NewGuid());
        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            transport.AcquireFieldLeaseAsync(request, TestContext.Current.CancellationToken));
        Assert.Equal("request", error.ParamName);
    }
}
