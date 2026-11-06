using System.Security.Cryptography;
using System.Text;
using NSubstitute;
using NSec.Cryptography;
using Xunit;

namespace Novalist.Sync.Client.Tests;

public sealed class SharedProjectFilesTests
{
    [Fact]
    public async Task AProjectMutationArrivesAsAWholeAndReplaysAfterReopening()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var peers = new Peers();
        var initial = new Dictionary<string, byte[]> { ["Book/book.json"] = Bytes("{\"title\":\"Before\"}") };
        var firstDisk = new Dictionary<string, byte[]>(initial);
        var secondDisk = new Dictionary<string, byte[]>(initial);
        await using var first = peers.Workspace(0, firstDisk);
        await using var second = peers.Workspace(1, secondDisk);
        await first.StartAsync(initial, ct);
        await second.StartAsync(initial, ct);
        await first.CommitAsync([
            new("Book/book.json", initial["Book/book.json"], Bytes("{\"title\":\"After\"}")),
            new("Research/map.png", null, [0, 17, 255])], ct);
        await peers.Sessions[1].SynchronizeAsync(false, ct);
        await second.RefreshAsync(ct);
        Assert.Equal("{\"title\":\"After\"}", Text(secondDisk["Book/book.json"]));
        Assert.Equal(new byte[] { 0, 17, 255 }, secondDisk["Research/map.png"]);
        var reopenedDisk = new Dictionary<string, byte[]>(initial);
        await using var reopened = peers.Workspace(1, reopenedDisk);
        await reopened.StartAsync(initial, ct);
        Assert.Equal("{\"title\":\"After\"}", Text(reopenedDisk["Book/book.json"]));
        Assert.Equal(new byte[] { 0, 17, 255 }, reopenedDisk["Research/map.png"]);
    }

    [Fact]
    public async Task ConcurrentMetadataChangesPreserveOtherFieldsAndIndependentListEntries()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var peers = new Peers();
        const string path = "Book/book.json";
        var original = Bytes("{\"title\":\"Book\",\"summary\":\"Before\",\"scenes\":[{\"id\":\"a\",\"title\":\"Opening\"}]}");
        var initial = new Dictionary<string, byte[]> { [path] = original };
        var firstDisk = new Dictionary<string, byte[]>(initial);
        var secondDisk = new Dictionary<string, byte[]>(initial);
        await using var first = peers.Workspace(0, firstDisk);
        await using var second = peers.Workspace(1, secondDisk);
        await first.StartAsync(initial, ct);
        await second.StartAsync(initial, ct);
        await first.CommitAsync([new(path, original, Bytes("{\"title\":\"Renamed\",\"summary\":\"Before\",\"scenes\":[{\"id\":\"a\",\"title\":\"Opening\"},{\"id\":\"b\",\"title\":\"Second\"}]}"))], ct);
        await second.CommitAsync([new(path, original, Bytes("{\"title\":\"Book\",\"summary\":\"New summary\",\"scenes\":[{\"id\":\"a\",\"title\":\"Revised opening\"},{\"id\":\"c\",\"title\":\"Third\"}]}"))], ct);
        await peers.Sessions[0].SynchronizeAsync(false, ct);
        await first.RefreshAsync(ct);
        Assert.Equal("{\"title\":\"Renamed\",\"summary\":\"New summary\",\"scenes\":[{\"id\":\"a\",\"title\":\"Revised opening\"},{\"id\":\"b\",\"title\":\"Second\"},{\"id\":\"c\",\"title\":\"Third\"}]}", Text(firstDisk[path]));
        Assert.Equal(firstDisk[path], secondDisk[path]);
    }

    [Fact]
    public async Task NewlySharedScenesKeepTheirSeedWhenMovedAndForgetTheirBindingWhenDeleted()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var peers = new Peers();
        var disk = new Dictionary<string, byte[]>();
        await using var first = peers.Workspace(0, disk);
        await using var second = peers.Workspace(1, new());
        await first.StartAsync(new Dictionary<string, byte[]>(), ct);
        await second.StartAsync(new Dictionary<string, byte[]>(), ct);
        var source = Bytes("A new scene");
        var item = Guid.NewGuid();
        var seed = new SharedDocumentState(item, "Book/Scenes/first.md", [1, 2, 3])
        { SourceSha256 = Convert.ToHexStringLower(SHA256.HashData(source)) };
        await first.CommitAsync([new(seed.Path, null, source)], new([seed], []), ct);
        var moved = seed with { Path = "Book/Scenes/moved.md" };
        await first.CommitAsync([new(seed.Path, source, null), new(moved.Path, null, source)], new([moved], []), ct);
        await peers.Sessions[1].SynchronizeAsync(false, ct);
        await second.RefreshAsync(ct);
        var binding = Assert.Single((await second.SnapshotStateAsync(ct)).Documents);
        Assert.Equal(item, binding.ItemId);
        Assert.Equal(moved.Path, binding.Path);
        Assert.Equal(new byte[] { 1, 2, 3 }, binding.State);
        await first.CommitAsync([new(moved.Path, source, null)], ct);
        await peers.Sessions[1].SynchronizeAsync(false, ct);
        await second.RefreshAsync(ct);
        Assert.Empty((await second.SnapshotStateAsync(ct)).Documents);
    }

    [Fact]
    public async Task DeletingAFileKeepsConcurrentEditsRecoverableWithoutRestoringIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var peers = new Peers();
        const string path = "Research/notes.md";
        var original = Bytes("Original notes");
        var initial = new Dictionary<string, byte[]> { [path] = original };
        var recoveries = new List<SharedFileRecovery>();
        var disk = new Dictionary<string, byte[]>(initial);
        await using var first = peers.Workspace(0, new(initial));
        await using var second = peers.Workspace(1, disk, recoveries);
        await first.StartAsync(initial, ct);
        await second.StartAsync(initial, ct);
        await first.CommitAsync([new(path, original, null)], ct);
        await second.CommitAsync([new(path, original, Bytes("Keep these edits"))], ct);
        Assert.False(disk.ContainsKey(path));
        var recovered = Assert.Single(recoveries);
        Assert.Equal(path, recovered.Path);
        Assert.Equal("Keep these edits", Text(recovered.Content));
        await second.RefreshAsync(ct);
        Assert.Single(recoveries);
        var reopenedRecoveries = new List<SharedFileRecovery>();
        await using var reopened = peers.Workspace(1, new(initial), reopenedRecoveries);
        await reopened.StartAsync(initial, ct);
        Assert.Equal(recovered.Id, Assert.Single(reopenedRecoveries).Id);
    }

    [Fact]
    public async Task ConflictingBinaryAssetsKeepTheConfirmedVersionAndRecoverTheOtherVersion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var peers = new Peers();
        const string path = "Research/map.png";
        var initial = new Dictionary<string, byte[]> { [path] = [0, 1, 2] };
        var recoveries = new List<SharedFileRecovery>();
        var disk = new Dictionary<string, byte[]>(initial);
        await using var first = peers.Workspace(0, new(initial));
        await using var second = peers.Workspace(1, disk, recoveries);
        await first.StartAsync(initial, ct);
        await second.StartAsync(initial, ct);
        await first.CommitAsync([new(path, initial[path], [0, 5, 6])], ct);
        await second.CommitAsync([new(path, initial[path], [0, 7, 8])], ct);
        Assert.Equal(new byte[] { 0, 5, 6 }, disk[path]);
        Assert.Equal(new byte[] { 0, 7, 8 }, Assert.Single(recoveries).Content);
    }

    [Fact]
    public async Task SettingsRemainFilteredEvenWhenAnAuthenticatedWriterSendsPrivateValues()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var peers = new Peers();
        const string path = ".novalist/settings.json";
        var initial = new Dictionary<string, byte[]> { [path] = Bytes("{\"author\":\"Writer\",\"apiKey\":\"local-only\"}") };
        await using var files = peers.Workspace(0, new());
        await files.StartAsync(initial, ct);
        Assert.Equal("{\"author\":\"Writer\"}", Text((await files.SnapshotAsync(ct))[path]));
        var update = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 1,
            Changes = new[] { new SharedFileChange(path, Bytes("{\"author\":\"Writer\"}"), Bytes("{\"author\":\"Coauthors\",\"apiKey\":\"never-install\"}")) },
            AddedState = new SharedProjectState([], [])
        });
        peers.Sessions[0].Save(files.ItemId, update);
        await peers.Sessions[0].SynchronizeAsync(false, ct);
        await files.RefreshAsync(ct);
        Assert.Equal("{\"author\":\"Coauthors\"}", Text((await files.SnapshotAsync(ct))[path]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MovingASceneFromAStaleCopyCannotUndoItsConcurrentDeletion(bool catchUpBeforeSaving)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var peers = new Peers();
        var original = Bytes("Original scene");
        var binding = new SharedDocumentState(Guid.NewGuid(), "Book/Scenes/first.md", [1, 2, 3])
        { SourceSha256 = Convert.ToHexStringLower(SHA256.HashData(original)) };
        var initial = new Dictionary<string, byte[]> { [binding.Path] = original };
        var initialState = new SharedProjectState([binding], []);
        var recoveries = new List<SharedFileRecovery>();
        var disk = new Dictionary<string, byte[]>(initial);
        await using var first = peers.Workspace(0, new(initial));
        await using var second = peers.Workspace(1, disk, recoveries);
        await first.StartAsync(initial, initialState, ct);
        await second.StartAsync(initial, initialState, ct);
        await first.CommitAsync([new(binding.Path, original, null)], ct);
        if (catchUpBeforeSaving)
        {
            await peers.Sessions[1].SynchronizeAsync(false, ct);
            await second.SnapshotAsync(ct);
        }
        var moved = binding with { Path = "Book/Scenes/moved.md" };
        await second.CommitAsync([new(binding.Path, original, null), new(moved.Path, null, original)], new([moved], []), ct);
        Assert.Empty(disk);
        Assert.Equal(original, Assert.Single(recoveries).Content);
        Assert.Empty((await second.SnapshotStateAsync(ct)).Documents);
    }

    [Fact]
    public async Task AConflictingDocumentBindingCannotBlockLaterProjectChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var peers = new Peers();
        var original = Bytes("Original scene");
        var binding = new SharedDocumentState(Guid.NewGuid(), "Book/Scenes/first.md", [1, 2, 3]);
        var initial = new Dictionary<string, byte[]> { [binding.Path] = original };
        var disk = new Dictionary<string, byte[]>(initial);
        await using var files = peers.Workspace(0, disk);
        await files.StartAsync(initial, new([binding], []), ct);
        var bad = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 1,
            Changes = new[] { new SharedFileChange(binding.Path, original, original) },
            AddedState = new SharedProjectState([binding with { ItemId = Guid.NewGuid() }], [])
        });
        peers.Sessions[0].Save(files.ItemId, bad);
        await peers.Sessions[0].SynchronizeAsync(false, ct);
        await files.CommitAsync([new("Research/notes.md", null, Bytes("Valid notes"))], ct);
        Assert.True(files.RejectedUpdates);
        Assert.Equal("Valid notes", Text(disk["Research/notes.md"]));
        Assert.Equal(binding.ItemId, Assert.Single((await files.SnapshotStateAsync(ct)).Documents).ItemId);
    }

    [Theory]
    [InlineData(".novalist/project.json", "{\"worldBibleFolder\":\"../outside\"}")]
    [InlineData(".novalist/project.json", "{\"books\":[{\"folderName\":\"C:/outside\"}]}")]
    [InlineData(".novalist/project.json", "{\"books\":[{\"folderName\":\"Book\",\"drafts\":[{\"folderName\":\"../../outside\"}]}]}")]
    [InlineData("Book/Drafts/default/draft.json", "{\"chapterFolder\":\"../outside\"}")]
    [InlineData("Book/Drafts/default/scenes.json", "{\"chapters\":{\"chapter\":[{\"id\":\"scene\",\"fileName\":\"../../outside.novalist\"}]}}")]
    [InlineData("Book/Drafts/default/Chapters/scene.novalist", "<!--nv v=1 id=../outside -->\n<p>Unsafe identity</p>")]
    public async Task UnsafeLiveStorageReferencesRejectTheWholeBatchAndLeaveExistingFilesUnchanged(string target, string content)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var peers = new Peers();
        var initial = new Dictionary<string, byte[]>
        {
            [".novalist/project.json"] = Bytes("{\"books\":[{\"folderName\":\"Book\",\"drafts\":[{\"folderName\":\"default\"}]}]}"),
            ["Book/Drafts/default/draft.json"] = Bytes("{}"),
            ["Book/Drafts/default/scenes.json"] = Bytes("{\"chapters\":{}}"),
            ["Book/Drafts/default/Chapters/scene.novalist"] = Bytes("<!--nv v=1 id=scene -->\n<p>Existing scene</p>")
        };
        var disk = new Dictionary<string, byte[]>(initial);
        await using var files = peers.Workspace(0, disk);
        await files.StartAsync(initial, ct);
        var batch = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 1,
            Changes = new[] { new SharedFileChange("Research/partial.txt", null, Bytes("Must not be installed")), new(target, initial[target], Bytes(content)) },
            AddedState = new SharedProjectState([], [])
        });
        peers.Sessions[0].Save(files.ItemId, batch);
        await peers.Sessions[0].SynchronizeAsync(false, ct);
        await files.RefreshAsync(ct);
        Assert.True(files.RejectedUpdates);
        Assert.Equal(initial.Count, disk.Count);
        foreach (var (path, original) in initial) Assert.Equal(original, disk[path]);
        await files.CommitAsync([new("Research/valid.txt", null, Bytes("Later valid update"))], ct);
        Assert.Equal("Later valid update", Text(disk["Research/valid.txt"]));
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    private sealed class Peers : IAsyncDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "novalist-file-sync", Guid.NewGuid().ToString("N"));
        private readonly List<IDisposable> resources = [];
        private readonly List<SequencedUpdate> updates = [];
        public CollaborationSession[] Sessions { get; }

        public Peers()
        {
            var project = Guid.NewGuid();
            var devices = new[] { Guid.NewGuid(), Guid.NewGuid() };
            var keys = devices.Select(_ => Key.Create(SignatureAlgorithm.Ed25519)).ToArray();
            resources.AddRange(keys);
            var writers = devices.Select((device, index) => (device, keys[index].PublicKey)).ToDictionary();
            var secret = RandomNumberGenerator.GetBytes(32);
            Sessions = devices.Select((device, index) =>
            {
                var cipher = new EnvelopeCipher(project, 0, secret);
                var sequence = new DeviceSequence(Path.Combine(folder, $"device-{index}.sqlite"), device);
                var journal = new SessionJournal(Path.Combine(folder, $"journal-{index}.sqlite"), Guid.NewGuid(), project, sequence);
                resources.AddRange([cipher, sequence, journal]);
                var wire = Substitute.For<IUpdateTransport, IFieldLeaseTransport>();
                wire.ConnectionIdentity.Returns("test:shared-project-files");
                wire.IsConnected.Returns(true);
                wire.CatchUpAsync(project, Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(call => CatchUp(call.Arg<long>()));
                var leases = (IFieldLeaseTransport)wire;
                leases.CreateFieldLeaseRequestAsync(project, Arg.Any<Guid>(), 0, Arg.Any<CancellationToken>())
                    .Returns(call => new FieldLeaseRequest(project, call.ArgAt<Guid>(1), 0, Guid.NewGuid()));
                leases.AcquireFieldLeaseAsync(Arg.Any<FieldLeaseRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
                {
                    var now = DateTimeOffset.UtcNow;
                    return new FieldLeaseGrant(device, call.Arg<FieldLeaseRequest>().LeaseId, now.AddSeconds(30), now, updates.Count);
                });
                leases.ReleaseFieldLeaseAsync(Arg.Any<FieldLeaseRequest>(), Arg.Any<CancellationToken>()).Returns(true);
                leases.PushFieldEditAsync(Arg.Any<FieldLeaseRequest>(), Arg.Any<UpdateEnvelope>(), Arg.Any<CancellationToken>()).Returns(call =>
                {
                    var number = updates.Count + 1L;
                    updates.Add(new(number, call.Arg<UpdateEnvelope>()));
                    return number;
                });
                wire.PushAsync(Arg.Any<UpdateEnvelope>(), Arg.Any<CancellationToken>()).Returns(call =>
                {
                    var number = updates.Count + 1L;
                    updates.Add(new(number, call.Arg<UpdateEnvelope>()));
                    return number;
                });
                return new CollaborationSession(new(project, device, 0), journal, cipher, keys[index], writers, wire);
            }).ToArray();
        }

        public SharedProjectFiles Workspace(int index, Dictionary<string, byte[]> disk, List<SharedFileRecovery>? recoveries = null) => new(Sessions[index], (projection, _) =>
        {
            foreach (var change in projection.Changes)
                if (change.After is { } content) disk[change.Path] = content;
                else disk.Remove(change.Path);
            recoveries?.AddRange(projection.Recoveries);
            return Task.CompletedTask;
        });

        private async IAsyncEnumerable<SequencedUpdate> CatchUp(long after)
        {
            await Task.CompletedTask;
            foreach (var update in updates.Where(update => update.ServerSeq > after).ToArray()) yield return update;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var session in Sessions) await session.DisposeAsync();
            foreach (var resource in resources.AsEnumerable().Reverse()) resource.Dispose();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}
