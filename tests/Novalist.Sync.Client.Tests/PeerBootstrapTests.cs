using System.Net;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using NSubstitute;
using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class PeerBootstrapTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "novalist-bootstrap-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MatchingLocalCopyTransfersNoProjectBytesAndDoesNotEnterEditHistory()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = await ProjectAsync("owner", ct);
        var destination = await ProjectAsync("guest", ct);
        using var owner = await DeviceAsync("owner", ct);
        using var guest = await DeviceAsync("guest", ct);
        await using var host = await HostAsync(owner, ct);
        host.SetBootstrap(await ArchiveAsync(source, ct));
        await using var transport = await TcpPeerTransport.JoinInvitationAsync(host.CreateInvitation("127.0.0.1"), guest, ct);
        using var reconstructed = new MemoryStream();
        var progress = new TransferProgress();

        await PeerBootstrapTransfer.DownloadAsync(transport, destination, reconstructed, progress, ct);

        var complete = Assert.Single(progress.Values, value => value.Phase == "complete");
        Assert.Equal(2, complete.ReusedFiles);
        Assert.Equal(0, complete.TotalBytes);
        Assert.Equal(0, complete.CompletedBytes);
        Assert.Equal("Scene content", await File.ReadAllTextAsync(Path.Combine(destination, "Book/scene.txt"), ct));
        await transport.JoinAsync(transport.Access.ProjectId, ct);
        Assert.Empty(await transport.CatchUpAsync(transport.Access.ProjectId, 0, ct).ToListAsync(ct));
    }

    private async Task<string> ProjectAsync(string name, CancellationToken ct)
    {
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(path, ".novalist"));
        Directory.CreateDirectory(Path.Combine(path, "Book"));
        await File.WriteAllTextAsync(Path.Combine(path, ".novalist/project.json"),
            """{"id":"shared-project","books":[{"folderName":"Book"}],"worldBibleFolder":"World Bible"}""", ct);
        await File.WriteAllTextAsync(Path.Combine(path, "Book/scene.txt"), "Scene content", ct);
        return path;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JoiningDoesNotOverwriteAnUnrelatedProjectOrAnArbitraryNonemptyFolder(bool project)
    {
        var ct = TestContext.Current.CancellationToken;
        var source = await ProjectAsync("owner", ct);
        var destination = Path.Combine(root, "unrelated");
        Directory.CreateDirectory(destination);
        if (project)
        {
            await ProjectAsync("unrelated", ct);
            await File.WriteAllTextAsync(Path.Combine(destination, ".novalist/project.json"),
                """{"id":"different-project","books":[],"worldBibleFolder":"World Bible"}""", ct);
        }
        await File.WriteAllTextAsync(Path.Combine(destination, "sentinel.txt"), "Unrelated local content", ct);
        await using var archive = File.OpenRead(await ArchiveAsync(source, ct));

        await Assert.ThrowsAsync<IOException>(() => ProjectCheckpoint.InstallOrUpdateAsync(archive, destination, ct));

        Assert.Equal("Unrelated local content", await File.ReadAllTextAsync(Path.Combine(destination, "sentinel.txt"), ct));
    }

    [Fact]
    public async Task ChangedAndMissingFilesAreTransferredWhileLocalEditsAndPrivateFilesAreRetained()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = await ProjectAsync("owner", ct);
        var destination = await ProjectAsync("guest", ct);
        await File.WriteAllTextAsync(Path.Combine(source, "Book/new.txt"), "New", ct);
        await File.WriteAllTextAsync(Path.Combine(destination, "Book/scene.txt"), "My offline scene", ct);
        await File.WriteAllTextAsync(Path.Combine(destination, "Book/offline.txt"), "My offline addition", ct);
        Directory.CreateDirectory(Path.Combine(destination, ".git"));
        await File.WriteAllTextAsync(Path.Combine(destination, ".git/config"), "Private git configuration", ct);
        await File.WriteAllTextAsync(Path.Combine(destination, ".novalist/credentials.json"), "Private credentials", ct);
        using var owner = await DeviceAsync("owner", ct);
        using var guest = await DeviceAsync("guest", ct);
        await using var host = await HostAsync(owner, ct);
        host.SetBootstrap(await ArchiveAsync(source, ct));
        await using var transport = await TcpPeerTransport.JoinInvitationAsync(host.CreateInvitation("127.0.0.1"), guest, ct);
        using var reconstructed = new MemoryStream();
        var progress = new TransferProgress();

        await PeerBootstrapTransfer.DownloadAsync(transport, destination, reconstructed, progress, ct);

        var complete = Assert.Single(progress.Values, value => value.Phase == "complete");
        Assert.Equal(1, complete.ReusedFiles);
        Assert.Equal(16, complete.CompletedBytes);
        Assert.Equal("Scene content", await File.ReadAllTextAsync(Path.Combine(destination, "Book/scene.txt"), ct));
        Assert.Equal("New", await File.ReadAllTextAsync(Path.Combine(destination, "Book/new.txt"), ct));
        Assert.False(File.Exists(Path.Combine(destination, "Book/offline.txt")));
        var recovery = Assert.Single(Directory.GetDirectories(Path.Combine(destination, "Recovered edits")));
        Assert.Equal("My offline scene", await File.ReadAllTextAsync(Path.Combine(recovery, "Book/scene.txt"), ct));
        Assert.Equal("My offline addition", await File.ReadAllTextAsync(Path.Combine(recovery, "Book/offline.txt"), ct));
        Assert.Equal("Private git configuration", await File.ReadAllTextAsync(Path.Combine(destination, ".git/config"), ct));
        Assert.Equal("Private credentials", await File.ReadAllTextAsync(Path.Combine(destination, ".novalist/credentials.json"), ct));
    }

    private async Task<string> ArchiveAsync(string source, CancellationToken ct)
    {
        var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
        await using var stream = File.Create(path);
        await ProjectCheckpoint.CreateAsync(source, stream, () => ProjectContentSelection.Read(source), ct);
        return path;
    }

    [Fact]
    public async Task LargeFilesAndRequiredDocumentStateUseChunkedTransferToANewLocalCopy()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = await ProjectAsync("owner", ct);
        var content = RandomNumberGenerator.GetBytes(1_400_000);
        await File.WriteAllBytesAsync(Path.Combine(source, "Book/asset.bin"), content, ct);
        var state = new SharedDocumentState(Guid.NewGuid(), "Book/scene.txt", [2, 4, 6]);
        var path = Path.Combine(root, "large.zip");
        await using (var output = File.Create(path))
            await ProjectCheckpoint.CreateAsync(source, output, () => ProjectContentSelection.Read(source), [state], ct);
        using var owner = await DeviceAsync("owner", ct);
        using var guest = await DeviceAsync("guest", ct);
        await using var host = await HostAsync(owner, ct);
        host.SetBootstrap(path);
        await using var transport = await TcpPeerTransport.JoinInvitationAsync(host.CreateInvitation("127.0.0.1"), guest, ct);
        using var reconstructed = new MemoryStream();
        var progress = new TransferProgress();
        var destination = Path.Combine(root, "downloaded");

        var installed = await PeerBootstrapTransfer.DownloadAsync(transport, destination, reconstructed, progress, ct);

        Assert.Equal(content, await File.ReadAllBytesAsync(Path.Combine(destination, "Book/asset.bin"), ct));
        Assert.Equal(state.State, Assert.Single(installed.Documents).State);
        var complete = Assert.Single(progress.Values, value => value.Phase == "complete");
        Assert.Equal(complete.TotalBytes, complete.CompletedBytes);
        Assert.Equal(3, complete.CompletedFiles);
        Assert.Equal(0, complete.ReusedFiles);
        Assert.Equal(["scanning", "comparing", "transferring", "installing", "complete"],
            progress.Values.Select(value => value.Phase).Distinct().ToArray());
    }

    [Fact]
    public async Task ChangedRemoteContentFailsItsHashCheckBeforeTouchingTheLocalProject()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = await ProjectAsync("owner", ct);
        var destination = await ProjectAsync("guest", ct);
        await File.WriteAllTextAsync(Path.Combine(destination, "Book/scene.txt"), "Local original", ct);
        var path = await ArchiveAsync(source, ct);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            archive.GetEntry("files/Book/scene.txt")!.Delete();
            await using var entry = archive.CreateEntry("files/Book/scene.txt").Open();
            await entry.WriteAsync("Wrong content"u8.ToArray(), ct);
        }
        using var owner = await DeviceAsync("owner", ct);
        using var guest = await DeviceAsync("guest", ct);
        await using var host = await HostAsync(owner, ct);
        host.SetBootstrap(path);
        await using var transport = await TcpPeerTransport.JoinInvitationAsync(host.CreateInvitation("127.0.0.1"), guest, ct);
        using var reconstructed = new MemoryStream();

        await Assert.ThrowsAsync<InvalidDataException>(() => PeerBootstrapTransfer.DownloadAsync(transport, destination, reconstructed, null, ct));

        Assert.Equal("Local original", await File.ReadAllTextAsync(Path.Combine(destination, "Book/scene.txt"), ct));
        Assert.False(Directory.Exists(Path.Combine(destination, "Recovered edits")));
    }

    [Fact]
    public async Task UnsafeManifestPathsCannotEscapeTheSelectedProject()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = await ProjectAsync("owner", ct);
        var destination = Path.Combine(root, "downloaded");
        var path = await ArchiveAsync(source, ct);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            CheckpointManifest manifest;
            await using (var entry = archive.GetEntry("manifest.json")!.Open())
                manifest = (await JsonSerializer.DeserializeAsync<CheckpointManifest>(entry, cancellationToken: ct))!;
            archive.GetEntry("manifest.json")!.Delete();
            var unsafeFiles = manifest.Files.Select(file => file.Path == "Book/scene.txt" ? file with { Path = "../outside.txt" } : file).ToArray();
            await using var replacement = archive.CreateEntry("manifest.json").Open();
            await JsonSerializer.SerializeAsync(replacement, manifest with { Files = unsafeFiles }, cancellationToken: ct);
        }
        using var owner = await DeviceAsync("owner", ct);
        using var guest = await DeviceAsync("guest", ct);
        await using var host = await HostAsync(owner, ct);
        host.SetBootstrap(path);
        await using var transport = await TcpPeerTransport.JoinInvitationAsync(host.CreateInvitation("127.0.0.1"), guest, ct);
        using var reconstructed = new MemoryStream();

        await Assert.ThrowsAsync<InvalidOperationException>(() => PeerBootstrapTransfer.DownloadAsync(transport, destination, reconstructed, null, ct));

        Assert.False(Directory.Exists(destination));
        Assert.False(File.Exists(Path.Combine(root, "outside.txt")));
    }

    [Fact]
    public async Task ProjectSettingsMergePreservesPrivateLocalPreferences()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = await ProjectAsync("owner", ct);
        var destination = await ProjectAsync("guest", ct);
        await File.WriteAllTextAsync(Path.Combine(source, ".novalist/settings.json"),
            """{"author":"Shared author","overrides":{"bookFontSize":14},"wordCountGoals":{"projectGoal":5000}}""", ct);
        await File.WriteAllTextAsync(Path.Combine(destination, ".novalist/settings.json"),
            """{"author":"Local author","theme":"dark","overrides":{"bookFontSize":12,"locale":"de"},"wordCountGoals":{"dailyGoal":250}}""", ct);
        await using var archive = File.OpenRead(await ArchiveAsync(source, ct));

        await ProjectCheckpoint.InstallOrUpdateAsync(archive, destination, ct);

        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(destination, ".novalist/settings.json"), ct));
        Assert.Equal("Shared author", settings.RootElement.GetProperty("author").GetString());
        Assert.Equal("dark", settings.RootElement.GetProperty("theme").GetString());
        Assert.Equal("de", settings.RootElement.GetProperty("overrides").GetProperty("locale").GetString());
        Assert.Equal(14, settings.RootElement.GetProperty("overrides").GetProperty("bookFontSize").GetInt32());
        Assert.Equal(250, settings.RootElement.GetProperty("wordCountGoals").GetProperty("dailyGoal").GetInt32());
        Assert.Equal(5000, settings.RootElement.GetProperty("wordCountGoals").GetProperty("projectGoal").GetInt32());
    }

    [Fact]
    public async Task FreshSnapshotReusesCurrentFilesAndAppliesOnlyEditsAfterItsBoundary()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = await ProjectAsync("owner", ct);
        var destination = await ProjectAsync("guest", ct);
        using var owner = await DeviceAsync("owner", ct);
        using var guest = await DeviceAsync("guest", ct);
        await using var host = await HostAsync(owner, ct);
        var ownerAccess = host.CreateOwnerAccess("127.0.0.1");
        var ownerTransport = new TcpPeerTransport(ownerAccess, owner);
        await using var ownerSession = await PeerProjectSession.OpenAsync(Path.Combine(root, "owner-session"), Guid.NewGuid(), ownerTransport, owner);
        await ownerSession.StartAsync(true, ct);
        var item = Guid.NewGuid();
        ownerSession.Updates.Save(item, "Before snapshot"u8.ToArray());
        await ownerSession.Updates.SynchronizeAsync(false, ct);
        host.PrepareBootstrap = token => ownerSession.Updates.CaptureSnapshotAsync(async (sequence, snapshotToken) =>
        {
            host.SetBootstrap(await ArchiveAsync(source, snapshotToken), sequence);
            return true;
        }, token);
        await using var transport = await TcpPeerTransport.JoinInvitationAsync(host.CreateInvitation("127.0.0.1"), guest, ct);
        using var reconstructed = new MemoryStream();
        var progress = new TransferProgress(value =>
        {
            if (value.Phase == "transferring") ownerSession.Updates.Save(item, "After snapshot"u8.ToArray());
        });

        await PeerBootstrapTransfer.DownloadAsync(transport, destination, reconstructed, progress, ct);
        await ownerSession.Updates.SynchronizeAsync(false, ct);
        await using var guestSession = await PeerProjectSession.OpenAsync(Path.Combine(root, "guest-session"), Guid.NewGuid(), transport, guest,
            transport.BootstrapSequence);
        await guestSession.StartAsync(false, ct);

        Assert.Equal(1, transport.BootstrapSequence);
        Assert.Equal("After snapshot"u8.ToArray(), Assert.Single(guestSession.Updates.ReadItem(item)));
        Assert.Equal(0, Assert.Single(progress.Values, value => value.Phase == "complete").CompletedBytes);
    }

    [Fact]
    public async Task LockedReplacementRollsBackFilesInstalledEarlierInTheSameJoin()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = await ProjectAsync("owner", ct);
        var destination = await ProjectAsync("guest", ct);
        await File.WriteAllTextAsync(Path.Combine(source, "Book/added.txt"), "New file", ct);
        var locked = Path.Combine(destination, "Book/scene.txt");
        await File.WriteAllTextAsync(locked, "Local original", ct);
        await using var archive = File.OpenRead(await ArchiveAsync(source, ct));
        await using var held = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.Read);

        var failure = await Record.ExceptionAsync(() => ProjectCheckpoint.InstallOrUpdateAsync(archive, destination, ct));
        Assert.True(failure is IOException or UnauthorizedAccessException);

        Assert.False(File.Exists(Path.Combine(destination, "Book/added.txt")));
        Assert.Equal("Local original", await File.ReadAllTextAsync(locked, ct));
    }

    [Fact]
    public async Task ExistingCopyCannotRedirectComparisonThroughASymbolicLink()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = await ProjectAsync("owner", ct);
        var destination = await ProjectAsync("guest", ct);
        var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "scene.txt"), "Private original", ct);
        File.Delete(Path.Combine(destination, "Book/scene.txt"));
        Directory.Delete(Path.Combine(destination, "Book"));
        Directory.CreateSymbolicLink(Path.Combine(destination, "Book"), outside);
        using var owner = await DeviceAsync("owner", ct);
        using var guest = await DeviceAsync("guest", ct);
        await using var host = await HostAsync(owner, ct);
        host.SetBootstrap(await ArchiveAsync(source, ct));
        await using var transport = await TcpPeerTransport.JoinInvitationAsync(host.CreateInvitation("127.0.0.1"), guest, ct);
        using var reconstructed = new MemoryStream();

        await Assert.ThrowsAsync<InvalidDataException>(() => PeerBootstrapTransfer.DownloadAsync(transport, destination, reconstructed, null, ct));

        Assert.Equal("Private original", await File.ReadAllTextAsync(Path.Combine(outside, "scene.txt"), ct));
    }

    [Fact]
    public async Task MatchingCurrentCopyReceivesOnlyTheSmallCanonicalMergePatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = await ProjectAsync("owner", ct);
        var destination = await ProjectAsync("guest", ct);
        var canonical = System.Text.Encoding.UTF8.GetBytes(new string('x', 1_000_000) + "old");
        var current = new string('x', 1_000_000) + "latest";
        await File.WriteAllTextAsync(Path.Combine(source, "Book/prose.txt"), current, ct);
        await File.WriteAllTextAsync(Path.Combine(destination, "Book/prose.txt"), current, ct);
        var state = new SharedProjectState([], []) { FileBaselines = new Dictionary<string, byte[]> { ["Book/prose.txt"] = canonical } };
        var path = Path.Combine(root, "baselines.zip");
        await using (var output = File.Create(path))
            await ProjectCheckpoint.CreateAsync(source, output, () => ProjectContentSelection.Read(source), state, ct);
        using var owner = await DeviceAsync("owner", ct);
        using var guest = await DeviceAsync("guest", ct);
        await using var host = await HostAsync(owner, ct);
        host.SetBootstrap(path);
        await using var transport = await TcpPeerTransport.JoinInvitationAsync(host.CreateInvitation("127.0.0.1"), guest, ct);
        using var reconstructed = new MemoryStream();
        var progress = new TransferProgress();

        var installed = await PeerBootstrapTransfer.DownloadAsync(transport, destination, reconstructed, progress, ct);

        Assert.NotNull(installed.FileBaselines);
        Assert.Equal(canonical, installed.FileBaselines["Book/prose.txt"]);
        Assert.Equal(current, await File.ReadAllTextAsync(Path.Combine(destination, "Book/prose.txt"), ct));
        var complete = Assert.Single(progress.Values, value => value.Phase == "complete");
        Assert.Equal(3, complete.ReusedFiles);
        Assert.InRange(complete.CompletedBytes, 1, 128);
        await using var session = await PeerProjectSession.OpenAsync(Path.Combine(root, "guest-session"), Guid.NewGuid(), transport, guest,
            transport.BootstrapSequence);
        await session.StartAsync(false, ct);
        var actualFiles = await ProjectCheckpoint.ReadFilesAsync(reconstructed, long.MaxValue, ct);
        await using var files = new SharedProjectFiles(session.Updates, (_, _) => Task.CompletedTask);
        await files.StartAsync(actualFiles, installed, ct);
        Assert.Equal(canonical, (await files.SnapshotAsync(ct))["Book/prose.txt"]);
    }

    private Task<TcpPeerHost> HostAsync(DeviceIdentity owner, CancellationToken ct) =>
        TcpPeerHost.StartAsync(new(IPAddress.Loopback, 0), root,
            new(Guid.NewGuid(), 0, RandomNumberGenerator.GetBytes(32), owner), ct);

    private static Task<DeviceIdentity> DeviceAsync(string name, CancellationToken ct)
    {
        var credentials = Substitute.For<ICollaborationCredentials>();
        credentials.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));
        return new DeviceIdentityStore(credentials).GetOrCreateAsync(new("https://peer.test"), name, ct);
    }

    private sealed class TransferProgress(Action<CollaborationTransferProgress>? report = null) : IProgress<CollaborationTransferProgress>
    {
        public List<CollaborationTransferProgress> Values { get; } = [];
        public void Report(CollaborationTransferProgress value) { Values.Add(value); report?.Invoke(value); }
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
