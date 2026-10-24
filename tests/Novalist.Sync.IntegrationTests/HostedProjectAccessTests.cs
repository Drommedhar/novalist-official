using System.Security.Cryptography;
using Microsoft.AspNetCore.SignalR;
using Novalist.Sync.Client;
using Xunit;

namespace Novalist.Sync.IntegrationTests;

public sealed class HostedProjectAccessTests(CommunityServerFixture server) : IClassFixture<CommunityServerFixture>
{
    [Fact]
    public async Task OnlyTheOwnerCanRemoveMembersAndOldInvitationsCannotRestoreTheirAccess()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var owner = await ConnectAsync(ct);
        await using var guest = await ConnectAsync(ct);
        var project = Guid.NewGuid();
        await owner.Transport.CreateProjectAsync(project, ct);
        var invitation = Guid.NewGuid();
        await owner.Transport.CreateInvitationAsync(project, invitation, DateTimeOffset.UtcNow.AddHours(1), ct);
        var certificate = RandomNumberGenerator.GetBytes(32);
        await guest.Transport.ActivateInvitationAsync(project, invitation, certificate, ct);
        await guest.Transport.JoinAsync(project, ct);
        var lease = await guest.Transport.CreateFieldLeaseRequestAsync(project, Guid.NewGuid(), 0, ct);
        await guest.Transport.AcquireFieldLeaseAsync(lease, ct);
        var members = await owner.Transport.ListProjectMembersAsync(project, ct);
        Assert.Equal(2, members.Count);
        var removed = Assert.Single(members, member => member.Role == "Contributor");
        await Assert.ThrowsAsync<HubException>(() => guest.Transport.ListProjectMembersAsync(project, ct));
        await Assert.ThrowsAsync<HubException>(() => guest.Transport.RemoveProjectMemberAsync(project, removed.UserId, ct));
        await Assert.ThrowsAsync<HubException>(() => guest.Transport.DeleteProjectAsync(project, ct));
        var spareInvitation = Guid.NewGuid();
        await owner.Transport.CreateInvitationAsync(project, spareInvitation, DateTimeOffset.UtcNow.AddHours(1), ct);
        await owner.Transport.RemoveProjectMemberAsync(project, removed.UserId, ct);
        Assert.Single(await owner.Transport.ListProjectMembersAsync(project, ct));
        await Assert.ThrowsAsync<HubException>(() => guest.Transport.JoinAsync(project, ct));
        await Assert.ThrowsAsync<HubException>(() => guest.Transport.ActivateInvitationAsync(project, invitation, certificate, ct));
        await Assert.ThrowsAsync<HubException>(() => guest.Transport.ActivateInvitationAsync(project, spareInvitation, certificate, ct));
        await Assert.ThrowsAsync<HubException>(() => guest.Transport.AcquireFieldLeaseAsync(lease, ct));
        await Assert.ThrowsAsync<HubException>(() => guest.Transport.RenewFieldLeaseAsync(lease, ct));
        await Assert.ThrowsAsync<HubException>(() => guest.Transport.ReleaseFieldLeaseAsync(lease, ct));
        var envelope = new UpdateEnvelope(new UpdateHeader(project, lease.ResourceId, 0, guest.DeviceId, 1).Encode(),
            RandomNumberGenerator.GetBytes(24), RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(64));
        await Assert.ThrowsAsync<HubException>(() => guest.Transport.PushAsync(envelope, ct));
        await Assert.ThrowsAsync<HubException>(() => guest.Transport.PushFieldEditAsync(lease, envelope, ct));
        await Assert.ThrowsAsync<HubException>(() => guest.Transport.PublishPresenceAsync(envelope with
        { Header = new UpdateHeader(project, lease.ResourceId, 0, guest.DeviceId, 2).Encode(EnvelopeKind.Presence) }, ct));
        await Assert.ThrowsAsync<HubException>(async () => { await foreach (var _ in guest.Transport.CatchUpAsync(project, 0, ct)) { } });
    }

    [Fact]
    public async Task DeletedSharedProjectsDenyEveryMemberAndCannotBeRecreatedUnderTheOldIdentity()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var owner = await ConnectAsync(ct);
        await using var guest = await ConnectAsync(ct);
        var project = Guid.NewGuid();
        await owner.Transport.CreateProjectAsync(project, ct);
        var invitation = Guid.NewGuid();
        await owner.Transport.CreateInvitationAsync(project, invitation, DateTimeOffset.UtcNow.AddHours(1), ct);
        await owner.Transport.DeleteProjectAsync(project, ct);
        await Assert.ThrowsAsync<HubException>(() => owner.Transport.JoinAsync(project, ct));
        await Assert.ThrowsAsync<HubException>(() => owner.Transport.CreateProjectAsync(project, ct));
        await Assert.ThrowsAsync<HubException>(() => guest.Transport.ActivateInvitationAsync(project, invitation, RandomNumberGenerator.GetBytes(32), ct));
    }

    private async Task<Participant> ConnectAsync(CancellationToken ct)
    {
        var subject = Guid.NewGuid().ToString();
        var device = await new DeviceIdentityStore(new Credentials()).GetOrCreateAsync(new("https://identity.example"), subject, ct);
        await server.EnrollAsync(device, subject, ct);
        var transport = new HostedTransport(server.Address, device, () => Task.FromResult(server.Token(subject)));
        try { await transport.ConnectAsync(ct); return new(device, transport); }
        catch { await transport.DisposeAsync(); device.Dispose(); throw; }
    }

    private sealed class Participant(DeviceIdentity device, HostedTransport transport) : IAsyncDisposable
    {
        public Guid DeviceId => device.Id;
        public HostedTransport Transport => transport;
        public async ValueTask DisposeAsync() { await transport.DisposeAsync(); device.Dispose(); }
    }

    private sealed class Credentials : ICollaborationCredentials
    {
        private readonly Dictionary<string, string> values = [];
        public Task<string?> ReadAsync(string key, CancellationToken ct) => Task.FromResult(values.GetValueOrDefault(key));
        public Task WriteAsync(string key, string value, CancellationToken ct) { values[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken ct) { values.Remove(key); return Task.CompletedTask; }
    }
}
