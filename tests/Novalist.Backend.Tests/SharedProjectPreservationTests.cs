using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Sync.Client;
using Novalist.Sync.Client.Tests;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class SharedProjectPreservationTests
{
    [Fact]
    public async Task SharingAndJoiningPreserveEveryUneditedProjectFile()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var owner = new HostedDesktop(directory.Combine("owner-settings"), identity);
        var project = await owner.Rpc.InvokeAsync<ProjectStateDto>("project/create", directory.Path, "Shared", "Book");
        var chapter = Assert.Single((await owner.Rpc.InvokeAsync<ProjectStateDto>("project/createChapter", "Opening")).Chapters);
        var scene = Assert.Single(Assert.Single((await owner.Rpc.InvokeAsync<ProjectStateDto>("project/createScene", chapter.Guid, "Arrival")).Chapters).Scenes);
        await owner.Rpc.InvokeAsync("scenes/write", chapter.Guid, scene.Id, "<p>Initial draft.</p>", "Initial draft.");
        await owner.Rpc.InvokeAsync("research/save", null, "Note", "Note", "Original prose.", Array.Empty<string>(), Array.Empty<string>());
        var root = project.ProjectPath!;
        var entry = await owner.Rpc.InvokeAsync<JsonElement>("entities/create", "location", "Place");
        var entityId = entry.GetProperty("id").GetString()!;
        var entityPath = Path.Combine(root, "Book/Locations", entityId + ".json");
        var entity = JsonNode.Parse(await File.ReadAllBytesAsync(entityPath, ct))!;
        entity["sections"] = new JsonArray(
            new JsonObject { ["title"] = "Legacy section", ["content"] = "Original section prose." },
            new JsonObject { ["title"] = "Other section", ["content"] = "Other original prose." });
        await File.WriteAllTextAsync(entityPath, entity.ToJsonString(), ct);
        foreach (var relative in ProjectContentSelection.Read(root).Where(path => path.EndsWith(".json", StringComparison.Ordinal)))
        {
            var path = Path.Combine(root, relative);
            await File.WriteAllTextAsync(path, JsonNode.Parse(await File.ReadAllBytesAsync(path, ct))!.ToJsonString() + "\n", ct);
        }
        var original = await HashFilesAsync(root, ct);
        await owner.SignInAsync(server.Address);

        await owner.Rpc.InvokeAsync("collaboration/create", server.Address);

        Assert.Empty(ChangedFiles(original, await HashFilesAsync(root, ct)));
        var invitation = await owner.Rpc.InvokeAsync<string>("collaboration/invite", DateTimeOffset.UtcNow.AddHours(1));
        identity.Subject = "guest";
        await using var guest = new HostedDesktop(directory.Combine("guest-settings"), identity);
        await guest.SignInAsync(server.Address);
        var destination = directory.Combine("Joined");
        await guest.Rpc.InvokeAsync("collaboration/join", server.Address, invitation, destination);
        Assert.DoesNotContain(ChangedFiles(original, await HashFilesAsync(destination, ct)), path => path != ".novalist/settings.json");
        Assert.Equal(await SharedProjectFiles.FilterContentAsync(".novalist/settings.json", await File.ReadAllBytesAsync(Path.Combine(root, ".novalist/settings.json"), ct), ct),
            await SharedProjectFiles.FilterContentAsync(".novalist/settings.json", await File.ReadAllBytesAsync(Path.Combine(destination, ".novalist/settings.json"), ct), ct));
        Assert.Empty(ChangedFiles(original, await HashFilesAsync(root, ct)));
        var sharedEntry = await guest.Rpc.InvokeAsync<JsonElement>("entities/get", "location", entityId);
        var sections = sharedEntry.GetProperty("sections").EnumerateArray().ToArray();
        var firstId = sections[0].GetProperty("id").GetString();
        var secondId = sections[1].GetProperty("id").GetString();
        var field = sharedEntry.GetProperty("sharedProse").GetProperty("fields").GetProperty("section:" + firstId).GetGuid();
        var session = sharedEntry.GetProperty("sharedProse").GetProperty("sessionId").GetGuid();
        await guest.Rpc.InvokeAsync("collaboration/writeScene", session, field, Encoding.UTF8.GetBytes("text:Edited section prose."));
        var saved = JsonNode.Parse(await File.ReadAllBytesAsync(Path.Combine(destination, "Book/Locations", entityId + ".json"), ct))!["sections"]!.AsArray();
        Assert.Equal(new[] { firstId, secondId }, saved.Select(section => section!["id"]!.GetValue<string>()));
        await guest.Rpc.InvokeAsync("entities/updateLists", "location", entityId, null,
            new EntitySectionDto[] { new("Other section", null, secondId), new("Inserted", "New prose."), new("Legacy section", null, firstId) }, null);
        await guest.Rpc.InvokeAsync("collaboration/writeScene", session, field, Encoding.UTF8.GetBytes("text:Edited after reordering."));
        sharedEntry = await guest.Rpc.InvokeAsync<JsonElement>("entities/get", "location", entityId);
        sections = sharedEntry.GetProperty("sections").EnumerateArray().ToArray();
        Assert.Equal(firstId, sections[2].GetProperty("id").GetString());
        Assert.Equal("Edited after reordering.", sections[2].GetProperty("content").GetString());
        Assert.Equal("Other original prose.", sections[0].GetProperty("content").GetString());
    }

    private static async Task<SortedDictionary<string, string>> HashFilesAsync(string root, CancellationToken ct)
    {
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in ProjectContentSelection.Read(root))
            hashes[path] = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(root, path), ct)));
        return hashes;
    }

    private static IEnumerable<string> ChangedFiles(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after) =>
        before.Keys.Union(after.Keys).Where(path => before.GetValueOrDefault(path) != after.GetValueOrDefault(path));
}
