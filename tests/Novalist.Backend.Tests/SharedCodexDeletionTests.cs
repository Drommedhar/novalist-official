using System.Text.Json;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Sync.Client;
using Novalist.Sync.Client.Tests;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class SharedCodexDeletionTests
{
    [Theory]
    [InlineData("age")]
    [InlineData("aliases")]
    [InlineData("section:notes:title")]
    public async Task UnsentFieldEditsCanBeRecoveredAfterAnotherWriterDeletesTheEntry(string fieldKey)
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var owner = new HostedDesktop(Path.Combine(directory.Path, "owner-settings"), identity);
        var project = await owner.Rpc.InvokeAsync<ProjectStateDto>("project/create", directory.Path, "Shared", "Book");
        var entry = await owner.Rpc.InvokeAsync<JsonElement>("entities/create", "character", "Writer");
        var id = entry.GetProperty("id").GetString();
        await owner.Rpc.InvokeAsync("entities/updateLists", "character", id, null, new[] { new EntitySectionDto("Notes", "Retained section writing.", "notes") }, null);
        await owner.SignInAsync(server.Address);
        var ownerSession = await owner.Rpc.InvokeAsync<HostedSessionDto>("collaboration/create", server.Address);
        var invitation = await owner.Rpc.InvokeAsync<string>("collaboration/invite", DateTimeOffset.UtcNow.AddHours(1));
        identity.Subject = "guest";
        await using var guest = new HostedDesktop(Path.Combine(directory.Path, "guest-settings"), identity);
        await guest.SignInAsync(server.Address);
        var joined = await guest.Rpc.InvokeAsync<HostedSessionDto>("collaboration/join", server.Address, invitation, Path.Combine(directory.Path, "Joined"));
        entry = await guest.Rpc.InvokeAsync<JsonElement>("entities/get", "character", id);
        var field = entry.GetProperty("sharedFields").GetProperty(fieldKey == "age" ? "fields" : "metadata").GetProperty(fieldKey).GetGuid();
        var focus = Guid.NewGuid();
        await guest.Rpc.InvokeAsync<SharedFieldFocus>("collaboration/acquireField", joined.SessionId, field, focus);
        var value = fieldKey == "age" ? "42 after deletion" : fieldKey == "aliases" ? JsonSerializer.Serialize(new[] { "42 after deletion" }) : JsonSerializer.Serialize("42 after deletion");
        if (fieldKey.StartsWith("section:", StringComparison.Ordinal))
            await owner.Rpc.InvokeAsync("entities/updateLists", "character", id, null, Array.Empty<EntitySectionDto>(), null);
        else await owner.Rpc.InvokeAsync("entities/delete", "character", id, false);
        await guest.Rpc.InvokeAsync("collaboration/recoverField", joined.SessionId, field, focus, value);
        var recovery = Path.Combine(directory.Path, "Joined", "Recovered edits");
        for (var attempt = 0; attempt < 100 && (!Directory.Exists(recovery) || !Directory.GetFiles(recovery, "*", SearchOption.AllDirectories).Any(file => File.ReadAllText(file).Contains("42 after deletion", StringComparison.Ordinal))); attempt++)
            await Task.Delay(20, ct);
        Assert.Contains(Directory.GetFiles(recovery, "*", SearchOption.AllDirectories), file => File.ReadAllText(file).Contains("42 after deletion", StringComparison.Ordinal));
        if (fieldKey.StartsWith("section:", StringComparison.Ordinal))
        {
            Assert.Empty((await owner.Rpc.InvokeAsync<JsonElement>("entities/get", "character", id)).GetProperty("sections").EnumerateArray());
            await Assert.ThrowsAsync<RemoteInvocationException>(() => owner.Rpc.InvokeAsync("collaboration/acquireField", ownerSession.SessionId, field, Guid.NewGuid()));
        }
        else Assert.Empty((await owner.Rpc.InvokeAsync<JsonElement>("entities/list", "character")).EnumerateArray());
    }
}
