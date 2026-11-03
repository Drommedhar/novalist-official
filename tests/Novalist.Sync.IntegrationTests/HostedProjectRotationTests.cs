using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Novalist.Sync.Client;
using Novalist.Sync.Server.Data;
using Xunit;

namespace Novalist.Sync.IntegrationTests;

public sealed class HostedProjectRotationTests(CommunityServerFixture server) : IClassFixture<CommunityServerFixture>
{
    [Fact]
    public async Task RemovalRotatesCheckpointKeysAndTheRemainingWriterCanResumeWithoutTheOwner()
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(Path.GetTempPath(), "novalist-rotation", Guid.NewGuid().ToString("N"));
        var credentials = new Credentials();
        var store = new HostedProjectAccessStore(credentials);
        var identities = new DeviceIdentityStore(credentials);
        var subjects = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid().ToString()).ToArray();
        using var owner = await identities.GetOrCreateAsync(new("https://identity.example"), subjects[0], ct);
        using var writer = await identities.GetOrCreateAsync(new("https://identity.example"), subjects[1], ct);
        using var removed = await identities.GetOrCreateAsync(new("https://identity.example"), subjects[2], ct);
        await server.EnrollAsync(owner, subjects[0], ct);
        await server.EnrollAsync(writer, subjects[1], ct);
        await server.EnrollAsync(removed, subjects[2], ct);
        try
        {
            using var access = await store.CreateOwnerAsync(server.Address, owner, ct);
            await using var ownerSession = await HostedProjectSession.OpenAsync(folder, Guid.NewGuid(), access, owner, () => Task.FromResult(server.Token(subjects[0])));
            await ownerSession.StartAsync(true, ct);
            var initial = new MemoryStream("Initial project"u8.ToArray());
            var checkpoint = await CheckpointTransfer.PublishAsync(ownerSession.Updates, initial, ct);
            async Task<HostedProjectAccess> InviteAsync(DeviceIdentity device, string subject)
            {
                var link = await HostedProjectInvitations.CreateAsync(access, owner, ownerSession.Transport, new(checkpoint, DateTimeOffset.UtcNow.AddHours(1)), ct);
                var invited = await store.PrepareJoinAsync(link, device, ct);
                await using var transport = new HostedTransport(server.Address, device, () => Task.FromResult(server.Token(subject)));
                await transport.ConnectAsync(ct);
                await store.ActivateAsync(invited, transport, ct);
                return invited;
            }
            using var writerAccess = await InviteAsync(writer, subjects[1]);
            using var removedAccess = await InviteAsync(removed, subjects[2]);
            await using var writerSession = await HostedProjectSession.OpenAsync(folder, Guid.NewGuid(), writerAccess, writer, () => Task.FromResult(server.Token(subjects[1])));
            await using var removedSession = await HostedProjectSession.OpenAsync(folder, Guid.NewGuid(), removedAccess, removed, () => Task.FromResult(server.Token(subjects[2])));
            await writerSession.StartAsync(false, ct);
            await removedSession.StartAsync(false, ct);
            var roster = await ownerSession.Transport.ListProjectDevicesAsync(access.ProjectId, ct);
            var member = roster.Single(value => value.DeviceId == removed.Id).UserId;
            var newBytes = "Fresh project after removal"u8.ToArray();
            await new HostedProjectRotation(folder, owner, ownerSession.Transport).RotateAsync(access, member,
                async (boundary, token) =>
                {
                    removedSession.Updates.Save(Guid.NewGuid(), "Attempt to interrupt removal"u8.ToArray());
                    await Assert.ThrowsAsync<HubException>(() => removedSession.Updates.SynchronizeAsync(false, token));
                    await ownerSession.Updates.SynchronizeAsync(false, token);
                    Assert.Equal(boundary, ownerSession.Updates.LastServerSequence);
                    Assert.Equal(boundary, (await ownerSession.Transport.GetProjectEpochAsync(access.ProjectId, token)).LastServerSeq);
                    return new MemoryStream(newBytes);
                }, ct);
            await Assert.ThrowsAsync<HubException>(() => removedSession.Transport.GetProjectEpochGrantAsync(access.ProjectId, 0, ct));
            var observed = new List<SequencedUpdate>();
            await foreach (var update in ownerSession.Transport.CatchUpAsync(access.ProjectId, 0, ct)) observed.Add(update);
            var newEnvelope = observed.First(value => UpdateHeader.Parse(value.Envelope.Header).Epoch == 1).Envelope;
            using var oldCipher = removedAccess.CreateCipher();
            Assert.Throws<CryptographicException>(() => oldCipher.Open(newEnvelope, owner.SigningKey.PublicKey));
            await ownerSession.EndAsync(ct);
            await using (var stale = await HostedProjectSession.OpenAsync(folder, Guid.NewGuid(), writerAccess, writer, () => Task.FromResult(server.Token(subjects[1]))))
            {
                stale.Updates.Save(Guid.NewGuid(), "Unshared writing with the previous project key"u8.ToArray());
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => stale.StartAsync(false, ct));
                Assert.Equal(SessionConnectionState.KeyRefreshRequired, stale.Status.State);
            }
            await using (var scope = server.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
                var stored = await db.ProjectEpochKeyGrants.SingleAsync(value => value.ProjectId == access.ProjectId && value.DeviceId == writer.Id, ct);
                var original = stored.Payload.ToArray();
                var altered = JsonNode.Parse(original)!;
                altered["Signature"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
                stored.Payload = JsonSerializer.SerializeToUtf8Bytes(altered);
                await db.SaveChangesAsync(ct);
                await Assert.ThrowsAsync<CryptographicException>(() => store.RefreshEpochAsync(writerAccess, writer, writerSession.Transport, ct));
                using var untouched = await store.LoadAsync(server.Address, access.ProjectId, writer, ct);
                Assert.Equal(0U, untouched!.Epoch);
                stored.Payload = original;
                await db.SaveChangesAsync(ct);
            }
            using var refreshed = await store.RefreshEpochAsync(writerAccess, writer, writerSession.Transport, ct);
            Assert.NotNull(refreshed);
            await writerSession.EndAsync(ct);
            await using var resumed = await HostedProjectSession.OpenAsync(folder, Guid.NewGuid(), refreshed, writer, () => Task.FromResult(server.Token(subjects[1])));
            await resumed.StartAsync(false, ct);
            await using var downloaded = new MemoryStream();
            await CheckpointTransfer.DownloadAsync(resumed.Updates, refreshed.Checkpoint!, downloaded, 100000, ct);
            Assert.Equal(newBytes, downloaded.ToArray());
            resumed.Updates.Save(Guid.NewGuid(), "Writer continues with owner offline"u8.ToArray());
            await resumed.Updates.SynchronizeAsync(false, ct);
            using var saved = await store.LoadAsync(server.Address, access.ProjectId, writer, ct);
            Assert.Equal(1U, saved!.Epoch);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    private sealed class Credentials : ICollaborationCredentials
    {
        private readonly Dictionary<string, string> values = [];
        public Task<string?> ReadAsync(string key, CancellationToken ct) => Task.FromResult(values.GetValueOrDefault(key));
        public Task WriteAsync(string key, string value, CancellationToken ct) { values[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken ct) { values.Remove(key); return Task.CompletedTask; }
    }
}
