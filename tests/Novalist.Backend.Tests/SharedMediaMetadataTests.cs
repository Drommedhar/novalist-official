using System.Text.Json;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Sync.Client.Tests;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class SharedMediaMetadataTests
{
    [Fact]
    public async Task MaterializingInheritedImagesBindsTheirScopedNamesBeforeTheNextRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        using var directory = new TempDir();
        await using var identity = await TestIdentityProvider.StartAsync(ct);
        await using var server = await HostedSessionServer.StartAsync(identity.Authority, ct);
        await using var desktop = new HostedDesktop(Path.Combine(directory.Path, "settings"), identity);
        await desktop.Rpc.InvokeAsync("project/create", directory.Path, "Shared", "Book");
        var chapter = Assert.Single((await desktop.Rpc.InvokeAsync<ProjectStateDto>("project/createChapter", "Opening")).Chapters);
        await desktop.Rpc.InvokeAsync("project/createScene", chapter.Guid, "Arrival");
        var character = await desktop.Rpc.InvokeAsync<JsonElement>("entities/create", "character", "Mira");
        var id = character.GetProperty("id").GetString();
        var imagePath = Path.Combine(directory.Path, "portrait.png");
        await File.WriteAllBytesAsync(imagePath, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="), ct);
        character = await desktop.Rpc.InvokeAsync<JsonElement>("entities/addImage", "character", id, imagePath, true);
        var image = character.GetProperty("images")[0];
        await desktop.Rpc.InvokeAsync("entities/setOverride", id, chapter.Guid, "Arrival", new { age = "27" });
        await desktop.SignInAsync(server.Address);
        await desktop.Rpc.InvokeAsync("collaboration/create", server.Address);
        var invitation = await desktop.Rpc.InvokeAsync<string>("collaboration/invite", DateTimeOffset.UtcNow.AddHours(1));
        identity.Subject = "guest";
        await using var guest = new HostedDesktop(Path.Combine(directory.Path, "guest-settings"), identity);
        await guest.SignInAsync(server.Address);
        var joined = await guest.Rpc.InvokeAsync<HostedSessionDto>("collaboration/join", server.Address, invitation, Path.Combine(directory.Path, "Joined"));
        var guestRecord = await guest.Rpc.InvokeAsync<JsonElement>("entities/get", "character", id);
        var age = guestRecord.GetProperty("sharedFields").GetProperty("metadata").GetProperty($"override:{chapter.Guid}:Arrival:age").GetGuid();
        var focus = Guid.NewGuid();
        focus = Guid.NewGuid();
        await guest.Rpc.InvokeAsync("collaboration/acquireField", joined.SessionId, age, focus);
        await guest.Rpc.InvokeAsync("collaboration/writeField", joined.SessionId, age, focus, "\"28\"");
        await guest.Rpc.InvokeAsync("collaboration/releaseField", joined.SessionId, age, focus);
        await WaitAsync(async () => (await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "character", id)).GetProperty("chapterOverrides")[0].GetProperty("age").GetString() == "28", ct);

        await desktop.Rpc.InvokeAsync("entities/setOverrideImages", id, chapter.Guid, "Arrival",
            new[] { new EntityImageDto(image.GetProperty("name").GetString()!, image.GetProperty("path").GetString()!) });

        character = await desktop.Rpc.InvokeAsync<JsonElement>("entities/get", "character", id);
        var metadata = character.GetProperty("sharedFields").GetProperty("metadata");
        Assert.True(metadata.TryGetProperty($"override:{chapter.Guid}:Arrival:image:{image.GetProperty("path").GetString()}:name", out var binding),
            string.Join(", ", metadata.EnumerateObject().Where(property => property.Name.Contains("image:", StringComparison.Ordinal)).Select(property => property.Name)));
        Assert.NotEqual(Guid.Empty, binding.GetGuid());
        await WaitAsync(async () => (await guest.Rpc.InvokeAsync<JsonElement>("entities/get", "character", id)).GetProperty("sharedFields").GetProperty("metadata")
            .TryGetProperty($"override:{chapter.Guid}:Arrival:image:{image.GetProperty("path").GetString()}:name", out _), ct);
        guestRecord = await guest.Rpc.InvokeAsync<JsonElement>("entities/get", "character", id);
        Assert.Equal("28", guestRecord.GetProperty("chapterOverrides")[0].GetProperty("age").GetString());
        Assert.Single(guestRecord.GetProperty("chapterOverrides")[0].GetProperty("images").EnumerateArray());
    }

    private static async Task WaitAsync(Func<Task<bool>> condition, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline) { if (await condition()) return; await Task.Delay(20, ct); }
        Assert.True(await condition());
    }
}
