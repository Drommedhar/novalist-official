using System.Security.Cryptography;
using NSec.Cryptography;
using NSubstitute;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Core.Services;
using Novalist.Sync.Client;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class SharedCodexPersistenceTests
{
    [Theory]
    [InlineData("age", false)]
    [InlineData("Age", false)]
    [InlineData("age", true)]
    public async Task AFocusedFieldKeepsItsBookBindingAfterNavigationAndUsesGuardedSaving(string ordinaryKey, bool worldBible)
    {
        var ct = TestContext.Current.CancellationToken;
        using var temporary = new TempDir();
        using var workspace = new Workspace(Path.Combine(temporary.Path, "settings"));
        await workspace.Projects.CreateProjectAsync(temporary.Path, "Shared novel", "First");
        var firstBook = workspace.Projects.ActiveBook ?? throw new InvalidOperationException();
        var entityId = Guid.NewGuid().ToString();
        var entities = new EntityService(workspace.Projects);
        await entities.SaveCharacterAsync(new() { Id = entityId, Name = "Writer", Age = "42", IsWorldBible = worldBible });
        var secondBook = await workspace.Projects.CreateBookAsync("Second");
        await workspace.Projects.SwitchBookAsync(secondBook.Id);
        await entities.SaveCharacterAsync(new() { Id = entityId, Name = "Other", Age = "20" });
        await workspace.Projects.SwitchBookAsync(firstBook.Id);
        await workspace.Projects.SaveProjectAsync();
        var root = workspace.Projects.ProjectRoot ?? throw new InvalidOperationException();
        var fields = await SharedCodexFields.CaptureAsync(root, ProjectContentSelection.Read(root), ct);
        var project = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        using var sequence = new DeviceSequence(Path.Combine(temporary.Path, "device.sqlite"), Guid.NewGuid());
        using var journal = new SessionJournal(Path.Combine(temporary.Path, "journal.sqlite"), sessionId, project, sequence);
        using var signing = Key.Create(SignatureAlgorithm.Ed25519);
        using var cipher = new EnvelopeCipher(project, 0, RandomNumberGenerator.GetBytes(32));
        var wire = Substitute.For<IUpdateTransport, IFieldLeaseTransport>();
        wire.ConnectionIdentity.Returns("test:codex-fields");
        wire.IsConnected.Returns(true);
        wire.CatchUpAsync(project, Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(EmptyUpdates());
        await using var session = new CollaborationSession(new(project, sequence.DeviceId, 0), journal, cipher, signing,
            new Dictionary<Guid, PublicKey> { [sequence.DeviceId] = signing.PublicKey }, wire);
        await workspace.AttachSharedFieldsAsync(sessionId, session, fields, ct);
        try
        {
            var codex = new EntitiesRpc(workspace);
            var record = await codex.GetAsync("character", entityId);
            var item = record.GetProperty("sharedFields").GetProperty("fields").GetProperty("age").GetGuid();
            var focus = Guid.NewGuid();
            var lease = new FieldLeaseRequest(project, item, 0, focus);
            var now = DateTimeOffset.UtcNow;
            ((IFieldLeaseTransport)wire).AcquireFieldLeaseAsync(lease, Arg.Any<CancellationToken>())
                .Returns(new FieldLeaseGrant(sequence.DeviceId, focus, now.AddSeconds(30), now, 0));
            ((IFieldLeaseTransport)wire).PushFieldEditAsync(lease, Arg.Any<UpdateEnvelope>(), Arg.Any<CancellationToken>()).Returns(1L);
            var collaboration = new CollaborationRpc(workspace);
            Assert.Equal("42", (await collaboration.AcquireFieldAsync(sessionId, item, focus, ct)).Value);
            await Assert.ThrowsAsync<InvalidOperationException>(() => codex.UpdateAsync("character", entityId, new() { [ordinaryKey] = "stale" }));
            await workspace.Projects.SwitchBookAsync(secondBook.Id);
            await collaboration.WriteFieldAsync(sessionId, item, focus, "43", ct);
            Assert.Equal("20", (await codex.GetAsync("character", entityId)).GetProperty("age").GetString());
            Assert.Empty(codex.History(entityId));
            await workspace.Projects.SwitchBookAsync(firstBook.Id);
            Assert.Equal("43", (await codex.GetAsync("character", entityId)).GetProperty("age").GetString());
            var revision = Assert.Single(codex.History(entityId, worldBible));
            await codex.SetLockedAsync("character", entityId, true);
            Assert.Equal(EntitiesRpc.LockedMessage, (await Assert.ThrowsAsync<InvalidOperationException>(
                () => collaboration.AcquireFieldAsync(sessionId, item, focus, ct))).Message);
            Assert.Equal(EntitiesRpc.LockedMessage, (await Assert.ThrowsAsync<InvalidOperationException>(
                () => collaboration.WriteFieldAsync(sessionId, item, focus, "44", ct))).Message);
            var recoveredPath = await collaboration.RecoverFieldAsync(sessionId, item, focus, "44", ct);
            Assert.Equal(recoveredPath, await collaboration.RecoverFieldAsync(sessionId, item, focus, "44", ct));
            using var recovered = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(recoveredPath, ct));
            Assert.Equal("44", recovered.RootElement.GetProperty("value").GetString());
            Assert.Equal("age", recovered.RootElement.GetProperty("field").GetString());
            await collaboration.EndAsync();
            Assert.False((await codex.GetAsync("character", entityId)).TryGetProperty("sharedFields", out _));
            Assert.Equal("43", (await codex.GetAsync("character", entityId)).GetProperty("age").GetString());
            await codex.SetLockedAsync("character", entityId, false);
            var restored = await codex.RestoreRevisionAsync("character", entityId, revision.Id);
            Assert.Equal("42", restored.GetProperty("age").GetString());
            Assert.Equal(worldBible, restored.GetProperty("isWorldBible").GetBoolean());
        }
        finally { await workspace.DetachSharedScenesAsync(); }
    }

    private static async IAsyncEnumerable<SequencedUpdate> EmptyUpdates()
    {
        await Task.CompletedTask;
        yield break;
    }
}
