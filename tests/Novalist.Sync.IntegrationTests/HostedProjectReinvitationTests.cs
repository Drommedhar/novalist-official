using Novalist.Sync.Client;
using Xunit;

namespace Novalist.Sync.IntegrationTests;

public sealed class HostedProjectReinvitationTests(CommunityServerFixture server) : IClassFixture<CommunityServerFixture>
{
    [Fact]
    public async Task RemovedDeviceAcceptsFreshInvitationWithoutLosingOldCredentialsOrQueuedWriting()
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(Path.GetTempPath(), "novalist-reinvitation", Guid.NewGuid().ToString("N"));
        var credentials = new Credentials();
        var store = new HostedProjectAccessStore(credentials);
        var identities = new DeviceIdentityStore(credentials);
        var ownerSubject = Guid.NewGuid().ToString();
        var guestSubject = Guid.NewGuid().ToString();
        using var owner = await identities.GetOrCreateAsync(new("https://identity.example"), ownerSubject, ct);
        using var guest = await identities.GetOrCreateAsync(new("https://identity.example"), guestSubject, ct);
        try
        {
            using var access = await store.CreateOwnerAsync(server.Address, owner, ct);
            await using var session = await HostedProjectSession.OpenAsync(folder, Guid.NewGuid(), access, owner, () => Task.FromResult(server.Token(ownerSubject)));
            await session.StartAsync(true, ct);
            await using var source = new MemoryStream("Project before removal"u8.ToArray());
            var checkpoint = await CheckpointTransfer.PublishAsync(session.Updates, source, ct);
            var firstLink = await HostedProjectInvitations.CreateAsync(access, owner, session.Transport, new(checkpoint, DateTimeOffset.UtcNow.AddHours(1)), ct);
            using var first = await store.PrepareJoinAsync(firstLink, guest, ct);
            await using var previousSession = await HostedProjectSession.OpenAsync(folder, Guid.NewGuid(), first, guest, () => Task.FromResult(server.Token(guestSubject)));
            await previousSession.ConnectAsync(ct);
            await store.ActivateAsync(first, previousSession.Transport, ct);
            await previousSession.StartAsync(false, ct);
            var member = (await session.Transport.ListProjectDevicesAsync(access.ProjectId, ct)).Single(value => value.DeviceId == guest.Id).UserId;
            await using var fresh = new MemoryStream("Current project"u8.ToArray());
            await new HostedProjectRotation(folder, owner, session.Transport).RotateAsync(access, member, session.Updates.LastServerSequence, fresh, ct);
            var item = Guid.NewGuid();
            previousSession.Updates.Save(item, "Private writing after removal"u8.ToArray());
            await previousSession.EndAsync(ct);
            using var ownerAccess = await store.RefreshEpochAsync(access, owner, session.Transport, ct);
            Assert.NotNull(ownerAccess);
            await session.EndAsync(ct);
            await using var resumedOwner = await HostedProjectSession.OpenAsync(folder, Guid.NewGuid(), ownerAccess, owner, () => Task.FromResult(server.Token(ownerSubject)));
            await resumedOwner.StartAsync(false, ct);
            var link = await HostedProjectInvitations.CreateAsync(ownerAccess, owner, resumedOwner.Transport,
                new(ownerAccess.Checkpoint!, DateTimeOffset.UtcNow.AddHours(1)), ct);
            using var replacement = await store.PrepareJoinAsync(link, guest, ct);
            using (var unchanged = await store.LoadAsync(server.Address, access.ProjectId, guest, ct)) Assert.Equal(0U, unchanged!.Epoch);
            await using var transport = new HostedTransport(server.Address, guest, () => Task.FromResult(server.Token(guestSubject)));
            await transport.ConnectAsync(ct);
            credentials.FailNextProjectWrite = true;
            await Assert.ThrowsAsync<IOException>(() => store.ActivateAsync(replacement, transport, ct));
            using (var unchanged = await store.LoadAsync(server.Address, access.ProjectId, guest, ct)) Assert.Equal(0U, unchanged!.Epoch);
            var restarted = new HostedProjectAccessStore(credentials);
            using var restored = await restarted.PrepareJoinAsync(link, guest, ct);
            Assert.Equal(replacement.Certificate, restored.Certificate);
            await restarted.ActivateAsync(restored, transport, ct);
            await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.PrepareJoinAsync(firstLink, guest, ct));
            Assert.Contains(credentials.Keys, key => key.StartsWith("project-history:0:", StringComparison.Ordinal));
            Assert.Equal("Private writing after removal"u8.ToArray(), Assert.Single(previousSession.Updates.ReadItem(item)));
            await using var joined = await HostedProjectSession.OpenAsync(folder, Guid.NewGuid(), restored, guest, () => Task.FromResult(server.Token(guestSubject)));
            await joined.StartAsync(false, ct);
            await using var downloaded = new MemoryStream();
            await CheckpointTransfer.DownloadAsync(joined.Updates, restored.Checkpoint!, downloaded, 10000, ct);
            Assert.Equal("Current project"u8.ToArray(), downloaded.ToArray());
            Assert.Empty(joined.Updates.ReadItem(item));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    private sealed class Credentials : ICollaborationCredentials
    {
        private readonly Dictionary<string, string> values = [];
        internal bool FailNextProjectWrite { get; set; }
        internal IEnumerable<string> Keys => values.Keys;
        public Task<string?> ReadAsync(string key, CancellationToken ct) => Task.FromResult(values.GetValueOrDefault(key));
        public Task WriteAsync(string key, string value, CancellationToken ct)
        {
            if (FailNextProjectWrite && key.StartsWith("project:", StringComparison.Ordinal))
            { FailNextProjectWrite = false; throw new IOException("The credential store is temporarily unavailable."); }
            values[key] = value;
            return Task.CompletedTask;
        }
        public Task RemoveAsync(string key, CancellationToken ct) { values.Remove(key); return Task.CompletedTask; }
    }
}
