using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Novalist.Sync.Client;
using Xunit;

namespace Novalist.Sync.IntegrationTests;

public sealed class VerifiedTransportTests(CommunityServerFixture server) : IClassFixture<CommunityServerFixture>
{
    [Fact]
    public async Task FocusLocksFenceEncryptedEditsAcrossTwoVerifiedConnections()
    {
        var ct = TestContext.Current.CancellationToken;
        var accounts = new DeviceIdentityStore(new MemoryCredentials());
        var authority = new Uri("https://identity.example");
        var ownerSubject = Guid.NewGuid().ToString();
        var guestSubject = Guid.NewGuid().ToString();
        using var ownerDevice = await accounts.GetOrCreateAsync(authority, ownerSubject, ct);
        using var guestDevice = await accounts.GetOrCreateAsync(authority, guestSubject, ct);
        await server.EnrollAsync(ownerDevice, ownerSubject, ct);
        await server.EnrollAsync(guestDevice, guestSubject, ct);
        await using var owner = new HostedTransport(server.Address, ownerDevice, () => Task.FromResult(server.Token(ownerSubject)));
        await using var guest = new HostedTransport(server.Address, guestDevice, () => Task.FromResult(server.Token(guestSubject)));
        await owner.ConnectAsync(ct);
        await guest.ConnectAsync(ct);
        var project = Guid.NewGuid();
        await owner.CreateProjectAsync(project, ct);
        await server.AddContributorAsync(project, guestSubject, ct);
        await guest.JoinAsync(project, ct);
        var request = FieldLeaseRequest.Create(project, Guid.NewGuid(), 0);
        var granted = await owner.AcquireFieldLeaseAsync(request, ct);
        Assert.Equal(ownerDevice.Id, granted.DeviceId);
        Assert.Equal(request.LeaseId, granted.LeaseId);
        Assert.Equal(30, (granted.ExpiresAt - granted.ServerTime).TotalSeconds);
        var otherRequest = request with { LeaseId = Guid.NewGuid() };
        Assert.Equal(ownerDevice.Id, (await guest.AcquireFieldLeaseAsync(otherRequest, ct)).DeviceId);
        using var cipher = new EnvelopeCipher(project, 0, RandomNumberGenerator.GetBytes(32));
        var edit = cipher.Seal(new(project, Guid.NewGuid(), 0, ownerDevice.Id, 1), "Private character age"u8, ownerDevice.SigningKey);
        var received = new TaskCompletionSource<SequencedUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        guest.Received += update => { received.TrySetResult(update); return Task.CompletedTask; };
        Assert.Equal(1, await owner.PushFieldEditAsync(request, edit, ct));
        Assert.Equal("Private character age"u8.ToArray(), cipher.Open((await received.Task.WaitAsync(TimeSpan.FromSeconds(5), ct)).Envelope, ownerDevice.SigningKey.PublicKey));
        Assert.NotNull(await owner.RenewFieldLeaseAsync(request, ct));
        Assert.True(await owner.ReleaseFieldLeaseAsync(request, ct));
        var next = await guest.AcquireFieldLeaseAsync(otherRequest with { LeaseId = Guid.NewGuid() }, ct);
        Assert.Equal(guestDevice.Id, next.DeviceId);
        Assert.Equal(1, next.ServerSeq);
        Assert.Equal(1, await owner.PushFieldEditAsync(request, edit, ct));
        var late = cipher.Seal(new(project, Guid.NewGuid(), 0, ownerDevice.Id, 2), "Late edit"u8, ownerDevice.SigningKey);
        await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(() => owner.PushFieldEditAsync(request, late, ct));
        var field = request with { ResourceId = Guid.NewGuid(), LeaseId = Guid.NewGuid() };
        var graph = field with { ResourceId = Guid.NewGuid(), LeaseId = Guid.NewGuid() };
        await owner.AcquireFieldLeaseAsync(field, ct);
        await owner.AcquireFieldLeaseAsync(graph, ct);
        var guarded = field with { RequiredLease = new(graph.ResourceId, graph.LeaseId) };
        Assert.Equal(2, await owner.PushFieldEditAsync(guarded, late, ct));
        Assert.True(await owner.ReleaseFieldLeaseAsync(graph, ct));
        Assert.Equal(guestDevice.Id, (await guest.AcquireFieldLeaseAsync(graph with { LeaseId = Guid.NewGuid() }, ct)).DeviceId);
        var obsolete = cipher.Seal(new(project, Guid.NewGuid(), 0, ownerDevice.Id, 3), "Obsolete graph edit"u8, ownerDevice.SigningKey);
        await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(() => owner.PushFieldEditAsync(guarded, obsolete, ct));
        Assert.Equal(2, await owner.PushFieldEditAsync(guarded, late, ct));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ASecondServerCannotRelayRegistrationProofToTheSelectedServersPeer(bool rewriteServerUrl)
    {
        var ct = TestContext.Current.CancellationToken;
        var subject = Guid.NewGuid().ToString();
        var token = server.Token(subject);
        using var device = await new DeviceIdentityStore(new MemoryCredentials()).GetOrCreateAsync(new("https://identity.example"), subject, ct);
        await server.EnrollAsync(device, subject, ct);
        using var upstream = new HttpClient { BaseAddress = server.Address };
        upstream.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var relay = builder.Build();
        var registrations = 0;
        relay.MapGet("/devices/registration", async () =>
        {
            var response = await upstream.GetFromJsonAsync<Dictionary<string, object>>("devices/registration", ct)
                ?? throw new InvalidDataException("Missing registration context.");
            if (rewriteServerUrl) response["serverUrl"] = relay.Urls.Single().TrimEnd('/') + '/';
            return Results.Json(response);
        });
        relay.MapPut("/devices/verified", async (HttpContext context) =>
        {
            Interlocked.Increment(ref registrations);
            using var content = new StreamContent(context.Request.Body);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await upstream.PutAsync("devices/verified", content, ct);
            return Results.StatusCode((int)response.StatusCode);
        });
        await relay.StartAsync(ct);
        await using var transport = new HostedTransport(new Uri(relay.Urls.Single()), device, () => Task.FromResult(token));
        if (rewriteServerUrl)
        {
            var error = await Assert.ThrowsAsync<HttpRequestException>(() => transport.ConnectAsync(ct));
            Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
            Assert.Equal(1, registrations);
        }
        else
        {
            await Assert.ThrowsAsync<CryptographicException>(() => transport.ConnectAsync(ct));
            Assert.Equal(0, registrations);
        }
    }

    [Fact]
    public async Task VerifiedDevicesInviteAcrossAccountsAndReconnectWithFreshProofs()
    {
        var ct = TestContext.Current.CancellationToken;
        var accounts = new DeviceIdentityStore(new MemoryCredentials());
        var authority = new Uri("https://identity.example");
        var ownerSubject = Guid.NewGuid().ToString();
        var guestSubject = Guid.NewGuid().ToString();
        using var ownerDevice = await accounts.GetOrCreateAsync(authority, ownerSubject, ct);
        using var guestDevice = await accounts.GetOrCreateAsync(authority, guestSubject, ct);
        await server.EnrollAsync(ownerDevice, ownerSubject, ct);
        await server.EnrollAsync(guestDevice, guestSubject, ct);
        await using var owner = new HostedTransport(server.Address, ownerDevice, () => Task.FromResult(server.Token(ownerSubject)));
        await using var guest = new HostedTransport(server.Address, guestDevice, () => Task.FromResult(server.Token(guestSubject)));
        await owner.ConnectAsync(ct);
        var project = Guid.NewGuid();
        var invitation = Guid.NewGuid();
        await owner.CreateProjectAsync(project, ct);
        await owner.CreateInvitationAsync(project, invitation, DateTimeOffset.UtcNow.AddHours(1), ct);
        await owner.DisconnectAsync(ct);
        await guest.ConnectAsync(ct);
        var fingerprint = RandomNumberGenerator.GetBytes(32);
        var activation = await guest.ActivateInvitationAsync(project, invitation, fingerprint, ct);
        Assert.Equal(project, activation.ProjectId);
        Assert.Equal(guestDevice.Id, activation.DeviceId);
        Assert.Equal(fingerprint, activation.CertificateHash);
        await guest.JoinAsync(project, ct);
        await guest.DisconnectAsync(ct);
        await guest.ConnectAsync(ct);
        var retry = await guest.ActivateInvitationAsync(project, invitation, fingerprint, ct);
        Assert.Equal(activation.CertificateHash, retry.CertificateHash);
        await guest.JoinAsync(project, ct);
        Assert.True(guest.IsConnected);
    }

    private sealed class MemoryCredentials : ICollaborationCredentials
    {
        private readonly Dictionary<string, string> values = [];
        public Task<string?> ReadAsync(string key, CancellationToken ct) => Task.FromResult(values.GetValueOrDefault(key));
        public Task WriteAsync(string key, string value, CancellationToken ct) { values[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken ct) { values.Remove(key); return Task.CompletedTask; }
    }
}
