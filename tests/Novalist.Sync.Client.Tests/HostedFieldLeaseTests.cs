using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class HostedFieldLeaseTests
{
    [Theory]
    [InlineData(-10)]
    [InlineData(10)]
    public async Task FocusDeadlineUsesServerTimeOnceAndSurvivesRetriesWithASkewedClock(int serverOffsetMinutes)
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new LeaseClockState { Now = DateTimeOffset.UtcNow.AddMinutes(serverOffsetMinutes) };
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSingleton(state);
        builder.Services.AddSignalR().AddMessagePackProtocol();
        await using var app = builder.Build();
        app.MapPut("/devices/{deviceId:guid}", () => Results.NoContent());
        app.MapHub<LeaseClockHub>("/sync");
        await app.StartAsync(ct);
        await using var transport = new HostedTransport(new(app.Urls.Single()), state.Device, () => Task.FromResult("test-token"));
        await transport.ConnectAsync(ct);
        var request = await transport.CreateFieldLeaseRequestAsync(Guid.NewGuid(), Guid.NewGuid(), 0, ct);
        Assert.Equal(state.Now.AddMinutes(1), request.AcquireBefore);
        await transport.AcquireFieldLeaseAsync(request, ct);
        state.Now = state.Now.AddSeconds(5);
        await transport.AcquireFieldLeaseAsync(request, ct);
        Assert.True(await transport.ReleaseFieldLeaseAsync(request, ct));
        Assert.Equal(1, state.TimeRequests);
        Assert.Equal(3, state.Requests.Count);
        Assert.All(state.Requests, received => Assert.Equal(request, received));
    }

    [Fact]
    public async Task HostedAcquisitionRequiresTheCallerToKeepAFixedDeadline()
    {
        await using var transport = new HostedTransport(new("https://relay.example"), Guid.NewGuid(), () => Task.FromResult("token"));
        var request = new FieldLeaseRequest(Guid.NewGuid(), Guid.NewGuid(), 0, Guid.NewGuid());
        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            transport.AcquireFieldLeaseAsync(request, TestContext.Current.CancellationToken));
        Assert.Equal("request", error.ParamName);
    }

    public sealed class LeaseClockState
    {
        public DateTimeOffset Now { get; set; }
        public Guid Device { get; } = Guid.NewGuid();
        public int TimeRequests { get; set; }
        public List<FieldLeaseRequest> Requests { get; } = [];
    }

    public sealed class LeaseClockHub(LeaseClockState state) : Hub
    {
        public DateTimeOffset GetFieldLeaseTime() { state.TimeRequests++; return state.Now; }

        public FieldLeaseGrant AcquireFieldLease(FieldLeaseRequest request)
        {
            if (request.AcquireBefore is not { } deadline || deadline <= state.Now || deadline > state.Now.AddMinutes(2))
                throw new HubException("The acquisition deadline is outside the server's clock window.");
            state.Requests.Add(request);
            return new(state.Device, request.LeaseId, state.Now.AddSeconds(30), state.Now, 0);
        }

        public bool ReleaseFieldLease(FieldLeaseRequest request) { state.Requests.Add(request); return true; }
    }
}
