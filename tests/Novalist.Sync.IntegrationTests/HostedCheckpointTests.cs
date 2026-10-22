using System.Security.Cryptography;
using Novalist.Sync.Client;
using NSec.Cryptography;
using Xunit;

namespace Novalist.Sync.IntegrationTests;

public sealed class HostedCheckpointTests(CommunityServerFixture server) : IClassFixture<CommunityServerFixture>
{
    [Fact]
    public async Task ANewMemberDownloadsACompleteProjectAfterTheOwnerGoesOffline()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = Guid.NewGuid();
        var ownerDevice = Guid.NewGuid();
        var guestDevice = Guid.NewGuid();
        var subject = Guid.NewGuid().ToString();
        var folder = Path.Combine(Path.GetTempPath(), "novalist-checkpoint-relay-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            using var ownerKey = Key.Create(SignatureAlgorithm.Ed25519);
            using var guestKey = Key.Create(SignatureAlgorithm.Ed25519);
            using var cipher = new EnvelopeCipher(project, 0, RandomNumberGenerator.GetBytes(32));
            using var ownerSequence = new DeviceSequence(Path.Combine(folder, "owner-device.sqlite"), ownerDevice);
            using var guestSequence = new DeviceSequence(Path.Combine(folder, "guest-device.sqlite"), guestDevice);
            using var ownerJournal = new SessionJournal(Path.Combine(folder, "owner.sqlite"), Guid.NewGuid(), project, ownerSequence);
            using var guestJournal = new SessionJournal(Path.Combine(folder, "guest.sqlite"), Guid.NewGuid(), project, guestSequence);
            await using var ownerTransport = new HostedTransport(server.Address, ownerDevice, () => Task.FromResult(server.Token("owner-" + subject)));
            await using var guestTransport = new HostedTransport(server.Address, guestDevice, () => Task.FromResult(server.Token(subject)));
            var writers = new Dictionary<Guid, PublicKey> { [ownerDevice] = ownerKey.PublicKey, [guestDevice] = guestKey.PublicKey };
            await using var owner = new CollaborationSession(new(project, ownerDevice, 0), ownerJournal, cipher, ownerKey, writers, ownerTransport);
            await using var guest = new CollaborationSession(new(project, guestDevice, 0), guestJournal, cipher, guestKey, writers, guestTransport);
            await owner.SynchronizeAsync(createProject: true, ct);
            await guestTransport.ConnectAsync(ct);
            await server.AddContributorAsync(project, subject, ct);
            await guest.PauseAsync(ct);
            var source = Path.Combine(folder, "original");
            Directory.CreateDirectory(Path.Combine(source, ".novalist"));
            Directory.CreateDirectory(Path.Combine(source, "Research"));
            await File.WriteAllTextAsync(Path.Combine(source, ".novalist/project.json"), "{}", ct);
            var attachment = RandomNumberGenerator.GetBytes(1_100_000);
            await File.WriteAllBytesAsync(Path.Combine(source, "Research/map.png"), attachment, ct);
            using var archive = new MemoryStream();
            await ProjectCheckpoint.CreateAsync(source, archive, () => [".novalist/project.json", "Research/map.png"], ct);
            archive.Position = 0;
            var reference = await CheckpointTransfer.PublishAsync(owner, archive, ct);
            await owner.PauseAsync(ct);

            await guest.SynchronizeAsync(createProject: false, ct);
            using var downloaded = new MemoryStream();
            await CheckpointTransfer.DownloadAsync(guest, reference, downloaded, 2_000_000, ct);
            downloaded.Position = 0;
            var destination = Path.Combine(folder, "guest-project");
            await ProjectCheckpoint.InstallAsync(downloaded, destination, 2_000_000, ct);
            Assert.Equal(attachment, await File.ReadAllBytesAsync(Path.Combine(destination, "Research/map.png"), ct));
            Assert.Equal("{}", await File.ReadAllTextAsync(Path.Combine(destination, ".novalist/project.json"), ct));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
